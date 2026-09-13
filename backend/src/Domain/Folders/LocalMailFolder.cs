// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Folders;

/// <summary>One folder of the hierarchy MailFathom keeps for an account whose mailbox it holds.</summary>
/// <param name="Id">The folder's identity, which survives every rename and move.</param>
/// <param name="ParentId">The folder it sits beneath, or <see langword="null" /> at the top of the hierarchy.</param>
/// <param name="Name">The folder's own level of the hierarchy.</param>
/// <param name="Role">The protected role the folder plays, or <see langword="null" /> for an ordinary folder.</param>
/// <param name="SourceFolderAlias">The synchronized source folder whose arrivals this folder receives, or <see langword="null" /> where none does.</param>
public sealed record LocalMailFolder(
    LocalMailFolderId Id,
    LocalMailFolderId? ParentId,
    LocalMailFolderName Name,
    MailFolderSpecialUse? Role,
    MailFolderAlias? SourceFolderAlias)
{
    /// <summary>Gets whether the folder plays a role no act may take away, and therefore cannot be renamed, moved, or deleted.</summary>
    public bool IsProtected => this.Role is not null;
}
