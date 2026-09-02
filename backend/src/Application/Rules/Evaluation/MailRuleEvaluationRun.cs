// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.History;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Rules.Evaluation;

/// <summary>A whole-mailbox rule run, and how far the account's synchronization runs have carried it.</summary>
/// <remarks>
/// <para>
/// Durable because it has to survive the process. The run spans as many account runs as its batch budget needs, so a
/// restart in the middle of one must resume at the email nobody has reached rather than at the beginning of a mailbox —
/// and a request that arrived seconds before a shutdown must still be a request afterwards.
/// </para>
/// <para>
/// One outstanding run per account, which is what makes a second request an answer rather than a second walk. There is
/// no queue behind it: asking twice for the same thing is asking once, and the reply says the run is already under way.
/// That holds across what started them as well — a rule's schedule finding a run in front of the account is answered
/// with it, because the mailbox is going to be walked either way.
/// </para>
/// <para>
/// Every field is either MailFathom's own identity for something or a count. Nothing derived from a message belongs in
/// a record an operator reads to find out what their instance is doing.
/// </para>
/// </remarks>
public sealed record MailRuleEvaluationRun
{
    /// <summary>Gets the account whose mail the run walks, named by its owner and its identifier together.</summary>
    public required MailAccountIdentity Account { get; init; }

    /// <summary>Gets when the run was asked for.</summary>
    public required DateTimeOffset RequestedAt { get; init; }

    /// <summary>Gets what started the run, which decides both the rules it reaches and what its executions are recorded as.</summary>
    /// <remarks>
    /// Only <see cref="MailRuleExecutionTrigger.RequestedRun" /> and <see cref="MailRuleExecutionTrigger.ScheduledRun" />
    /// occur here, because arrival is not a run at all. The distinction is not cosmetic: a requested run applies every
    /// rule the account has, and a scheduled one applies the rules that declared the schedule trigger.
    /// </remarks>
    public required MailRuleExecutionTrigger Trigger { get; init; }

    /// <summary>Gets the rule set the run is bound to, which is unspecified until the first pass picks the run up.</summary>
    /// <remarks>
    /// Bound when the run starts rather than when it is requested, because a request is answered on a thread that has no
    /// business deciding what the run will apply: the rule set may reload between the two, and the revision that matters
    /// is the one in force when the first email is actually evaluated.
    /// </remarks>
    public MailRuleSetRevision Revision { get; init; }

    /// <summary>Gets the identity of the last email the run committed, or <see langword="null" /> while it has committed none.</summary>
    public StoredEmailId? Position { get; init; }

    /// <summary>Gets how many of the account's emails the run has evaluated.</summary>
    public int EvaluatedEmailCount { get; init; }

    /// <summary>Gets how many of those emails at least one rule matched.</summary>
    public int MatchedEmailCount { get; init; }

    /// <summary>Gets how many emails the run stepped over because their body text had not been extracted yet.</summary>
    public int SkippedEmailCount { get; init; }

    /// <summary>Gets when the run stopped being outstanding, or <see langword="null" /> while it still is.</summary>
    public DateTimeOffset? EndedAt { get; init; }

    /// <summary>Gets how the run ended, which is absent for exactly as long as <see cref="EndedAt" /> is.</summary>
    public MailRuleEvaluationRunEnding? Ending { get; init; }

    /// <summary>Gets whether the run is still waiting to be carried further by an account run.</summary>
    public bool IsOutstanding => this.EndedAt is null;

    /// <summary>Answers whether this run, which is starting, may take the place of one the account already has.</summary>
    /// <param name="outstanding">The run the account currently has recorded.</param>
    /// <returns><see langword="true" /> when starting this run is right, <see langword="false" /> when it must stand down.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="outstanding" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The whole precedence rule between the two triggers, in the one place both the request path and the write that
    /// claims the account's row read it from. A run that has ended is not in anybody's way. An operator's request
    /// replaces a scheduled walk, because it reaches every rule the account has while the scheduled one reaches only the
    /// rules that opted into a schedule — answering the wider request with the narrower run would report a rule set as
    /// applied when part of it never was. Everything else stands down, including a schedule meeting a schedule.
    /// </remarks>
    public bool Supersedes(MailRuleEvaluationRun outstanding)
    {
        ArgumentNullException.ThrowIfNull(outstanding);

        return !outstanding.IsOutstanding
            || (this.Trigger is MailRuleExecutionTrigger.RequestedRun
                && outstanding.Trigger is MailRuleExecutionTrigger.ScheduledRun);
    }
}
