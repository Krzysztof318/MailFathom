// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Data.Common;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Indexing;
using MailFathom.CodeCoverage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MailFathom.Infrastructure.Persistence.Embeddings;

/// <summary>Builds and removes the partial HNSW index that serves one profile's vectors.</summary>
/// <remarks>
/// <para>
/// This is the one place MailFathom changes the schema outside a migration, and it is deliberate: the index covers one
/// width and one profile, so it cannot be written into a migration that runs before either is known. Both statements
/// are therefore data-definition rather than data-manipulation, which has one consequence an operator has to plan for
/// — creating an index requires owning the table, a right PostgreSQL says is inherent in ownership and cannot be
/// granted on its own, so a deployment that applies its schema as a separate migrating role has to hand
/// <c>email_embeddings</c> to the role MailFathom connects as. Where it has not, the refusal arrives here, named,
/// while exact search goes on answering correctly.
/// </para>
/// <para>
/// Both run on the scoped context, so a caller that has opened a transaction gets them inside it. Neither uses
/// <c>CONCURRENTLY</c>, which PostgreSQL forbids inside a transaction block and which buys nothing at the moment this
/// is called: a profile is activated before its generation holds any vectors, so the build reads an empty predicate and
/// returns immediately, and every vector afterwards enters the index as it is written.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed partial class EmbeddingProfileVectorIndex(
    MailFathomDbContext dbContext,
    ILogger<EmbeddingProfileVectorIndex> logger) : IEmbeddingProfileVectorIndex
{
    /// <inheritdoc />
    public async Task EnsureBuiltAsync(RegisteredEmbeddingProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var indexName = EmbeddingVectorIndexStatements.IndexNameFor(profile.Id);

        await this.ExecuteAsync(
            EmbeddingVectorIndexStatements.CreateIndexFor(profile),
            profile.Id,
            $"The approximate vector index for embedding profile {profile.Id} could not be built. "
                + "The vectors under that profile are unaffected and are searched exactly until it exists.",
            cancellationToken);

        LogIndexBuilt(logger, indexName, profile.Id.Value, profile.Identity.Dimension);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(EmbeddingProfileId profileId, CancellationToken cancellationToken)
    {
        var indexName = EmbeddingVectorIndexStatements.IndexNameFor(profileId);

        await this.ExecuteAsync(
            EmbeddingVectorIndexStatements.DropIndexFor(profileId),
            profileId,
            $"The approximate vector index for embedding profile {profileId} could not be removed. "
                + "It goes on occupying storage for a generation that is no longer read.",
            cancellationToken);

        LogIndexRemoved(logger, indexName, profileId.Value);
    }

    private async Task ExecuteAsync(
        string statement,
        EmbeddingProfileId profileId,
        string operatorSafeMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            // The raw overload rather than the interpolated one, which is the opposite of what every other store here
            // does and is not a choice: PostgreSQL accepts no parameter in a utility statement, so a parameterized
            // `CREATE INDEX` is not a safer way to write this one — it is not a statement PostgreSQL would run.
            // `EmbeddingVectorIndexStatements` is where the text comes from and where that safety is argued and tested.
            await dbContext.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }
        catch (DbException refusal)
        {
            throw new EmbeddingVectorIndexFailedException(operatorSafeMessage, profileId, refusal);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Built the approximate vector index {IndexName} for embedding profile {EmbeddingProfileId} at {Dimension} dimensions.")]
    private static partial void LogIndexBuilt(ILogger logger, string indexName, Guid embeddingProfileId, int dimension);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Removed the approximate vector index {IndexName} for embedding profile {EmbeddingProfileId}.")]
    private static partial void LogIndexRemoved(ILogger logger, string indexName, Guid embeddingProfileId);
}
