// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Mutations.Convergence;

/// <summary>Bounds one convergence pass and says how long an unresolved outcome may stay unresolved.</summary>
/// <remarks>
/// Neither setting is a retry policy. How often a pass runs and how far apart two attempts of the same change are is the
/// account's synchronization schedule, and how many attempts one change may spend is
/// <see cref="MailboxMutationOptions.MaximumAttempts" />. What is configured here is the width of a pass and the one
/// deadline convergence owns.
/// </remarks>
public sealed class MailboxConvergenceOptions
{
    /// <summary>Gets or sets how many outstanding mutations one pass takes in hand.</summary>
    /// <remarks>
    /// <para>
    /// A pass is bounded so an account that has accumulated a backlog cannot turn one run into an unbounded sequence of
    /// mail-server round trips while the folders it is meant to synchronize wait behind it. What the bound leaves is
    /// picked up by the next pass, oldest first, so nothing is dropped by it.
    /// </para>
    /// <para>
    /// A mutation is a write to a mail server rather than a row to process, so the useful values are small. Raising it
    /// drains a backlog in fewer runs at the cost of a longer run.
    /// </para>
    /// </remarks>
    public int MaxMutationsPerPass { get; set; } = 50;

    /// <summary>Gets or sets how long a mutation whose placement was never acknowledged waits to be settled by observation.</summary>
    /// <remarks>
    /// <para>
    /// A placement command that went out without an answer is never issued again, so the only thing that can still
    /// finish it is the mailbox itself: a later synchronization run sees the source occurrence gone and the record
    /// accounts for the disappearance. That takes as long as it takes the account's folders to come round again, which
    /// is why this is a period rather than an attempt count.
    /// </para>
    /// <para>
    /// When it elapses the mutation is given up on and stays visible as dead-lettered. Waiting forever instead would be
    /// the exact failure this design exists to remove: a change that looks busy and is not. Raising it buys patience on
    /// an account with long synchronization intervals; lowering it surfaces an unresolvable copy sooner.
    /// </para>
    /// </remarks>
    public TimeSpan UnknownOutcomeGrace { get; set; } = TimeSpan.FromHours(6);
}
