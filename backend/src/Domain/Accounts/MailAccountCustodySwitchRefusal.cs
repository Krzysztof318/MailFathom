// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Accounts;

/// <summary>Names why a custody switch was not accepted, so a refusal says what to correct rather than that it failed.</summary>
/// <remarks>
/// Every value here is something the command can know when it is issued, which is what makes the refusal a refusal
/// rather than a drain that pauses later. What cannot be known until a connection is open — whether the source
/// advertises <c>UIDPLUS</c> — is not one of them and holds the account in its current phase instead. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0034-holding-a-mailbox-mailfathom-alone-keeps.md">ADR 0034</see>.
/// </remarks>
public enum MailAccountCustodySwitchRefusal
{
    /// <summary>The account synchronizes a folder playing a virtual role, whose messages are occurrences of other folders.</summary>
    /// <remarks>
    /// Holding such a folder would store every message it presents a second time, and draining it would remove mail
    /// from folders nobody asked about. The refusal names the alias so an operator stops synchronizing that mapping.
    /// </remarks>
    SynchronizedVirtualFolder = 0,

    /// <summary>A replica holding a live work lease runs a build that does not know the mode.</summary>
    /// <remarks>
    /// Such a build reads a held account as mirrored, and on meeting its source emptied it applies the account's
    /// disposition for mail somebody else deleted to every drained message. It clears on its own once the older
    /// replicas' leases expire, so the refusal is worth repeating rather than working around.
    /// </remarks>
    ReplicaOnBuildWithoutTheMode = 1,

    /// <summary>A folder mapping of the account names a remote path the restore could not append into.</summary>
    /// <remarks>
    /// Asked before a switch off is accepted, because the restore appends every held message into the source folder its
    /// mapping names: a path that is not valid under the account's folder rules, or that names no folder the source
    /// advertises and whose mapping does not permit creating one there, would leave the restore unable to finish.
    /// </remarks>
    UnusableFolderMapping = 2,

    /// <summary>The account's own deletions only flag a message <c>\Deleted</c> and leave it on the source.</summary>
    /// <remarks>
    /// A held account empties its source once each message is durably stored, so a delete that means to leave the
    /// message on the server contradicts the mode itself. The operator states <c>Expunge</c> for the account first.
    /// </remarks>
    FlagOnlyAuthoredDelete = 3,
}
