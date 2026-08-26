// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.Evaluation;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Rules.History;

/// <summary>Ages the rule history out at the window the rule configuration declares.</summary>
/// <remarks>
/// <para>
/// The history is the one part of the rule machinery that grows without an end of its own. A pass records what every
/// rule it reached concluded about every message it evaluated, which is what makes "this rule has never matched" and
/// "this rule was never asked" different answers — and it is also why a deployment left alone would accumulate a row per
/// rule per message for as long as it runs. The window is the storage-limitation half of keeping that record at all.
/// </para>
/// <para>
/// It is the record's own bound and not the whole of its lifetime. An execution names a message, and a message erased
/// anywhere in this system takes the executions naming it with it through the email's own deletion path — so the history
/// inherits the deletion obligations of the mail it describes whatever this window says.
/// </para>
/// <para>
/// It runs on the account's own synchronization run rather than on a worker of its own, for the reason the two audit
/// retentions do: an account already has a loop that comes round, and a second schedule would be a second thing to
/// configure, watch, and reason about for work that is one bounded delete.
/// </para>
/// </remarks>
public sealed class MailRuleHistoryRetention
{
    /// <summary>The greatest number of executions one pass erases.</summary>
    /// <remarks>
    /// Larger than either audit trail's bound, because a rule pass writes an execution per rule per message where a run
    /// leaves one entry: the backlog this drains is a multiple of the mail rather than a count of it. It is a constant
    /// rather than a setting because nothing an operator would tune depends on it — a pass that reaches the bound is
    /// followed by another on the account's next run, so the number decides how long a backlog takes to clear rather
    /// than what is kept.
    /// </remarks>
    public const int MaximumExecutionsErasedPerPass = 5_000;

    private readonly IMailRuleExecutionStore store;
    private readonly MailRuleEvaluationOptions options;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the retention pass from the declared window and the history it erases from.</summary>
    /// <param name="store">Holds the history the pass erases from.</param>
    /// <param name="options">Declares the window an execution is kept for.</param>
    /// <param name="timeProvider">Measures the window back from now.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public MailRuleHistoryRetention(
        IMailRuleExecutionStore store,
        MailRuleEvaluationOptions options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.store = store;
        this.options = options;
        this.timeProvider = timeProvider;
    }

    /// <summary>Erases everything in one account's history that has outlived the configured window.</summary>
    /// <param name="account">The account whose history is aged.</param>
    /// <param name="cancellationToken">Cancels the erasure.</param>
    /// <returns>How many executions were erased.</returns>
    /// <remarks>
    /// A window of zero or less names no boundary at all and erases nothing, which is how a deployment declares that it
    /// keeps its history until the mail it describes is erased and no longer.
    /// </remarks>
    public Task<int> EraseExpiredAsync(MailAccountIdentity account, CancellationToken cancellationToken)
    {
        if (this.options.HistoryRetention <= TimeSpan.Zero)
        {
            return Task.FromResult(0);
        }

        return this.store.EraseEvaluatedBeforeAsync(
            account,
            this.timeProvider.GetUtcNow() - this.options.HistoryRetention,
            MaximumExecutionsErasedPerPass,
            cancellationToken);
    }
}
