// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Accounts;

/// <summary>How current one folder's local copy is, in the three facts a screen drawing it beside its mail needs.</summary>
/// <param name="Alias">MailFathom's own name for the folder.</param>
/// <param name="State">Whether the deployment's last attempt at the folder succeeded, failed, found no server, or has never happened.</param>
/// <param name="SynchronizedAt">When the folder last durably committed progress, or <see langword="null" /> when it never has.</param>
/// <param name="IsBehind">Whether the folder's last attempt ended with mail it had not yet taken in.</param>
/// <remarks>
/// <para>
/// It is <see cref="MailboxFolderFreshness" /> — the durable half, which is one instant — reduced together with what
/// this process's synchronization run ledger observed. Neither source answers on its own: the instant says how old the
/// copy is and nothing about whether anything is still refreshing it, and the ledger says how the last attempt went and
/// nothing about how much of the folder had already been stored before it.
/// </para>
/// <para>
/// Being behind is deliberately not a state. A folder can be behind under any of them — a run that succeeded within its
/// batch budget leaves mail for the next one, and a failing folder is usually behind as well — so folding it in would
/// make one field answer two questions and lose whichever the reader was not asking.
/// </para>
/// </remarks>
public sealed record MailFolderFreshness(
    MailFolderAlias Alias,
    MailSynchronizationState State,
    DateTimeOffset? SynchronizedAt,
    bool IsBehind);
