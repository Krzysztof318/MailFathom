// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations.Authoring;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Emails;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Mutations;

/// <summary>Reads, from the local mailbox copy, where one email is in the identities a mutation names.</summary>
/// <remarks>
/// <para>
/// The projection names six columns and joins one folder row, which is everything a mutation is addressed by and
/// nothing a message carries. Reading the entity would pull a subject, the participants, and every derived column into
/// memory to answer a question none of them takes part in, so the query is written as a projection rather than as a
/// lookup that discards most of what it loaded.
/// </para>
/// <para>
/// A tombstoned row answers as an absent email, on the same terms every read of stored mail applies, so answering about
/// mail no listing serves cannot make the write surface a way to learn that a row exists.
/// </para>
/// <para>
/// An occurrence the server no longer holds answers as absent too, and that is the stricter of the two conditions
/// rather than the same one said twice. A local copy retained after an authored delete is not a tombstone — a listing
/// serves it, so a caller can see it and name it — while the UID it carries names a message the server expunged.
/// Recording a change against one would open durable records that convergence could only attempt and fail, so this read
/// refuses it where a read of the mail itself does not.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class AuthoredMailboxTargetReader(MailFathomDbContext readContext) : IAuthoredMailboxTargetReader
{
    /// <inheritdoc />
    public async Task<AuthoredMailboxTarget?> FindAsync(
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken)
    {
        var emailId = storedEmailId.Value;

        var located = await readContext.StoredEmails
            .AsNoTracking()
            .Where(email => email.Id == emailId)
            .Where(StoredEmailTombstone.IsNotTombstoned)
            .Where(email => email.RemoteExpungeObservedAt == null)
            .Select(email => new
            {
                email.OwnerId,
                email.MailboxAccountId,
                email.UidValidity,
                email.Uid,
                Alias = email.MailFolder.Alias,
                Generation = email.MailFolder.ResolutionGeneration,
                RemotePath = email.MailFolder.RemotePath,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (located is null)
        {
            return null;
        }

        var folder = new MailFolderResolution(
            MailFolderAlias.Create(located.Alias),
            MailFolderResolutionGeneration.Create(located.Generation),
            RemoteFolderPath.Create(located.RemotePath));

        return new AuthoredMailboxTarget(
            MailOwnerId.Create(located.OwnerId),
            EmailOccurrenceId.Create(
                MailAccountId.Create(located.MailboxAccountId),
                folder.Id,
                ImapUidValidity.Create(located.UidValidity),
                ImapUid.Create(located.Uid)),
            folder);
    }
}
