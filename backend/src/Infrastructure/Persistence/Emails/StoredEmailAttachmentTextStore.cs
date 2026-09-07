// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Application.Folders;
using MailFathom.Application.Persistence;
using MailFathom.Application.Spam.Gating;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using MailFathom.Infrastructure.Persistence.Spam;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>EF Core state for the reading of attachments the account run performs behind the cut.</summary>
/// <remarks>
/// The selection carries the cut's four conditions and two of its own. The four are the arrival pipeline's orderings —
/// the message is still local, the rules have finished with it and are not still moving it, its folder is one an
/// operator asked to have embedded, and the classification gate admits it — which is how withholding reaches an
/// attachment without a second evaluation. The two are what makes the walk terminate and stay proportional: the message
/// has to carry an attachment at all, and nothing may have read them yet.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class StoredEmailAttachmentTextStore(
    MailFathomDbContext dbContext,
    EmailChunkWriter chunkWriter,
    IMailFolderParticipationReader folderParticipation,
    DerivedWorkGate derivedWorkGate,
    TimeProvider timeProvider)
    : IStoredEmailAttachmentTextStore
{
    /// <inheritdoc />
    /// <remarks>
    /// Ordering is by the primary key, which is total, stable, and already indexed, so the resume position is one
    /// column and no tie-breaker is needed.
    /// </remarks>
    public async Task<IReadOnlyList<EmailAwaitingAttachmentText>> GetEmailsAwaitingAttachmentTextAsync(
        MailAccountIdentity account,
        StoredEmailId? resumeAfter,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        var userId = account.User.Value;
        var mailboxAccountId = account.Id.Value;
        var after = resumeAfter?.Value;

        // One snapshot for both halves, exactly as the cut reads it: the predicate narrows the batch and the answer
        // below names which of the gate's decisions admitted each row, so a second reading taken microseconds later
        // could let the query select a row the answer then reported as still waiting.
        var terms = derivedWorkGate.ReadTerms();

        var candidates = await Selecting(
                dbContext.StoredEmails.AsNoTracking(),
                userId,
                mailboxAccountId,
                folderParticipation.FoldersGeneratingEmbeddings,
                terms)
            .Where(email => after == null || email.Id > after)
            .OrderBy(email => email.Id)
            .Take(batchSize)
            .Select(email => new OutstandingAttachmentRow(
                email.Id,
                email.UserId,
                new StoredDerivedWorkCandidateRow(
                    email.MailFolder.MailboxAccountId,
                    email.MailFolder.Alias,
                    email.StoredAt,
                    email.ContentAvailability,
                    email.SpamClassification == null ? null : email.SpamClassification.Verdict)))
            .ToArrayAsync(cancellationToken);

        return
        [
            .. candidates.Select(row => new EmailAwaitingAttachmentText(
                StoredEmailId.Create(row.Id),
                MailUserId.Create(row.UserId),
                row.Candidate.AdmittedUnder(terms))),
        ];
    }

    /// <inheritdoc />
    public async Task SaveAttachmentTextAsync(
        IPersistenceSession session,
        StoredEmailId emailId,
        EmailAttachmentTextDerivation derived,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(derived);

        var context = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        var storedEmail = await context.StoredEmails.FindAsync([emailId.Value], cancellationToken)
            ?? throw new InvalidOperationException(
                "Attachment text cannot be stored for a stored email that no longer exists.");

        var derivedAt = timeProvider.GetUtcNow();

        // Replaced whole rather than reconciled row by row, because a re-derivation is the only thing that reaches an
        // already-read message and it has just re-read every attachment on it. The passages below are reconciled per
        // attachment instead, which is where an unchanged reading has to write nothing.
        await context.EmailAttachmentTexts
            .Where(text => text.StoredEmailId == emailId.Value)
            .ExecuteDeleteAsync(cancellationToken);

        context.EmailAttachmentTexts.AddRange(derived.Attachments.Select(attachment =>
            new EmailAttachmentTextEntity
            {
                StoredEmailId = storedEmail.Id,
                StoredEmail = storedEmail,
                AttachmentPosition = attachment.Position,
                Kind = attachment.Kind,
                DeclaredMediaType = attachment.DeclaredMediaType,
                FileName = attachment.FileName,
                Outcome = attachment.Outcome,
                Text = attachment.Text,
                PageCount = attachment.PageCount,
                Segments = SerializeSegments(attachment),
                DerivedAt = derivedAt,
                SensitiveContentStamp = derived.RedactedUnder?.Value,
            }));

        await chunkWriter.SaveAttachmentChunksAsync(context, storedEmail, derived.Attachments, cancellationToken);

        // Written last and in the same statement as everything above. A message stamped without its readings would
        // never be offered to a parser again, and readings without the stamp would be taken a second time next run.
        //
        // Withheld where the reading is not the last one this message needs — a provider that may answer later, or a
        // stored copy a repair request was recorded for. The rows above are still written, so what the run did manage
        // to read is not lost and the re-read replaces them wholesale.
        // EmailAttachmentTextDerivation.IsSettled holds which refusals count and why the configuration ones do not.
        if (derived.IsSettled)
        {
            storedEmail.AttachmentTextDerivedAt = derivedAt;
        }
    }

    /// <summary>Narrows stored mail to the messages whose attachments nothing has read yet.</summary>
    /// <param name="emails">The emails to narrow.</param>
    /// <param name="userId">The user whose account this pass belongs to, which is what the index leads with.</param>
    /// <param name="mailboxAccountId">The configured account this pass belongs to.</param>
    /// <param name="embeddedFolders">The folders a mapping admits to embedding, which is what decides the reading.</param>
    /// <param name="terms">The classification terms the whole batch is decided under.</param>
    /// <returns>The narrowed query, which PostgreSQL evaluates in full.</returns>
    /// <remarks>
    /// Written as a composable predicate rather than inline, so what selects a message is one statement that can be
    /// asserted about directly. It is the cut's predicate with the search-document clause replaced: a body's words and
    /// a file's words are independent, so a message whose body yielded nothing may still carry a contract worth
    /// reading, and one whose attachments were already read is out of the walk whatever its body did.
    /// </remarks>
    internal static IQueryable<StoredEmailEntity> Selecting(
        IQueryable<StoredEmailEntity> emails,
        Guid userId,
        string mailboxAccountId,
        IReadOnlyList<MailFolderIdentity> embeddedFolders,
        DerivedWorkAdmissionTerms terms) => SelectingEverywhere(
        emails.Where(email => email.UserId == userId && email.MailboxAccountId == mailboxAccountId),
        embeddedFolders,
        terms);

    /// <summary>Narrows to the mail awaiting a reading of its attachments, in whatever scope the caller has already narrowed to.</summary>
    /// <param name="emails">The messages to select from, narrowed to one account or to none.</param>
    /// <param name="embeddedFolders">The folders an operator asked to have embedded.</param>
    /// <param name="terms">The classification gate's terms, read once so the predicate and the answer agree.</param>
    /// <returns>The mail this pass still owes a reading.</returns>
    /// <remarks>
    /// The account narrowing is the caller's rather than this method's, so a reading that reports across the whole
    /// deployment and the walk that reads one account at a time answer the same question. Two predicates would let the
    /// figure an operator watches disagree with the work that moves it: mail withheld by the gate, or held in a folder
    /// nobody asked to embed, is not outstanding and would otherwise be counted as outstanding for ever.
    /// </remarks>
    internal static IQueryable<StoredEmailEntity> SelectingEverywhere(
        IQueryable<StoredEmailEntity> emails,
        IReadOnlyList<MailFolderIdentity> embeddedFolders,
        DerivedWorkAdmissionTerms terms) =>
        ReachableEverywhere(emails, embeddedFolders, terms).Where(email => email.AttachmentTextDerivedAt == null);

    /// <summary>Narrows to the mail a reading could reach at all, whether or not one has already been taken.</summary>
    /// <param name="emails">The messages to select from, narrowed to one account or to none.</param>
    /// <param name="embeddedFolders">The folders an operator asked to have embedded.</param>
    /// <param name="terms">The classification gate's terms, read once so the predicate and the answer agree.</param>
    /// <returns>The mail carrying an attachment this pass is allowed to open.</returns>
    /// <remarks>
    /// The denominator of every coverage figure, and the selection above with the reading stamp left out. Keeping the
    /// two here rather than writing a second predicate elsewhere is what makes "read" and "outstanding" add up to the
    /// same population: a reading that counted a wider set would report a mailbox as permanently incomplete.
    /// </remarks>
    internal static IQueryable<StoredEmailEntity> ReachableEverywhere(
        IQueryable<StoredEmailEntity> emails,
        IReadOnlyList<MailFolderIdentity> embeddedFolders,
        DerivedWorkAdmissionTerms terms) => DerivedWorkAdmittedEmails.Admitting(
        AccountScopedMailFolders.Admitting(
            emails
                .Where(StoredEmailTombstone.IsNotTombstoned)
                .Where(email => email.AttachmentCount > 0)
                .Where(MailAwaitingRuleEvaluation.IsFinishedWith)
                .Where(MailAwaitingRelocation.IsSettledWhereItIs),
            embeddedFolders),
        terms);

    /// <inheritdoc />
    public async Task<int> DiscardAttachmentTextAsync(
        IPersistenceSession session,
        StoredEmailId emailId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var context = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        // One statement rather than tracked entities, for the reason the passage removal is: a row holds a whole
        // document's text, and loading it to throw it away is the one cost this removal exists to avoid paying twice.
        var discarded = await context.EmailAttachmentTexts
            .Where(text => text.StoredEmailId == emailId.Value)
            .ExecuteDeleteAsync(cancellationToken);

        // The stamp is the whole of what Selecting reads, so leaving it standing over rows that are gone would take the
        // message out of the walk for good — including after the gate re-admits it, which a reversed junk verdict does
        // with nothing having recorded the move. Junk mail stays unread on the gate rather than on the stamp.
        await context.StoredEmails
            .Where(email => email.Id == emailId.Value)
            .ExecuteUpdateAsync(
                email => email.SetProperty(row => row.AttachmentTextDerivedAt, (DateTimeOffset?)null),
                cancellationToken);

        return discarded;
    }

    /// <summary>Writes one attachment's page boundaries as the document the row stores, or nothing where it has none.</summary>
    /// <remarks>
    /// Absent rather than an empty array for an attachment that yielded no words, so a row carrying no text carries no
    /// coordinates either — the two absences are one fact and storing <c>[]</c> beside a null text would invite a
    /// reader to treat them as different.
    /// </remarks>
    private static string? SerializeSegments(DerivedAttachmentText attachment) => attachment.Segments.Count == 0
        ? null
        : JsonSerializer.Serialize(
            attachment.Segments,
            AttachmentTextSegmentJsonContext.Default.IReadOnlyListAttachmentTextSegment);

    /// <summary>One message awaiting a reading of its attachments, as the walk's projection returns it.</summary>
    private sealed record OutstandingAttachmentRow(
        Guid Id,
        Guid UserId,
        StoredDerivedWorkCandidateRow Candidate);
}
