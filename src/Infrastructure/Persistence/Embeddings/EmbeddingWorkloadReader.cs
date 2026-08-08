// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Administration;
using MailFathom.CodeCoverage;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Embeddings;

/// <summary>Counts what one vector space still owes, over the passages a search may reach.</summary>
/// <remarks>
/// <para>
/// Deliberately unbounded aggregates. They are what an operator is shown before agreeing to a provider bill, and
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>
/// makes them unconditional for that reason: a count and a sum over tables that already exist are cheap beside the
/// spending they inform. They run once per operator command rather than per unit of work, which is why the reads are
/// left as separate statements a reader can follow instead of being folded into one query with several aggregates.
/// </para>
/// <para>
/// Nothing here reads mail. The passages are counted and their lengths summed; no text, subject, address, or vector
/// leaves the database.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class EmbeddingWorkloadReader(MailFathomDbContext dbContext) : IEmbeddingWorkloadReader
{
    /// <inheritdoc />
    /// <remarks>
    /// The geometry is resolved to a profile row first, and a geometry no row carries is what an activation nobody has
    /// performed looks like: every passage is then outstanding, which is the estimate of a first activation. A geometry
    /// that does carry a row may still hold vectors — a generation being built, or one whose removal a rollback caught
    /// part-way through — and counting against that row is what keeps a rollback from being priced as a first
    /// activation.
    /// </remarks>
    public async Task<EmbeddingWorkload> ReadWorkloadAsync(
        EmbeddingProfileFingerprint geometry,
        CancellationToken cancellationToken)
    {
        var fingerprint = geometry.Value;

        var profileId = await dbContext.EmbeddingProfiles
            .AsNoTracking()
            .Where(candidate => candidate.IdentityFingerprint == fingerprint)
            .Select(candidate => (Guid?)candidate.Id)
            .SingleOrDefaultAsync(cancellationToken);

        var searchableEmailCount = await this.SearchableEmails().CountAsync(cancellationToken);

        var outstandingEmailCount = await this.SearchableEmails()
            .Where(email => !email.Chunks.Any()
                || email.Chunks.Any(chunk => profileId == null
                    || !chunk.Embeddings.Any(vector => vector.EmbeddingProfileId == profileId)))
            .CountAsync(cancellationToken);

        var outstandingPassages = this.SearchableEmails()
            .SelectMany(email => email.Chunks)
            .Where(chunk => profileId == null
                || !chunk.Embeddings.Any(vector => vector.EmbeddingProfileId == profileId));

        return new EmbeddingWorkload(
            searchableEmailCount,
            outstandingEmailCount,
            await outstandingPassages.LongCountAsync(cancellationToken),
            await outstandingPassages.SumAsync(chunk => (long)chunk.Text.Length, cancellationToken));
    }

    /// <summary>Selects the messages a search may reach at all, which is what every count here is taken over.</summary>
    /// <remarks>
    /// The same two conditions the embedding sweep selects on, composed from the same tombstone expression so the
    /// progress an operator reads is measured against exactly the mail the sweep will work through. A message an
    /// expunge has been observed for is outside it, because vectors nothing may retrieve are a provider bill with no
    /// reader; so is one whose extraction produced no text, because nothing about it could ever become a passage.
    /// </remarks>
    private IQueryable<StoredEmailEntity> SearchableEmails() => dbContext.StoredEmails
        .AsNoTracking()
        .Where(StoredEmailTombstone.IsNotTombstoned)
        .Where(email => email.Chunks.Any()
            || (email.SearchDocument != null && email.SearchDocument.BodyText != null));
}
