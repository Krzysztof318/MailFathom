// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Folders;
using MailFathom.Application.Persistence;
using MailFathom.Application.Rules.Evaluation;
using MailFathom.Application.Rules.Facts;
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

namespace MailFathom.Infrastructure.Persistence.Rules;

/// <summary>EF Core state for the rule passes that run inside an account's synchronization run.</summary>
/// <remarks>
/// <para>
/// Both walks project straight into the fact surface rather than loading rows, because a condition reads twenty of its
/// twenty-two facts from metadata and none of them is the raw MIME sitting beside it in the same aggregate. Of the
/// other two, <c>folderRole</c> is read from configuration rather than from any row, and <c>bodyText</c> is read one
/// email at a time and only when a condition names it, which is what keeps a rule set that mentions no body text free
/// of a content read.
/// </para>
/// <para>
/// Both walks also admit the folders a mapping mirrors and no others, and it is the walk rather than the pass that
/// narrows. A folder whose synchronization was switched off keeps the mail it had stored, and a folder whose mapping
/// was removed keeps it too, so the rows are there to be read; dropping them after the batch came back would leave
/// every one of them at the head of the arrival queue forever, since what takes an email out of that queue is a pass
/// having evaluated it.
/// </para>
/// <para>
/// Both walks also leave out the mail classification is withholding, which is the ordering between the two mechanisms
/// rather than a filter of the rule engine's own. An authored rule filing a sender's mail into a folder and a
/// classification filing the same message into junk are two fates for one occurrence; classification decides first, so
/// they can never disagree about one message. A message merely waiting on a verdict is left in the queue exactly as an
/// email waiting for extraction is — it is evaluated once the verdict arrives, or once the wait a verdict is allowed
/// runs out.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailRuleEvaluationStore(
    MailFathomDbContext dbContext,
    IMailFolderParticipationReader folderParticipation,
    DerivedWorkGate derivedWorkGate) : IMailRuleEvaluationStore
{
    /// <inheritdoc />
    /// <remarks>
    /// The absent evaluation timestamp is the predicate and the partial index behind it, so the queue costs a read
    /// proportional to what is in it rather than to the account's mail. A tombstoned email is left out for the reason
    /// both backfills leave one out: applying a rule to mail nothing may read is work with no reader.
    /// </remarks>
    public Task<IReadOnlyList<StoredEmailAwaitingRuleEvaluation>> GetEmailsAwaitingFirstEvaluationAsync(
        MailAccountId accountId,
        StoredEmailId? resumeAfter,
        int batchSize,
        CancellationToken cancellationToken) =>
        this.ReadCandidatesAsync(
            dbContext.StoredEmails.Where(email => email.RulesEvaluatedAt == null),
            accountId,
            resumeAfter,
            batchSize,
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredEmailAwaitingRuleEvaluation>> GetStoredEmailsAsync(
        MailAccountId accountId,
        StoredEmailId? resumeAfter,
        int batchSize,
        CancellationToken cancellationToken) =>
        this.ReadCandidatesAsync(dbContext.StoredEmails, accountId, resumeAfter, batchSize, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// The trimmed reading is what a condition sees, which is the same text lexical search matches on. A rule asking
    /// whether a message says something is asking about what its author wrote, not about the quoted history and the
    /// signature underneath it.
    /// </remarks>
    public async Task<string?> ReadExtractedBodyTextAsync(
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) =>
        await dbContext.EmailSearchDocuments
            .AsNoTracking()
            .Where(document => document.StoredEmailId == storedEmailId.Value)
            .Select(document => document.BodyText)
            .SingleOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Written as one statement over the batch rather than through tracked entities, and the reason is a race rather
    /// than a saving. Synchronization and reconciliation write these same rows under an optimistic token, so loading
    /// two hundred of them to set one column would turn every overlap into a conflict the pass has to retry — over a
    /// column no other writer touches.
    /// </remarks>
    public async Task RecordEvaluatedAsync(
        IPersistenceSession session,
        IReadOnlyList<StoredEmailId> storedEmailIds,
        DateTimeOffset evaluatedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(storedEmailIds);

        if (storedEmailIds.Count == 0)
        {
            return;
        }

        var identities = storedEmailIds.Select(storedEmailId => storedEmailId.Value).ToArray();
        var sessionContext = EfCorePersistenceSessionAccessor.DbContextOf(session);

        await sessionContext.StoredEmails
            .Where(email => identities.Contains(email.Id))
            .ExecuteUpdateAsync(
                update => update.SetProperty(email => email.RulesEvaluatedAt, evaluatedAt),
                cancellationToken);
    }

    /// <summary>Reads one keyset batch of an account's mail as the fact surface a condition is evaluated against.</summary>
    /// <remarks>
    /// <para>
    /// Ordering is by the primary key, which is total, stable, and already indexed for this account. Both the ordering
    /// and the keyset comparison are evaluated by PostgreSQL, so a walk runs entirely under that server's <c>uuid</c>
    /// ordering and never has to agree with how the CLR compares two identifiers.
    /// </para>
    /// <para>
    /// Text is still expected wherever content is stored or is going to be:
    /// <see cref="StoredEmailContentAvailability.AwaitingStorageHeadroom" /> because a later run fetches that payload as
    /// soon as the ceiling permits, and <see cref="StoredEmailContentAvailability.Available" /> with no document at all
    /// because that is mail stored before extraction reached it and the backfill still owes it a reading. Evaluating
    /// either now would stamp it as evaluated and leave a rule naming the body text never seeing it.
    /// <see cref="StoredEmailContentAvailability.ExceededSizeLimit" /> is the opposite future — every later run refuses
    /// it for the same reason — so such an email is evaluated now with the fact absent rather than waited on.
    /// </para>
    /// <para>
    /// Neither question is answered by whether a search document exists, because every stored occurrence has one: a
    /// message whose MIME nothing read is given a document built from its envelope alone, so that it is findable rather
    /// than invisible. Such a document records <see cref="ExtractedEmailTextSource.BodyNotExtracted" />, which is what
    /// separates the two cases the existence of a row cannot — a message that carries derived body text, and one whose
    /// stored MIME a reader already refused and no later pass will read differently.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<StoredEmailAwaitingRuleEvaluation>> ReadCandidatesAsync(
        IQueryable<StoredEmailEntity> emails,
        MailAccountId accountId,
        StoredEmailId? resumeAfter,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        var mailboxAccountId = accountId.Value;
        var resumeAfterId = resumeAfter?.Value;

        // Scoped to the folders a mapping mirrors, which withdraws two kinds of row at once from a pass that walks
        // stored mail rather than a request's scope: mail of a folder whose synchronization was switched off, which is
        // retained and refreshed by nothing, and mail of a folder configuration no longer names at all. Only an
        // admission reaches the second — no list of withheld names carries a folder nobody named — and a rule acting on
        // either would move or flag mail nothing here is still reading.
        var candidates = await DerivedWorkAdmittedEmails
            .Admitting(
                AccountScopedMailFolders.Admitting(emails, folderParticipation.FoldersSynchronized),
                derivedWorkGate.ReadTerms())
            .AsNoTracking()
            .Where(StoredEmailTombstone.IsNotTombstoned)
            .Where(email => email.MailboxAccountId == mailboxAccountId
                && (resumeAfterId == null || email.Id > resumeAfterId))
            .OrderBy(email => email.Id)
            .Take(batchSize)
            .Select(email => new
            {
                email.Id,
                email.MailFolder.Alias,
                email.MailFolder.ResolutionGeneration,
                email.UidValidity,
                email.Uid,
                email.Subject,
                email.SenderNormalizedAddress,
                email.ToAddresses,
                email.CcAddresses,
                email.ReceivedAt,
                email.SentAt,
                email.SizeOctets,
                email.AttachmentCount,
                email.AttachmentTotalSizeOctets,
                email.IsEncrypted,
                email.CarriesUnverifiedSignature,
                email.IsRemotelySeen,
                email.IsRemotelyAnswered,
                email.IsRemotelyFlagged,
                email.IsRemotelyDraft,
                HasExtractedContent = email.SearchDocument != null
                    && email.SearchDocument.TextSource != ExtractedEmailTextSource.BodyNotExtracted,
                AwaitsExtraction =
                    email.ContentAvailability == StoredEmailContentAvailability.AwaitingStorageHeadroom
                    || (email.ContentAvailability == StoredEmailContentAvailability.Available
                        && email.SearchDocument == null),
            })
            .ToArrayAsync(cancellationToken);

        return
        [
            .. candidates.Select(candidate => new StoredEmailAwaitingRuleEvaluation(
                StoredEmailId.Create(candidate.Id),
                EmailOccurrenceId.Create(
                    accountId,
                    new MailFolderResolutionId(
                        MailFolderAlias.Create(candidate.Alias),
                        MailFolderResolutionGeneration.Create(candidate.ResolutionGeneration)),
                    ImapUidValidity.Create(candidate.UidValidity),
                    ImapUid.Create(candidate.Uid)),
                new MailRuleEmailFacts
                {
                    Account = mailboxAccountId,
                    Folder = candidate.Alias,
                    Subject = candidate.Subject,
                    SenderAddress = candidate.SenderNormalizedAddress,
                    RecipientAddresses = [.. candidate.ToAddresses, .. candidate.CcAddresses],
                    ReceivedAt = candidate.ReceivedAt,
                    SentAt = candidate.SentAt,
                    SizeInBytes = candidate.SizeOctets,
                    AttachmentCount = candidate.AttachmentCount,
                    AttachmentTotalBytes = candidate.AttachmentTotalSizeOctets,
                    IsEncrypted = candidate.IsEncrypted,
                    CarriesUnverifiedSignature = candidate.CarriesUnverifiedSignature,
                    IsSeen = candidate.IsRemotelySeen,
                    IsAnswered = candidate.IsRemotelyAnswered,
                    IsFlagged = candidate.IsRemotelyFlagged,
                    IsDraft = candidate.IsRemotelyDraft,
                    HasExtractedContent = candidate.HasExtractedContent,
                },
                candidate.AwaitsExtraction)),
        ];
    }
}
