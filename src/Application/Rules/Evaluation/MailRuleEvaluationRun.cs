// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Rules.Evaluation;

/// <summary>A whole-mailbox rule run somebody asked for, and how far the account's synchronization runs have carried it.</summary>
/// <remarks>
/// <para>
/// Durable because it has to survive the process. The run spans as many account runs as its batch budget needs, so a
/// restart in the middle of one must resume at the email nobody has reached rather than at the beginning of a mailbox —
/// and a request that arrived seconds before a shutdown must still be a request afterwards.
/// </para>
/// <para>
/// One outstanding run per account, which is what makes a second request an answer rather than a second walk. There is
/// no queue behind it: asking twice for the same thing is asking once, and the reply says the run is already under way.
/// </para>
/// <para>
/// Every field is either MailFathom's own identity for something or a count. Nothing derived from a message belongs in
/// a record an operator reads to find out what their instance is doing.
/// </para>
/// </remarks>
public sealed record MailRuleEvaluationRun
{
    /// <summary>Gets the account whose mail the run walks.</summary>
    public required MailAccountId AccountId { get; init; }

    /// <summary>Gets when the run was asked for.</summary>
    public required DateTimeOffset RequestedAt { get; init; }

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
}
