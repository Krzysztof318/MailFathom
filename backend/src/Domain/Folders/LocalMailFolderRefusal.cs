// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Folders;

/// <summary>Why an act on a local folder hierarchy was refused.</summary>
public enum LocalMailFolderRefusal
{
    /// <summary>The caller holds no mail account by that identifier.</summary>
    AccountMissing = 0,

    /// <summary>The hierarchy may be edited only while MailFathom holds the account's mailbox, and this account is mirrored or restoring.</summary>
    AccountNotHeld = 1,

    /// <summary>The account has no live folder by that identifier.</summary>
    FolderMissing = 2,

    /// <summary>The account has no live folder by the parent identifier named.</summary>
    ParentMissing = 3,

    /// <summary>The folder plays a protected role and cannot be renamed, moved, or deleted.</summary>
    ProtectedRole = 4,

    /// <summary>The name is empty, too long, or carries a control character, a format character, or the hierarchy delimiter.</summary>
    NameInvalid = 5,

    /// <summary>The name is the inbox's, which no other folder may carry at the top of the hierarchy.</summary>
    InboxNameAtTopLevel = 6,

    /// <summary>A sibling already carries the name, compared without regard to case.</summary>
    NameTaken = 7,

    /// <summary>The folder would be moved beneath itself.</summary>
    NestedInItself = 8,

    /// <summary>The act would place a folder deeper than the hierarchy allows.</summary>
    TooDeep = 9,

    /// <summary>The account already holds as many folders as a hierarchy allows.</summary>
    TooManyFolders = 10,
}
