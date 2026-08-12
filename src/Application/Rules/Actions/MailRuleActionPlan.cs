// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Rules.Actions;

/// <summary>What one email's matching rules together ask the mailbox for, in the order the changes are applied.</summary>
/// <remarks>
/// <para>
/// One rule's actions are already proven compatible when the configuration is read; two rules matching one email are
/// not, and cannot be — which rules match is a property of the message rather than of the set. Two rules filing one
/// email into different folders is the case this resolves, and it resolves it the only way a rule set's own contract
/// allows: the rules are folded in declared order, and an action the ones before it leave no room for is withheld
/// rather than applied. Nothing here consults timing, so one email produces the same plan on every run and on every
/// instance.
/// </para>
/// <para>
/// A withheld action is named by its rule so a run can say which rule did not get its way. Without that, a rule whose
/// action was withheld reads exactly like a rule that never matched, which is the silence every part of this contract
/// exists to avoid.
/// </para>
/// </remarks>
public sealed record MailRuleActionPlan
{
    private MailRuleActionPlan(
        IReadOnlyList<PlannedMailRuleAction> actions,
        IReadOnlyList<PlannedMailRuleAction> withheldActions)
    {
        this.Actions = actions;
        this.WithheldActions = withheldActions;
        this.WithheldRuleNames =
        [
            .. withheldActions
                .Select(withheld => withheld.RuleName)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>Gets the plan of an email whose rules ask for nothing.</summary>
    public static MailRuleActionPlan Nothing { get; } = new([], []);

    /// <summary>Gets the actions to apply, in the order MailFathom applies them.</summary>
    public IReadOnlyList<PlannedMailRuleAction> Actions { get; }

    /// <summary>Gets the actions another rule had already settled, in the order they were reached.</summary>
    /// <remarks>
    /// The actions themselves rather than only the rules that declared them, because a rule declaring two changes may
    /// have one honored and one withheld, and a record saying only that the rule gave way could not say which.
    /// </remarks>
    public IReadOnlyList<PlannedMailRuleAction> WithheldActions { get; }

    /// <summary>Gets the names of the rules at least one of whose actions another rule had already settled.</summary>
    public IReadOnlyList<string> WithheldRuleNames { get; }

    /// <summary>Gets whether the plan asks for no change at all.</summary>
    public bool IsEmpty => this.Actions.Count == 0;

    /// <summary>Folds the actions of the rules that matched one email into the changes the mailbox will be asked for.</summary>
    /// <param name="matchedRules">The rules that matched, in the order the set declares them.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="matchedRules" /> is <see langword="null" />.</exception>
    public static MailRuleActionPlan Compose(IReadOnlyList<MailRule> matchedRules)
    {
        ArgumentNullException.ThrowIfNull(matchedRules);

        var honored = new List<PlannedMailRuleAction>();
        var withheld = new List<PlannedMailRuleAction>();

        foreach (var rule in matchedRules)
        {
            foreach (var (action, position) in rule.Actions.Actions.Select((action, position) => (action, position)))
            {
                var planned = new PlannedMailRuleAction(rule.Name, action, position);

                if (MailRuleActionSet.FindRefusal([.. honored.Select(honoredAction => honoredAction.Action)], action) is not null)
                {
                    withheld.Add(planned);

                    continue;
                }

                honored.Add(planned);
            }
        }

        if (honored.Count == 0 && withheld.Count == 0)
        {
            return Nothing;
        }

        // Ordered across rules rather than within one, so a flag declared by a later rule is still written before an
        // earlier rule moves the occurrence it would have been written on.
        return new MailRuleActionPlan(
            [.. honored.OrderBy(planned => MailRuleActionSet.ApplicationOrderOf(planned.Action))],
            withheld);
    }
}
