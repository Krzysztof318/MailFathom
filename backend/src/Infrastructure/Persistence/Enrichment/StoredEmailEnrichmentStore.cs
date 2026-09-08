// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Enrichment;
using MailFathom.Application.Folders;
using MailFathom.Application.Persistence;
using MailFathom.Application.Spam.Gating;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using MailFathom.Infrastructure.Persistence.Spam;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Enrichment;

/// <summary>EF Core state for the derivation the account run performs once the cut in front of it has finished.</summary>
/// <remarks>
/// The selection carries the arrival pipeline's own orderings and one condition of its own. The message has to have
/// been evaluated by the rules and to be settled where it is, because a rule may move it into a folder mapped
/// differently. The classification gate has to admit it, because junk is never derived from. Its folder has to be one
/// an operator asked to have embedded, which is the same admission every other derived-work path applies. And it has to
/// carry every passage it will ever have and no derivation — the first because a mark cites passages and a derivation
/// is never taken twice, the second because writing the record is what removes the message from this query.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class StoredEmailEnrichmentStore(
    MailFathomDbContext dbContext,
    IMailFolderParticipationReader folderParticipation,
    EmailAttachmentTextBounds attachmentTextBounds,
    DerivedWorkGate derivedWorkGate)
    : IStoredEmailEnrichmentStore
{
    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Ordering is by the primary key, which is total, stable, and already indexed. No resume position travels with the
    /// batch, because writing a message's record is what takes it out of this query: a pass that repeats the read after
    /// committing sees the next messages rather than the ones it just derived from.
    /// </para>
    /// <para>
    /// A message's passages are ordered body first and only then by attachment, because an attachment's ordinals run
    /// from zero alongside the body's rather than after them, so the ordinal alone puts no order between a body passage
    /// and an attachment passage carrying the same number. Taking the leading passages by ordinal alone would hand a
    /// derivation an arbitrary mix, and on a message with several attachments it could hand it no body text at all —
    /// which is the opposite of the opening this derivation reads a message for.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<EnrichableEmail>> GetEmailsAwaitingEnrichmentAsync(
        MailAccountIdentity account,
        int batchSize,
        int maximumPassagesPerEmail,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPassagesPerEmail);

        var userId = account.User.Value;
        var mailboxAccountId = account.Id.Value;
        var terms = derivedWorkGate.ReadTerms();

        var rows = await Selecting(
                dbContext.StoredEmails.AsNoTracking(),
                userId,
                mailboxAccountId,
                folderParticipation.FoldersGeneratingEmbeddings,
                attachmentTextBounds.IsEnabled,
                terms)
            .OrderBy(email => email.Id)
            .Take(batchSize)
            .Select(email => new EnrichableEmailRow(
                email.Id,
                email.Subject,
                email.ReceivedAt,
                email.Chunks
                    .OrderBy(chunk => chunk.AttachmentPosition == null ? 0 : 1)
                    .ThenBy(chunk => chunk.AttachmentPosition)
                    .ThenBy(chunk => chunk.Ordinal)
                    .Take(maximumPassagesPerEmail)
                    .Select(chunk => new EnrichablePassageRow(chunk.Id, chunk.Ordinal, chunk.Text))
                    .ToList()))
            .ToArrayAsync(cancellationToken);

        return
        [
            .. rows.Select(static row => new EnrichableEmail(
                StoredEmailId.Create(row.Id),
                row.Subject,
                row.ReceivedAt,
                [
                    .. row.Passages.Select(static passage => new EnrichablePassage(
                        EmailChunkId.Create(passage.Id),
                        passage.Ordinal,
                        passage.Text)),
                ])),
        ];
    }

    /// <inheritdoc />
    /// <remarks>
    /// The record is written whole rather than merged: a derivation is one answer about one message, so a second one
    /// replaces the first's marks instead of adding to them. Loading the existing marks is what makes that a delete and
    /// an insert EF Core can stage, rather than a write that would leave a superseded reading beside the current one.
    /// </remarks>
    public async Task SaveAsync(
        IPersistenceSession session,
        EmailEnrichment enrichment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(enrichment);

        var context = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var storedEmailId = enrichment.StoredEmailId.Value;

        var existing = await context.EmailEnrichments
            .Include(record => record.Marks)
            .FirstOrDefaultAsync(record => record.StoredEmailId == storedEmailId, cancellationToken);

        if (existing is null)
        {
            existing = new EmailEnrichmentEntity { StoredEmailId = storedEmailId };
            context.EmailEnrichments.Add(existing);
        }
        else
        {
            context.EmailEnrichmentMarks.RemoveRange(existing.Marks);
            existing.Marks.Clear();
        }

        existing.DerivedAt = enrichment.DerivedAt;

        foreach (var mark in enrichment.Marks)
        {
            existing.Marks.Add(new EmailEnrichmentMarkEntity
            {
                StoredEmailId = storedEmailId,
                Aspect = mark.Aspect,
                Text = mark.Text,
                Reason = mark.Reason,
                DueAt = mark.DueAt,
                Source = mark.Provenance.Source,
                Origin = mark.Provenance.Origin,
                Evidence = [.. mark.Evidence.Select(static passage => passage.Value)],
            });
        }
    }

    /// <summary>Narrows stored mail to the messages the arrival pipeline still owes a derivation for.</summary>
    /// <param name="emails">The emails to narrow.</param>
    /// <param name="userId">The user whose account this pass belongs to, which is what the index leads with.</param>
    /// <param name="mailboxAccountId">The configured account this pass belongs to.</param>
    /// <param name="embeddedFolders">The folders a mapping admits to derived work.</param>
    /// <param name="readsAttachments">Whether this deployment reads attachments, which decides whether to wait for one.</param>
    /// <param name="terms">The classification terms the whole batch is decided under.</param>
    /// <returns>The narrowed query, which PostgreSQL evaluates in full.</returns>
    /// <remarks>
    /// <para>
    /// Written as a composable predicate rather than inline, so what selects a message is one statement that can be
    /// asserted about directly. It carries the cut's own orderings and adds the two this stage is last for: a message
    /// is derived from once every passage it will ever have exists, and the record this store writes is what removes it
    /// from the query.
    /// </para>
    /// <para>
    /// Having a passage is not that condition. The two passes in front of this one walk their own queues from their own
    /// fronts, so neither's batch bounds the other's, and a message can carry an attachment's passages while its body
    /// is still uncut or carry its body's while an attachment it names is still unread. A derivation settles
    /// permanently, so deriving from either half alone would record a reading of a message this deployment had not
    /// finished cutting, and nothing would ever revisit it. Both clauses are therefore written as a negation of the
    /// selecting condition of the pass they wait for — and the attachment one is satisfied outright where the
    /// deployment reads no attachment, because it would otherwise hold every message carrying one out of this queue for
    /// ever.
    /// </para>
    /// <para>
    /// The search document is required to exist before either of those clauses is read, and that is a third condition
    /// rather than part of the first. Extraction writes a document for every message it reaches, including one whose
    /// body it could not read, so an absent document means extraction has not reached the message at all — which the
    /// backfill walking every message without one shows can go on across runs. Attachment reading asks nothing about
    /// the document, so such a message can already carry an attachment's passages, and folding the existence test into
    /// the negation would admit it: the cut would read as finished because the document is missing rather than because
    /// the body is cut, and the derivation would settle permanently on attachment text for a message whose own words
    /// arrive on a later run.
    /// </para>
    /// </remarks>
    internal static IQueryable<StoredEmailEntity> Selecting(
        IQueryable<StoredEmailEntity> emails,
        Guid userId,
        string mailboxAccountId,
        IReadOnlyList<MailFolderIdentity> embeddedFolders,
        bool readsAttachments,
        DerivedWorkAdmissionTerms terms) => DerivedWorkAdmittedEmails.Admitting(
        AccountScopedMailFolders.Admitting(
            emails
                .Where(StoredEmailTombstone.IsNotTombstoned)
                .Where(email => email.UserId == userId
                    && email.MailboxAccountId == mailboxAccountId
                    && email.Chunks.Any()
                    && email.Enrichment == null
                    && email.SearchDocument != null
                    && !(email.SearchDocument.BodyText != null
                        && !email.Chunks.Any(chunk => chunk.AttachmentPosition == null))
                    && !(readsAttachments
                        && email.AttachmentCount > 0
                        && email.AttachmentTextDerivedAt == null))
                .Where(MailAwaitingRuleEvaluation.IsFinishedWith)
                .Where(MailAwaitingRelocation.IsSettledWhereItIs),
            embeddedFolders),
        terms);

    private sealed record EnrichableEmailRow(
        Guid Id,
        string? Subject,
        DateTimeOffset? ReceivedAt,
        IReadOnlyList<EnrichablePassageRow> Passages);

    private sealed record EnrichablePassageRow(Guid Id, int Ordinal, string Text);
}
