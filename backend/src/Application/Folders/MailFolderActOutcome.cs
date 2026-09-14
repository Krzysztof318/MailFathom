// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Folders;

namespace MailFathom.Application.Folders;

/// <summary>How one act on an account's folders ended.</summary>
/// <param name="Folder">The folder as the act left it, or as it stood before one that took it away; <see langword="null" /> where the act was refused.</param>
/// <param name="Change">What the act did, or <see langword="null" /> where it was refused.</param>
/// <param name="Refusal">Why the act was refused, or <see langword="null" /> where it committed.</param>
/// <param name="MailErasureDeferred">Whether an act that disposes of mail found the job queue full, so the mail waits for the account's next such act.</param>
public sealed record MailFolderActOutcome(
    ManagedMailFolder? Folder,
    MailFolderChangeKind? Change,
    MailFolderActRefusal? Refusal,
    bool MailErasureDeferred = false)
{
    /// <summary>States a refusal.</summary>
    /// <param name="refusal">Why the act was refused.</param>
    /// <returns>The outcome.</returns>
    public static MailFolderActOutcome Refused(MailFolderActRefusal refusal) => new(null, null, refusal);
}
