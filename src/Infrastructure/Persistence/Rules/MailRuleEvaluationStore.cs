// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Rules.Evaluation;
using MailFathom.Application.Rules.Facts;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Rules;

/// <summary>EF Core state for the rule passes that run inside an account's synchronization run.</summary>
/// <remarks>
/// Both walks project straight into the fact surface rather than loading rows, because a condition reads twenty of its
/// twenty-one facts from metadata and none of them is the raw MIME sitting beside it in the same aggregate. The
/// twenty-first is read one email at a time and only when a condition names it, which is what keeps a rule set that
/// mentions no body text free of a content read.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailRuleEvaluationStore(MailFathomDbContext dbContext) : IMailRuleEvaluationStore
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
    /// Text is still expected wherever content is stored or is going to be, which is why
    /// <see cref="StoredEmailContentAvailability.AwaitingStorageHeadroom" /> counts alongside
    /// <see cref="StoredEmailContentAvailability.Available" />: a later run fetches that payload as soon as the ceiling
    /// permits, and evaluating the email now would stamp it as evaluated and leave a rule naming the body text never
    /// seeing it. <see cref="StoredEmailContentAvailability.ExceededSizeLimit" /> is the opposite future — every later
    /// run refuses it for the same reason — so such an email is evaluated now with the fact absent rather than waited on.
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

        var candidates = await emails
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
                HasExtractedContent = email.SearchDocument != null,
                AwaitsExtraction = (email.ContentAvailability == StoredEmailContentAvailability.Available
                        || email.ContentAvailability == StoredEmailContentAvailability.AwaitingStorageHeadroom)
                    && email.SearchDocument == null,
            })
            .ToArrayAsync(cancellationToken);

        return
        [
            .. candidates.Select(candidate => new StoredEmailAwaitingRuleEvaluation(
                StoredEmailId.Create(candidate.Id),
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
