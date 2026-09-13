// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Folders.Local;

/// <summary>Records who changed the shape of a held mailbox, and how.</summary>
/// <remarks>
/// The record carries identities and never a folder name: a name is text a person typed, and what an audit reader needs
/// is which folder of whose account changed and in what way, which the identities answer without it.
/// </remarks>
public interface ILocalMailFolderChangeAuditor
{
    /// <summary>Records one committed change.</summary>
    /// <param name="change">What changed.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    Task RecordAsync(LocalMailFolderChange change, CancellationToken cancellationToken);
}

/// <summary>One committed change to a held account's folder hierarchy.</summary>
/// <param name="Account">The account, named by its user and its identifier.</param>
/// <param name="Folder">The folder the act named.</param>
/// <param name="Kind">What the act did.</param>
/// <param name="ErasedFolderCount">How many folders the act erased, which is the folder and everything beneath it, or zero.</param>
/// <param name="OccurredAt">When the change committed.</param>
public sealed record LocalMailFolderChange(
    MailAccountIdentity Account,
    LocalMailFolderId Folder,
    LocalMailFolderChangeKind Kind,
    int ErasedFolderCount,
    DateTimeOffset OccurredAt);

/// <summary>What an act on a held account's folder did.</summary>
public enum LocalMailFolderChangeKind
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
}
