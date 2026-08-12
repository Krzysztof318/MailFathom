// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Rules.Evaluation;

/// <summary>What one walk of an evaluation pass did to one account's mail.</summary>
/// <remarks>
/// Counts and MailFathom's own configured rule names, and nothing else. A rule name is restricted to letters, digits,
/// spaces, and three punctuation marks precisely so that it can be reported here and in a log line while everything
/// else about the mail it selected stays where it is.
/// </remarks>
public sealed record MailRuleEvaluationWalk
{
    /// <summary>A walk that found nothing to do.</summary>
    public static readonly MailRuleEvaluationWalk Empty = new();

    /// <summary>Gets how many emails the walk evaluated the rule set for.</summary>
    public int EvaluatedEmailCount { get; init; }

    /// <summary>Gets how many of those emails at least one rule matched.</summary>
    public int MatchedEmailCount { get; init; }

    /// <summary>Gets how many emails the walk stepped over because their body text had not been extracted yet.</summary>
    /// <remarks>
    /// A skipped email is not evaluated and not recorded as evaluated, so it stays in the arrival queue and is reached
    /// again once extraction has run for it. It is a wait rather than a failure, which is why it is counted apart from
    /// one.
    /// </remarks>
    public int SkippedEmailCount { get; init; }

    /// <summary>Gets how many rule evaluations produced no answer, counted per rule and per email rather than per email.</summary>
    public int FailedRuleCount { get; init; }

    /// <summary>Gets how many of those failures were a condition outlasting the evaluation timeout.</summary>
    /// <remarks>
    /// Separated from the rest because the two have different remedies: a timeout is answered by simplifying the
    /// condition or raising the bound, and everything else is answered by looking at the rule.
    /// </remarks>
    public int TimedOutRuleCount { get; init; }

    /// <summary>Gets the names of the rules that matched something during the walk, sorted and without repetition.</summary>
    /// <remarks>
    /// Sorted rather than kept in the order the walk met them, so two runs over the same mail report the same line: the
    /// order rules are first reached in depends on which email came first, which says nothing about the rule set.
    /// </remarks>
    public IReadOnlyList<string> MatchedRuleNames { get; init; } = [];

    /// <summary>Gets the names of the rules that failed to answer during the walk, sorted and without repetition.</summary>
    public IReadOnlyList<string> FailedRuleNames { get; init; } = [];

    /// <summary>Gets whether the walk left work behind because its batch budget ran out.</summary>
    public bool EmailsRemain { get; init; }

    /// <summary>Gets whether the walk did anything worth reporting.</summary>
    public bool IsEmpty =>
        this.EvaluatedEmailCount == 0 && this.SkippedEmailCount == 0 && !this.EmailsRemain;
}
