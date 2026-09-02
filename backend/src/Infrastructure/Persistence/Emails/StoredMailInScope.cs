// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Maintenance;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>Narrows a stored-mail query to one maintenance scope, once for every query that has to.</summary>
/// <remarks>
/// Written once because the count an operator agrees to and the walk that follows it must select the same rows. Two
/// predicates that happened to agree today would drift the first time either learned something about tombstones or
/// about a repointed alias, and an operator would meet that as a figure the work never matched.
/// </remarks>
internal static class StoredMailInScope
{
    /// <summary>Admits the rows of one scope that every other reader of stored mail admits.</summary>
    /// <param name="emails">The query to narrow.</param>
    /// <param name="scope">The account, and the one folder of it, to narrow to.</param>
    /// <returns>The narrowed query, still composed as <see cref="IQueryable{T}" /> so PostgreSQL does the filtering.</returns>
    /// <remarks>
    /// The alias comparison is written against a local rather than through the scope's value object, because a value
    /// object's member inside a translated lambda either fails to translate or forces the rest of the pipeline into
    /// this process.
    /// </remarks>
    internal static IQueryable<StoredEmailEntity> Within(IQueryable<StoredEmailEntity> emails, StoredMailScope scope)
    {
        var owner = scope.Account.Owner.Value;
        var account = scope.Account.Id.Value;
        var alias = scope.Folder?.Value;

        // The owner leads the account, which is the order the index leads in: an identifier names one account within
        // its owner, so narrowing on it alone would admit another owner's account carrying the same name.
        return emails
            .Where(StoredEmailTombstone.IsNotTombstoned)
            .Where(email => email.OwnerId == owner
                && email.MailboxAccountId == account
                && (alias == null || email.MailFolder.Alias == alias));
    }
}
