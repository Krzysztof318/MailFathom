// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Folders;

/// <summary>One of the four things a person may ask of a folder, whichever copy of the mailbox is the truth.</summary>
/// <remarks>
/// It is what the client's folder surface reports as allowed and what a request names. The set is the same on every
/// account by design: which of them an account and a folder actually permit is answered per folder rather than by
/// publishing how the account's mail is stored.
/// </remarks>
public enum MailFolderAct
{
    /// <summary>Make a folder, either beneath a named parent or at the top of the hierarchy.</summary>
    Create = 0,

    /// <summary>Give a folder another name, leaving it where it is.</summary>
    Rename = 1,

    /// <summary>Put a folder, with everything beneath it, somewhere else in the hierarchy.</summary>
    Move = 2,

    /// <summary>Delete a folder.</summary>
    Delete = 3,
}
