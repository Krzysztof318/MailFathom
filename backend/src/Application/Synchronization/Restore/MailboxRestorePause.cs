// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Synchronization.Restore;

/// <summary>Names why a restore is standing still, which is always something an operator corrects in configuration.</summary>
/// <remarks>
/// A pause is not a failure. Nothing was attempted and nothing is retried on a backoff: the account stays in
/// <see cref="Domain.Accounts.MailAccountCustodyPhase.Restoring" />, its mail is served from stored state exactly as
/// before, and the next run after the correction carries on. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0034-holding-a-mailbox-mailfathom-alone-keeps.md">ADR 0034</see>.
/// </remarks>
public enum MailboxRestorePause
{
    /// <summary>Nothing is holding the restore up.</summary>
    None = 0,

    /// <summary>The account's configuration has come to synchronize a folder playing a virtual role.</summary>
    /// <remarks>
    /// Such a folder presents messages that are occurrences of other folders, so appending into it would put mail into
    /// folders nobody asked about. It pauses the drain for the same reason and by the same reading.
    /// </remarks>
    SynchronizedVirtualFolder = 1,

    /// <summary>A local folder holding mail corresponds to no folder mapping of the account.</summary>
    /// <remarks>
    /// There is no folder on the source its messages could go back into, and no source path may be derived from a local
    /// name. The operator writes a mapping for that folder, or the mail is moved into a folder that has one.
    /// </remarks>
    LocalFolderWithoutMapping = 2,
}
