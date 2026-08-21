// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves that the remote occurrence identity is enforced by PostgreSQL and survives its column mapping.</summary>
/// <remarks>
/// <para>
/// Both claims are ones ADR 0001 deferred to this suite. The repository deduplicates an occurrence it can see, but what
/// makes storing idempotent under two concurrent runs is the unique index: neither run can see the other's uncommitted
/// insert, so the database is the only thing that can refuse the second one.
/// </para>
/// <para>
/// The identity also has to survive its own mapping. UIDVALIDITY and UID are IMAP 32-bit unsigned values modelled as CLR
/// <see cref="uint" />, and PostgreSQL has no unsigned 32-bit integer, so the baseline migration maps both to
/// <c>bigint</c>. A value above <see cref="int.MaxValue" /> is what tells a lossless mapping from one that truncates,
/// and no unit test can observe the difference.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedEmailOccurrenceIdentityTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>The alias this class owns, so its rows are not disturbed by another class's writes.</summary>
    private const string FolderAlias = "occurrence-identity";

    /// <summary>The largest UID IMAP can hand out, which no signed 32-bit column can hold.</summary>
    private const uint HighestUid = uint.MaxValue;

    private const uint DuplicatedUid = 7;

    [Fact]
    public async Task UpsertMetadataAsync_ForTheSameOccurrenceInASecondSession_RewritesTheOneRowAndKeepsItsUnsignedIdentity()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, HighestUid);

        // Act
        var firstCommit = await StoreAsync(services, occurrenceId, "identity-first-run", cancellationToken);
        var secondCommit = await StoreAsync(services, occurrenceId, "identity-second-run", cancellationToken);

        // Assert
        Assert.Equal(PersistenceCommitResult.Committed, firstCommit);
        Assert.Equal(PersistenceCommitResult.Committed, secondCommit);

        var storedRows = await ReadRowsForAsync(services, occurrenceId, cancellationToken);
        var storedRow = Assert.Single(storedRows);
        Assert.Equal("identity-second-run", storedRow.Subject);
        Assert.Equal(SyntheticEmail.UidValidity, storedRow.UidValidity);
        Assert.Equal(HighestUid, storedRow.Uid);

        // One document too: the second run's write of an envelope-only document must recognize the first run's rather
        // than insert a second one under the same key.
        Assert.Equal(1, await CountSearchDocumentsForAsync(services, storedRow.Id, cancellationToken));
    }

    /// <summary>Proves the unique index refuses a duplicate neither writer could have seen.</summary>
    /// <remarks>
    /// The two sessions are deliberately interleaved: the first stages its insert, the second commits its own while that
    /// staged row is invisible to it, and only then does the first commit. That is the shape two overlapping
    /// synchronization runs produce, and the only thing standing between it and two rows naming one remote message is
    /// the index. The violation is not translated into a commit result, because a collision on this constraint means
    /// the occurrence is already stored rather than that this write raced a competing one for the same row.
    /// </remarks>
    [Fact]
    public async Task CommitAsync_WhenTwoSessionsStoreTheSameOccurrence_IsRefusedByTheOccurrenceUniqueIndex()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, DuplicatedUid);

        // Act
        var refusal = await services.InScopeAsync(
            async (stagingScope, token) =>
            {
                await using var stagedSession = await stagingScope
                    .GetRequiredService<IPersistenceSessionFactory>()
                    .BeginSessionAsync(token);

                await UpsertAsync(stagingScope, stagedSession, occurrenceId, "duplicate-staged", token);

                var competingCommit = await StoreAsync(services, occurrenceId, "duplicate-committed", token);
                Assert.Equal(PersistenceCommitResult.Committed, competingCommit);

                return await Record.ExceptionAsync(() => stagedSession.CommitAsync(token));
            },
            cancellationToken);

        // Assert
        var updateFailure = Assert.IsType<DbUpdateException>(refusal);
        var violation = Assert.IsType<PostgresException>(updateFailure.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, violation.SqlState);
        Assert.Equal(PersistenceConstraintNames.StoredEmailOccurrenceUniqueIndexName, violation.ConstraintName);

        var storedRows = await ReadRowsForAsync(services, occurrenceId, cancellationToken);
        var storedRow = Assert.Single(storedRows);
        Assert.Equal("duplicate-committed", storedRow.Subject);
    }

    private static Task<PersistenceCommitResult> StoreAsync(
        OrchestratedMailFathomServices services,
        EmailOccurrenceId occurrenceId,
        string subject,
        CancellationToken cancellationToken) => services.CommitAsync(
            (scope, session, token) => UpsertAsync(scope, session, occurrenceId, subject, token),
            cancellationToken);

    private static Task<StoredEmailId> UpsertAsync(
        IServiceProvider scope,
        IPersistenceSession session,
        EmailOccurrenceId occurrenceId,
        string subject,
        CancellationToken cancellationToken) => scope
            .GetRequiredService<IEmailMetadataRepository>()
            .UpsertMetadataAsync(
                session,
                SyntheticEmail.RemoteMetadataOf(occurrenceId, subject),
                extractedMetadata: null,
                StoredEmailContentAvailability.ExceededSizeLimit,
                cancellationToken);

    /// <summary>Reads every row naming one occurrence, so a duplicate is reported as a count rather than assumed away.</summary>
    private static Task<IReadOnlyList<StoredOccurrenceRow>> ReadRowsForAsync(
        OrchestratedMailFathomServices services,
        EmailOccurrenceId occurrenceId,
        CancellationToken cancellationToken)
    {
        var alias = occurrenceId.FolderResolutionId.Alias.Value;
        var generation = occurrenceId.FolderResolutionId.Generation.Value;
        var uidValidity = occurrenceId.UidValidity.Value;
        var uid = occurrenceId.Uid.Value;

        return services.InScopeAsync(
            async (scope, token) => (IReadOnlyList<StoredOccurrenceRow>)await scope
                .GetRequiredService<MailFathomDbContext>()
                .StoredEmails
                .AsNoTracking()
                .Where(storedEmail => storedEmail.MailFolder.MailboxAccountId == SyntheticMailAccount.AccountId.Value
                    && storedEmail.MailFolder.Alias == alias
                    && storedEmail.MailFolder.ResolutionGeneration == generation
                    && storedEmail.UidValidity == uidValidity
                    && storedEmail.Uid == uid)
                .Select(storedEmail => new StoredOccurrenceRow(
                    storedEmail.Id,
                    storedEmail.Subject,
                    storedEmail.UidValidity,
                    storedEmail.Uid))
                .ToArrayAsync(token),
            cancellationToken);
    }

    private static Task<int> CountSearchDocumentsForAsync(
        OrchestratedMailFathomServices services,
        Guid storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<MailFathomDbContext>()
                .EmailSearchDocuments
                .AsNoTracking()
                .CountAsync(document => document.StoredEmailId == storedEmailId, token),
            cancellationToken);

    /// <summary>The columns this class reads back, projected so no entity graph is materialized.</summary>
    private sealed record StoredOccurrenceRow(Guid Id, string? Subject, uint UidValidity, uint Uid);
}
