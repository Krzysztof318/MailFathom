// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Mail.Delivery.Filing;

/// <summary>A message filed into a held account's local folder by a transaction that has yet to commit.</summary>
/// <param name="Account">The account it was filed for.</param>
/// <param name="Folder">The alias of the folder binding it was stored under, which is what a client is told about.</param>
/// <param name="Email">The stored message.</param>
/// <param name="CreatedFolders">Whether filing it supplied protected folders the account was missing.</param>
public sealed record FiledLocalEmail(
    MailAccountIdentity Account,
    MailFolderAlias Folder,
    StoredEmailId Email,
    bool CreatedFolders)
{
    /// <summary>Gets the stored message this one replaced and the same transaction erased, or <see langword="null" /> where it replaced none.</summary>
    public StoredEmailId? Replaced { get; init; }
}
