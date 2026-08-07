// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.CodeCoverage;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Embeddings;

/// <summary>Reads the one profile row this instance currently embeds into and reads from.</summary>
/// <remarks>
/// At most one profile is <see cref="EmbeddingProfileLifecycleState.Active" /> at a time, which the activation command
/// is what enforces. This reads whichever row is there and takes the most recently activated one if a defect ever left
/// two, because serving the newer generation is the safer reading of an ambiguous state than picking arbitrarily.
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
    public async Task<ActiveEmbeddingProfile?> FindActiveProfileAsync(CancellationToken cancellationToken)
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

    private static ActiveEmbeddingProfile Map(ActiveProfileRow profile) => new(
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
