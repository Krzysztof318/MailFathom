// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.CodeCoverage;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Embeddings;

/// <summary>Reads the one profile row searches are answered from, which is also where arriving mail is embedded.</summary>
/// <remarks>
/// At most one profile is <see cref="EmbeddingProfileLifecycleState.Active" /> at a time, which a partial unique index
/// over the lifecycle column is what enforces. The generation being built is deliberately out of reach here: a caller
/// on this port is serving a search or embedding mail that has just arrived, and both of those belong to the
/// generation that is complete. The ordering is what makes an ambiguous state readable anyway — serving the newer
/// generation is the safer reading of two rows than picking arbitrarily.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class ActiveEmbeddingProfileReader(MailFathomDbContext dbContext) : IActiveEmbeddingProfileReader
{
    /// <inheritdoc />
    /// <remarks>
    /// Projected onto the identity columns rather than materialized as an entity, because the lifecycle columns and the
    /// fingerprint are not what a caller does anything with, and the row must not be tracked by a context a write will
    /// later join.
    /// </remarks>
    public async Task<RegisteredEmbeddingProfile?> FindActiveProfileAsync(CancellationToken cancellationToken)
    {
        var profile = await dbContext.EmbeddingProfiles
            .AsNoTracking()
            .Where(candidate => candidate.LifecycleState == EmbeddingProfileLifecycleState.Active)
            .OrderByDescending(candidate => candidate.ActivatedAt)
            .Select(candidate => new ActiveProfileRow(
                candidate.Id,
                candidate.Provider,
                candidate.ModelIdentifier,
                candidate.ModelVersion,
                candidate.Dimension,
                candidate.DistanceMetric,
                candidate.InputCharacterLimit,
                candidate.PassageInstruction,
                candidate.NormalizesVector))
            .FirstOrDefaultAsync(cancellationToken);

        return profile is null ? null : Map(profile);
    }

    private static RegisteredEmbeddingProfile Map(ActiveProfileRow profile) => new(
        EmbeddingProfileId.Create(profile.Id),
        EmbeddingProfileIdentity.Create(
            profile.Provider,
            profile.ModelIdentifier,
            profile.ModelVersion,
            profile.Dimension,
            profile.DistanceMetric,
            EmbeddingInputPreparation.Create(
                profile.InputCharacterLimit,
                profile.PassageInstruction,
                profile.NormalizesVector)));

    /// <summary>The identity columns of one profile row, as the projection returns them.</summary>
    private sealed record ActiveProfileRow(
        Guid Id,
        string Provider,
        string ModelIdentifier,
        string? ModelVersion,
        int Dimension,
        EmbeddingDistanceMetric DistanceMetric,
        int InputCharacterLimit,
        string? PassageInstruction,
        bool NormalizesVector);
}
