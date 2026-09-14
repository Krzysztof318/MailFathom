// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Folders;

/// <summary>Answers what deleting a folder through the client does on the mail server one account mirrors.</summary>
/// <remarks>
/// A port of its own rather than a method on either of the email deletion readers, for the reason those two are
/// separate from each other: the three answer for different acts and are read by different callers, and one port would
/// let a caller reach for the wrong answer with no signature to stop it. This one is read where the deletion is
/// authored, which is also where it is carried out, so nothing has to travel on a record.
/// </remarks>
public interface IAuthoredFolderDeleteDispositionReader
{
    /// <summary>Gets the disposition configured for one account's own folder deletions.</summary>
    /// <param name="accountId">The account whose folder is being deleted.</param>
    /// <returns>Whether the deletion reaches the mail server or marks the folder deleted in MailFathom alone.</returns>
    AuthoredFolderDeleteDisposition GetAuthoredFolderDeleteDisposition(MailAccountId accountId);
}
