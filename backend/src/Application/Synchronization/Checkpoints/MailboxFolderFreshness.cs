// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Synchronization.Checkpoints;

/// <summary>Reports how current the local copy of one folder is.</summary>
/// <param name="AccountId">The account the folder belongs to.</param>
/// <param name="FolderAlias">MailFathom's own name for the folder.</param>
/// <param name="SynchronizedAt">When synchronization last durably committed progress for the folder, or <see langword="null" /> when it never has.</param>
/// <remarks>
/// <para>
/// Every query result carries this, because a mailbox read is served from local state whether or not a mail server is
/// reachable. Without it a caller cannot tell a folder that holds no matching mail from one whose synchronization has
/// been failing for a week, and both look like an answer about the mailbox.
/// </para>
/// <para>
/// An alias can have been bound to several remote folders over time, and the timestamp is the most recent progress of
/// any of those bindings. That is what "how current is this alias" means to a reader: the older bindings' emails are
/// still listed under the same alias, and no reader is asking which generation produced them.
/// </para>
/// </remarks>
public sealed record MailboxFolderFreshness(
    MailAccountId AccountId,
    MailFolderAlias FolderAlias,
    DateTimeOffset? SynchronizedAt)
{
    /// <summary>Gets whether synchronization has ever committed progress for the folder.</summary>
    public bool WasSynchronized => this.SynchronizedAt is not null;
}
