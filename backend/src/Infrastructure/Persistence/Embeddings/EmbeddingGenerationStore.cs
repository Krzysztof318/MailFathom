// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Generations;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Embeddings;

/// <summary>EF Core state of the generations, and of every transition a profile row makes between them.</summary>
/// <remarks>
/// <para>
/// The lifecycle transitions are issued as set-based updates rather than staged through the change tracker, which is
/// the one place this store departs from the repositories around it and is not a preference. A partial unique index
/// admits one building generation and one serving one, so the switch has to supersede the old row before it promotes
/// the new one; the change tracker gives no order to two updates of one table, and either order is a coin toss that
/// would violate that index half the time. Two statements issued in the caller's transaction are ordered by
/// construction.
/// </para>
/// <para>
/// Nothing here is personal data. A profile describes a model, and the vectors this removes are counted rather than
/// read.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class EmbeddingGenerationStore(MailFathomDbContext dbContext, TimeProvider timeProvider)
    : IEmbeddingGenerationStore
{
    /// <inheritdoc />
    /// <remarks>
    /// One query for both rows, projected onto the identity columns rather than materialized: the partial unique index
    /// admits at most one of each state, so what comes back is at most two rows and neither of them is tracked by a
    /// context a write will later join.
    /// </remarks>
    public async Task<EmbeddingGenerations> ReadGenerationsAsync(CancellationToken cancellationToken)
    {
        var rows = await dbContext.EmbeddingProfiles
            .AsNoTracking()
            .Where(candidate => candidate.LifecycleState == EmbeddingProfileLifecycleState.Active
                || candidate.LifecycleState == EmbeddingProfileLifecycleState.Building)
            .Select(candidate => new GenerationRow(
                candidate.Id,
                candidate.LifecycleState,
                candidate.Provider,
                candidate.ModelIdentifier,
                candidate.ModelVersion,
                candidate.Dimension,
                candidate.DistanceMetric,
                candidate.InputCharacterLimit,
                candidate.PassageInstruction,
                candidate.NormalizesVector))
            .ToArrayAsync(cancellationToken);

        return new EmbeddingGenerations(
            Serving: Map(rows, EmbeddingProfileLifecycleState.Active),
            Building: Map(rows, EmbeddingProfileLifecycleState.Building));
    }

    /// <inheritdoc />
    /// <remarks>
    /// The lookup is by the identity fingerprint, which is an alternate key, so it takes the two-pass helper rather than
    /// <c>FindAsync</c>. A row it finds is re-registered rather than replaced: its identity columns may never move, and
    /// the lifecycle is the only thing this write touches.
    /// </remarks>
    public async Task<RegisteredEmbeddingProfile> RegisterBuildingAsync(
        IPersistenceSession session,
        EmbeddingProfileIdentity identity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var fingerprint = EmbeddingProfileFingerprint.Compute(identity).Value;

        var registered = await TrackedEntityLookup.SinglePendingOrPersistedAsync(
            sessionContext.EmbeddingProfiles,
            sessionContext.EmbeddingProfiles,
            candidate => candidate.IdentityFingerprint == fingerprint,
            cancellationToken);

        if (registered is null)
        {
            registered = new EmbeddingProfileEntity
            {
                Id = Guid.CreateVersion7(),
                Provider = identity.Provider,
                ModelIdentifier = identity.ModelIdentifier,
                ModelVersion = identity.ModelVersion,
                Dimension = identity.Dimension,
                DistanceMetric = identity.DistanceMetric,
                InputCharacterLimit = identity.InputPreparation.InputCharacterLimit,
                PassageInstruction = identity.InputPreparation.PassageInstruction,
                NormalizesVector = identity.InputPreparation.NormalizesVector,
                IdentityFingerprint = fingerprint,
                LifecycleState = EmbeddingProfileLifecycleState.Building,
                RegisteredAt = timeProvider.GetUtcNow(),
            };

            sessionContext.EmbeddingProfiles.Add(registered);
        }
        else
        {
            registered.LifecycleState = EmbeddingProfileLifecycleState.Building;
            registered.SupersededAt = null;
        }

        return new RegisteredEmbeddingProfile(EmbeddingProfileId.Create(registered.Id), identity);
    }

    /// <inheritdoc />
    public async Task<bool> SwitchToAsync(
        IPersistenceSession session,
        EmbeddingProfileId built,
        CancellationToken cancellationToken)
    {
        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var builtId = built.Value;

        if (!await LockWhileBuildingAsync(sessionContext, builtId, cancellationToken))
        {
            return false;
        }

        var switchedAt = timeProvider.GetUtcNow();

        // Superseding first is what the partial unique index requires, and the two statements are ordered because they
        // are issued rather than staged. The row being promoted is excluded from the first, so a switch repeated after
        // a crash between the commit and whatever followed it changes nothing.
        await sessionContext.EmbeddingProfiles
            .Where(candidate => candidate.LifecycleState == EmbeddingProfileLifecycleState.Active
                && candidate.Id != builtId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(candidate => candidate.LifecycleState, EmbeddingProfileLifecycleState.Superseded)
                    .SetProperty(candidate => candidate.SupersededAt, switchedAt),
                cancellationToken);

        await sessionContext.EmbeddingProfiles
            .Where(candidate => candidate.Id == builtId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(candidate => candidate.LifecycleState, EmbeddingProfileLifecycleState.Active)
                    .SetProperty(candidate => candidate.ActivatedAt, switchedAt)
                    .SetProperty(candidate => candidate.SupersededAt, (DateTimeOffset?)null),
                cancellationToken);

        return true;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Narrowed to a row that is still being built, so a cancellation that arrives after the switch has taken the same
    /// generation into service abandons nothing and says so — and one that arrives while the switch is committing waits
    /// on the row that switch has locked rather than passing through it.
    /// </remarks>
    public async Task<bool> AbandonAsync(
        IPersistenceSession session,
        EmbeddingProfileId building,
        CancellationToken cancellationToken)
    {
        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var buildingId = building.Value;
        var abandonedAt = timeProvider.GetUtcNow();

        var abandonedRowCount = await sessionContext.EmbeddingProfiles
            .Where(candidate => candidate.Id == buildingId
                && candidate.LifecycleState == EmbeddingProfileLifecycleState.Building)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(candidate => candidate.LifecycleState, EmbeddingProfileLifecycleState.Superseded)
                    .SetProperty(candidate => candidate.SupersededAt, abandonedAt),
                cancellationToken);

        return abandonedRowCount > 0;
    }

    /// <summary>Takes the row lock the switch needs, and answers whether the generation is still being built.</summary>
    /// <remarks>
    /// Written rather than composed, because the guard is the lock: reading the state without one would leave a
    /// cancellation free to abandon the generation between the supersede and the promote, and the transaction would
    /// commit with nothing serving at all. <c>FOR UPDATE</c> makes that cancellation wait for this transaction and then
    /// find a row that is no longer building, which is the answer its own narrowed update reports.
    /// </remarks>
    private static async Task<bool> LockWhileBuildingAsync(
        MailFathomDbContext sessionContext,
        Guid builtId,
        CancellationToken cancellationToken)
    {
        var lockedState = await sessionContext.Database
            .SqlQuery<string>(
                $"""SELECT "LifecycleState" AS "Value" FROM embedding_profiles WHERE "Id" = {builtId} FOR UPDATE""")
            .SingleOrDefaultAsync(cancellationToken);

        return lockedState == nameof(EmbeddingProfileLifecycleState.Building);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Ordered by when the generation was superseded, so the oldest is emptied first and a second replaced generation
    /// waits its turn rather than interleaving with it.
    /// </remarks>
    public async Task<EmbeddingProfileId?> FindSupersededProfileHoldingVectorsAsync(CancellationToken cancellationToken)
    {
        var profileId = await dbContext.EmbeddingProfiles
            .AsNoTracking()
            .Where(candidate => candidate.LifecycleState == EmbeddingProfileLifecycleState.Superseded)
            .Where(candidate => candidate.Embeddings.Any())
            .OrderBy(candidate => candidate.SupersededAt)
            .Select(candidate => (Guid?)candidate.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return profileId is { } superseded ? EmbeddingProfileId.Create(superseded) : null;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The one statement here that is written rather than composed, and the reason is the shape of a bounded delete
    /// over a whole generation. A limit needs rows chosen, and choosing them by any column would sort every vector the
    /// generation still holds on every batch — quadratic over the run that empties a mailbox's worth of them. Selecting
    /// by <c>ctid</c> lets PostgreSQL stop at the limit while reading the profile's own index, so a batch costs what a
    /// batch is. Every value is a parameter rather than text, so nothing a caller supplies reaches the statement.
    /// </para>
    /// <para>
    /// The lifecycle is re-checked here rather than trusted from the read that chose this generation, because the two
    /// are separate transactions and an activation of the same model in between is exactly the case the rollback
    /// promise covers: a generation that stops being superseded keeps whatever vectors it still holds, and a delete
    /// that had already been decided would charge the operator for re-embedding them. The subquery is uncorrelated, so
    /// PostgreSQL evaluates it once for the statement rather than per row.
    /// </para>
    /// </remarks>
    public async Task<int> RemoveVectorsAsync(
        IPersistenceSession session,
        EmbeddingProfileId profileId,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var supersededProfileId = profileId.Value;
        var supersededState = nameof(EmbeddingProfileLifecycleState.Superseded);

        return await sessionContext.Database.ExecuteSqlAsync(
            $"""
             DELETE FROM email_embeddings
             WHERE ctid IN (
                 SELECT vector.ctid FROM email_embeddings AS vector
                 WHERE vector."EmbeddingProfileId" = {supersededProfileId}
                   AND EXISTS (
                       SELECT 1 FROM embedding_profiles AS generation
                       WHERE generation."Id" = {supersededProfileId}
                         AND generation."LifecycleState" = {supersededState})
                 LIMIT {batchSize})
             """,
            cancellationToken);
    }

    private static RegisteredEmbeddingProfile? Map(
        IReadOnlyList<GenerationRow> rows,
        EmbeddingProfileLifecycleState lifecycleState)
    {
        if (rows.FirstOrDefault(row => row.LifecycleState == lifecycleState) is not { } generation)
        {
            return null;
        }

        return new RegisteredEmbeddingProfile(
            EmbeddingProfileId.Create(generation.Id),
            EmbeddingProfileIdentity.Create(
                generation.Provider,
                generation.ModelIdentifier,
                generation.ModelVersion,
                generation.Dimension,
                generation.DistanceMetric,
                EmbeddingInputPreparation.Create(
                    generation.InputCharacterLimit,
                    generation.PassageInstruction,
                    generation.NormalizesVector)));
    }

    /// <summary>One generation's row, as the projection returns it.</summary>
    private sealed record GenerationRow(
        Guid Id,
        EmbeddingProfileLifecycleState LifecycleState,
        string Provider,
        string ModelIdentifier,
        string? ModelVersion,
        int Dimension,
        EmbeddingDistanceMetric DistanceMetric,
        int InputCharacterLimit,
        string? PassageInstruction,
        bool NormalizesVector);
}
