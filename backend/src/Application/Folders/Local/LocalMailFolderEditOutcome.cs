// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Folders;

namespace MailFathom.Application.Folders.Local;

/// <summary>How an act on a held account's folder ended.</summary>
/// <param name="Folder">The folder as it stands after the act, or as it stood before one that erased it; <see langword="null" /> where the act was refused.</param>
/// <param name="Kind">What the act did, or <see langword="null" /> where it was refused.</param>
/// <param name="Refusal">Why the act was refused, or <see langword="null" /> where it committed.</param>
/// <param name="MailErasureDeferred">
/// Whether an erasure committed while the job queue already held as many erasure passes as it accepts, so no pass was
/// queued for it: the folders are gone from every listing and their mail stays stored, out of sight, until the account's
/// next erasure queues a pass, which erases the mail of every erased folder rather than only its own.
/// </param>
public sealed record LocalMailFolderEditOutcome(
    LocalMailFolder? Folder,
    LocalMailFolderChangeKind? Kind,
    LocalMailFolderRefusal? Refusal,
    bool MailErasureDeferred = false)
{
    /// <summary>States a refusal.</summary>
    /// <param name="refusal">Why the act was refused.</param>
    /// <returns>The outcome.</returns>
    public static LocalMailFolderEditOutcome Refused(LocalMailFolderRefusal refusal) => new(null, null, refusal);
}
