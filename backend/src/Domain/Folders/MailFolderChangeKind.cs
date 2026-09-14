// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Folders;

/// <summary>What an act on an account's folder did, which is not always what the act asked for.</summary>
/// <remarks>
/// The act a person asks for is one of the four <see cref="MailFolderAct" /> names; this is what it turned out to
/// mean. Deleting is where the two come apart most: on an account whose mailbox MailFathom holds it moves the folder
/// into the trash, or erases it where it was already there, and on a mirrored account it deletes the folder on the mail
/// server or marks it deleted here, according to what the account's deletion setting says.
/// </remarks>
public enum MailFolderChangeKind
{
    /// <summary>The folder was created.</summary>
    Created = 0,

    /// <summary>The folder was given another name.</summary>
    Renamed = 1,

    /// <summary>The folder, with everything beneath it, was moved to another place in the hierarchy.</summary>
    Moved = 2,

    /// <summary>The folder was deleted, which moved it with everything beneath it into the trash.</summary>
    MovedToTrash = 3,

    /// <summary>The folder was deleted from the trash, which erases it, everything beneath it, and all of their mail.</summary>
    Erased = 4,

    /// <summary>The folder was deleted on the mail server, and the mail MailFathom stored from it was removed.</summary>
    Deleted = 5,

    /// <summary>The folder was marked deleted in MailFathom, and the mail server still holds it and its mail.</summary>
    /// <remarks>
    /// The folder leaves the tree, leaves what the account synchronizes, and leaves every mailbox query. What is
    /// stored from it stays stored and stops being reachable, exactly as a tombstoned message does.
    /// </remarks>
    MarkedDeleted = 6,
}
