// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Synchronization.Checkpoints;

namespace MailFathom.Application.Accounts;

/// <summary>Describes one served account together with how current the local copy of each of its folders is.</summary>
/// <param name="Account">What configuration declares about the account.</param>
/// <param name="Folders">One entry per folder local state knows of, ordered by alias, empty when no folder has been discovered yet.</param>
/// <remarks>
/// The folders are what synchronization has reached rather than what an operator configured. An account whose folders
/// are all absent here has never been synchronized, which is a different statement from an account whose folders are
/// present and stale, and a reader deciding whether an empty mailbox answer means anything needs to tell the two apart.
/// </remarks>
public sealed record DescribedMailAccount(
    ServedMailAccount Account,
    IReadOnlyList<MailboxFolderFreshness> Folders);
