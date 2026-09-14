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
    MailAccountId Account,
    LocalMailFolderId Folder,
    MailFolderChangeKind Kind,
    int ErasedFolderCount,
    DateTimeOffset OccurredAt);
