// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Synchronization;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>EF Core reads over how much room local content storage has left and which occurrences are waiting for it.</summary>
[RequiresIntegrationCoverage]
internal sealed class StoredEmailContentInventory(MailFathomDbContext dbContext) : IStoredEmailContentInventory
{
    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The answer is PostgreSQL's own accounting of what the content table occupies — its heap, its indexes, and the
    /// TOAST relation the payloads actually live in — rather than a sum over the recorded byte lengths. That is the
    /// quantity the operator's disk reports, it is what a ceiling is set against, and it is read from the catalog in
    /// constant time, which is what lets a run ask before every folder instead of amortizing a table scan.
    /// </para>
    /// <para>
    /// Two consequences follow and are deliberate. Space a delete freed is reported as occupied until PostgreSQL
    /// reclaims it, because until then the file is still that large; and the number is always somewhat above the sum of
    /// the payloads, because storage overhead is part of what fills a disk. A database that has never been migrated has
    /// no such table, which reports zero rather than failing the run that asked.
    /// </para>
    /// </remarks>
    public async Task<long> GetStoredContentBytesAsync(CancellationToken cancellationToken)
    {
        var occupiedBytes = await dbContext.Database
            .SqlQuery<long>($"SELECT COALESCE(pg_total_relation_size(to_regclass('email_message_contents')), 0) AS \"Value\"")
            .SingleAsync(cancellationToken);

        return occupiedBytes;
    }

    /// <inheritdoc />
    /// <remarks>
    /// A tombstoned occurrence is left out, because fetching content for mail no query may serve would spend a mailbox
    /// round trip and storage on a message that has left the folder. The projection rebuilds exactly what the row was
    /// recorded from, so the run that fetches the payload commits it under the same metadata as the discovery that
    /// deferred it, and it reads the join to the outgoing send beside it rather than the send itself, because what the
    /// caller decides from it is whether the message is offered for a spam verdict.
    /// </remarks>
    public async Task<IReadOnlyList<EmailAwaitingContent>> GetEmailsAwaitingContentAsync(
        MailAccountIdentity account,
        MailFolderResolutionId folderResolutionId,
        ImapUidValidity uidValidity,
        int maxEmailCount,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEmailCount);

        var owner = account.Owner.Value;
        var mailboxAccountId = account.Id.Value;
        var alias = folderResolutionId.Alias.Value;
        var generation = folderResolutionId.Generation.Value;
        var uidValidityValue = uidValidity.Value;

        var candidates = await dbContext.StoredEmails
            .AsNoTracking()
            .Where(StoredEmailTombstone.IsNotTombstoned)
            .Where(email => email.ContentAvailability == StoredEmailContentAvailability.AwaitingStorageHeadroom
                && email.OwnerId == owner
                && email.MailboxAccountId == mailboxAccountId
                && email.MailFolder.Alias == alias
                && email.MailFolder.ResolutionGeneration == generation
                && email.UidValidity == uidValidityValue)
            .OrderBy(email => email.Uid)
            .Take(maxEmailCount)
            .Select(email => new
            {
                email.Uid,
                email.InternetMessageId,
                email.Subject,
                email.SentAt,
                email.SizeOctets,
                email.FiledFromOutgoingEmailId,
            })
            .ToArrayAsync(cancellationToken);

        return
        [
            .. candidates.Select(candidate => new EmailAwaitingContent(
                new RemoteEmailMetadata(
                    EmailOccurrenceId.Create(account.Id, folderResolutionId, uidValidity, ImapUid.Create(candidate.Uid)),
                    candidate.InternetMessageId,
                    candidate.Subject,
                    candidate.SentAt,
                    candidate.SizeOctets),
                candidate.FiledFromOutgoingEmailId is not null)),
        ];
    }
}
