// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Rules.Evaluation;

/// <summary>Bounds how much of an account's mail one evaluation pass takes in hand.</summary>
/// <remarks>
/// <para>
/// Neither setting is a schedule. Evaluation is a step of the account's synchronization run, so how often a pass happens
/// is that run's interval and its backoff; what is configured here is how wide one pass is allowed to be, which is what
/// stops a mailbox that has never been evaluated from turning one run into a walk of its whole history while the folders
/// the run exists to fetch wait behind it.
/// </para>
/// <para>
/// The two bounds apply to each of the pass's two walks separately, because they answer the same question about
/// different work: an account that has just been given its first rule set has a long arrival queue and no requested run,
/// and an account whose owner asked for a re-run has the opposite. Sharing one budget between them would let either one
/// starve the other for as many runs as it took to drain.
/// </para>
/// </remarks>
public sealed class MailRuleEvaluationOptions
{
    /// <summary>Gets or sets how many stored emails one batch reads, evaluates, and commits together.</summary>
    /// <remarks>
    /// A batch is the unit of progress an interrupted pass loses, and the unit of work one transaction covers. Raising
    /// it drains a backlog in fewer transactions; lowering it shortens the window a cancelled pass has to give back.
    /// </remarks>
    public int BatchSize { get; set; } = 200;

    /// <summary>Gets or sets how many batches one walk of one pass may commit before it leaves the rest to the next run.</summary>
    public int MaxBatchesPerPass { get; set; } = 5;

    /// <summary>Gets or sets how long a recorded rule execution is kept before the account's next run erases it.</summary>
    /// <remarks>
    /// The one bound the history has of its own. Everything else about its lifetime it inherits from the mail it
    /// describes, because an execution names a message and goes when that message does; this is what stops a deployment
    /// nobody deletes mail from accumulating a row per rule per message for as long as it runs. Zero or less declares no
    /// window, which keeps every execution until the message it names is erased.
    /// </remarks>
    public TimeSpan HistoryRetention { get; set; } = TimeSpan.FromDays(30);
}
