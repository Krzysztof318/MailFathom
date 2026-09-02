// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.Actions;

namespace MailFathom.Application.Rules;

/// <summary>What a whole rule set concluded about one email, under the revision it concluded it.</summary>
/// <remarks>
/// <para>
/// The evaluations are in declared order and stop where the pass stopped, so the record says both what every rule
/// concluded and which rules were never reached. That distinction is what a later explanation of a run needs: a rule
/// that did not match and a rule nobody asked are different facts about the same pass.
/// </para>
/// <para>
/// The action plan travels with them rather than being derived by a caller, because composing it needs the rules in
/// declared order and the caller holds only their names. Deriving it here is also what keeps the resolution of two
/// conflicting rules a property of the set rather than of whoever asks.
/// </para>
/// </remarks>
public sealed record MailRuleSetEvaluation
{
    private MailRuleSetEvaluation(
        MailRuleSetRevision revision,
        IReadOnlyList<MailRuleEvaluation> evaluations,
        bool stoppedEarly,
        MailRuleActionPlan actionPlan)
    {
        this.Revision = revision;
        this.Evaluations = evaluations;
        this.StoppedEarly = stoppedEarly;
        this.ActionPlan = actionPlan;
    }

    /// <summary>Gets the identity of the rule set the pass ran against.</summary>
    public MailRuleSetRevision Revision { get; }

    /// <summary>Gets what each rule the pass reached concluded, in declared order.</summary>
    public IReadOnlyList<MailRuleEvaluation> Evaluations { get; }

    /// <summary>Gets whether a matching rule ended the pass before the rules below it were reached.</summary>
    public bool StoppedEarly { get; }

    /// <summary>Gets what the matching rules together ask the mailbox for, in the order the changes are applied.</summary>
    public MailRuleActionPlan ActionPlan { get; }

    /// <summary>Gets the names of the rules that matched, in declared order.</summary>
    public IReadOnlyList<string> MatchedRuleNames =>
    [
        .. this.Evaluations
            .Where(evaluation => evaluation.Outcome == MailRuleOutcome.Matched)
            .Select(evaluation => evaluation.RuleName),
    ];

    /// <summary>Gets whether any rule the pass reached produced no answer.</summary>
    public bool HasFailures => this.Evaluations.Any(evaluation => evaluation.Outcome == MailRuleOutcome.Failed);

    /// <summary>Records the result of one pass over a rule set.</summary>
    /// <param name="revision">The rule set the pass ran against.</param>
    /// <param name="evaluations">What each rule the pass reached concluded, in declared order.</param>
    /// <param name="stoppedEarly">Whether a matching rule ended the pass.</param>
    /// <param name="actionPlan">What the matching rules ask the mailbox for, or nothing when they ask for no change.</param>
    /// <returns>The pass.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="evaluations" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the revision is unspecified.</exception>
    public static MailRuleSetEvaluation Create(
        MailRuleSetRevision revision,
        IReadOnlyList<MailRuleEvaluation> evaluations,
        bool stoppedEarly,
        MailRuleActionPlan? actionPlan = null)
    {
        ArgumentNullException.ThrowIfNull(evaluations);

        if (!revision.IsSpecified)
        {
            throw new ArgumentException("A pass must name the revision it ran against.", nameof(revision));
        }

        return new MailRuleSetEvaluation(
            revision,
            [.. evaluations],
            stoppedEarly,
            actionPlan ?? MailRuleActionPlan.Nothing);
    }
}
