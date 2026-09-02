// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Rules.Evaluation;

/// <summary>What one account run's evaluation step did, under the rule set it read when it began.</summary>
/// <param name="Revision">The rule set every email of the arrival walk was evaluated against.</param>
/// <param name="Arrivals">What the walk over mail no pass had evaluated did.</param>
/// <param name="RequestedRun">
/// What the walk over a requested whole-mailbox run did, or <see langword="null" /> when the account had no run
/// outstanding. A run the pass ended without evaluating anything — because the rule set moved under it — reports an
/// empty walk beside its ending rather than no walk at all.
/// </param>
/// <param name="RequestedRunEnding">
/// How the requested run stopped being outstanding, or <see langword="null" /> when it is still outstanding or when
/// there was none.
/// </param>
public sealed record MailRuleEvaluationReport(
    MailRuleSetRevision Revision,
    MailRuleEvaluationWalk Arrivals,
    MailRuleEvaluationWalk? RequestedRun,
    MailRuleEvaluationRunEnding? RequestedRunEnding)
{
    /// <summary>A pass that had no rule set to run and no mail to run it over.</summary>
    /// <param name="revision">The revision the pass read.</param>
    /// <returns>The report.</returns>
    public static MailRuleEvaluationReport Nothing(MailRuleSetRevision revision) =>
        new(revision, MailRuleEvaluationWalk.Empty, RequestedRun: null, RequestedRunEnding: null);

    /// <summary>Gets whether the pass did anything an operator would want reported.</summary>
    public bool IsEmpty => this.Arrivals.IsEmpty && this.RequestedRun is null;
}
