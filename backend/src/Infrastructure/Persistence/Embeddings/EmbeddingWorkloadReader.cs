// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Administration;
using MailFathom.Application.Folders;
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
internal sealed class EmbeddingWorkloadReader(
    MailFathomDbContext dbContext,
    EmailAttachmentTextBounds attachmentTextBounds,
    IMailFolderParticipationReader folderParticipation)
    : IEmbeddingWorkloadReader
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

        var awaitingAttachmentReading = this.IdsAwaitingAttachmentReading();

        var searchableEmailCount = await this.SearchableEmails().CountAsync(cancellationToken);

        var outstandingEmailCount = await this.SearchableEmails()
            // Uncut is a body with text and no body passage of its own, rather than a message with no passage at all:
            // email_chunks also holds what an attachment yielded, so a message carrying only those and no body text
            // would otherwise be reported as outstanding on every run for ever, there being no body left to cut.
            .Where(email => (email.SearchDocument != null
                    && email.SearchDocument.BodyText != null
                    && !email.Chunks.Any(chunk => chunk.AttachmentPosition == null))
                // The same reasoning for the other text a message carries: attachments this deployment will read and
                // has not read yet are passages it will pay a provider for, and a message whose body is already cut
                // and embedded matches neither of the clauses either side of this one.
                || awaitingAttachmentReading.Contains(email.Id)
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
    /// The same conditions the embedding sweep selects on, composed from the same tombstone expression so the progress
    /// an operator reads is measured against exactly the mail the sweep will work through. A message an expunge has
    /// been observed for is outside it, because vectors nothing may retrieve are a provider bill with no reader; so is
    /// one whose extraction produced no text, whose attachments yielded none, and which has none left to read, because
    /// nothing about it could ever become a passage. A passage cut from an attachment counts here exactly as a body's
    /// does, since the vector index reaches a message through either — and so does an attachment this deployment is
    /// going to read, because a message counted as outstanding by a filter composed over this one has to be counted as
    /// searchable by it too, or the two aggregates describe different mail.
    /// </remarks>
    private IQueryable<StoredEmailEntity> SearchableEmails()
    {
        var awaitingAttachmentReading = this.IdsAwaitingAttachmentReading();

        return dbContext.StoredEmails
            .AsNoTracking()
            .Where(StoredEmailTombstone.IsNotTombstoned)
            .Where(email => email.Chunks.Any()
                || (email.SearchDocument != null && email.SearchDocument.BodyText != null)
                || awaitingAttachmentReading.Contains(email.Id));
    }

    /// <summary>Names the messages whose attachments this deployment is going to read and has not read yet.</summary>
    /// <remarks>
    /// <para>
    /// Both counts above turn on the same question, and it is narrower than "carries an unread attachment". Reading
    /// attachments is off unless a deployment turned it on, and the pass that would read one selects only mail in a
    /// folder an operator asked to have embedded — so a clause reading the two columns alone would report every
    /// message with an attachment as outstanding for ever on a default deployment, and every message in an unembedded
    /// folder as outstanding on any deployment. Neither would ever leave the count, and
    /// <c>EmbeddingWorkload.IsComplete</c> would never become true on a fully embedded mailbox.
    /// </para>
    /// <para>
    /// The switch is expressed as the folder admission rather than as a branch, because an empty admitted set admits
    /// nothing — which is exactly what a deployment that reads no attachment participates in.
    /// </para>
    /// </remarks>
    private IQueryable<Guid> IdsAwaitingAttachmentReading() => AccountScopedMailFolders.Admitting(
            dbContext.StoredEmails
                .AsNoTracking()
                .Where(StoredEmailTombstone.IsNotTombstoned)
                .Where(email => email.AttachmentCount > 0 && email.AttachmentTextDerivedAt == null),
            attachmentTextBounds.IsEnabled ? folderParticipation.FoldersGeneratingEmbeddings : [])
        .Select(email => email.Id);
}
