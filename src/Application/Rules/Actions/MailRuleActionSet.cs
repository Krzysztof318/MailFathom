// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Rules.Actions;

/// <summary>What one rule does to a matching email, as the actions it declared in the order MailFathom applies them.</summary>
/// <remarks>
/// <para>
/// A set rather than one value, because the ordinary cases are combinations: file it and mark it read, copy it to an
/// archive folder and mark it read. What the set refuses is a combination naming two fates for one occurrence, and it
/// refuses it where the configuration is read rather than resolving it at run time — whichever of two such actions ran
/// second would act on a message that is no longer where the rule matched it, and no resolution invented here would be
/// the one the operator meant.
/// </para>
/// <para>
/// One rule therefore names at most one fate — relocate, copy, or delete — and a delete admits nothing beside it, since
/// a flag written on a message being removed is a flag nobody will ever read. The second thing it refuses is two
/// answers about one message's keywords: a replacement states the whole set, so an addition or a removal beside it
/// would leave what the message carries decided by which of the two ran second. Everything else is permitted, which in
/// practice is any combination of flag and keyword changes beside at most one fate.
/// </para>
/// <para>
/// The order is MailFathom's rather than the order the actions were written in: the flags and keywords are written
/// first and the relocation or the delete last, so every permitted combination acts on the occurrence the condition
/// matched. It is fixed here so that it is the same on every run and on every instance.
/// </para>
/// <para>
/// An empty set is permitted and is not a defect. A rule that declares no action selects mail and nothing more, which is
/// what a rule ending the pass with <c>StopWhenMatched</c> does to keep the mail it names away from the rules below it.
/// </para>
/// </remarks>
public sealed class MailRuleActionSet
{
    private MailRuleActionSet(IReadOnlyList<MailRuleAction> actions) => this.Actions = actions;

    /// <summary>Gets the set a rule that does nothing to the mail it selects declares.</summary>
    public static MailRuleActionSet Empty { get; } = new([]);

    /// <summary>Gets the actions in the order MailFathom applies them.</summary>
    public IReadOnlyList<MailRuleAction> Actions { get; }

    /// <summary>Gets whether the rule asks for no change at all.</summary>
    public bool IsEmpty => this.Actions.Count == 0;

    /// <summary>Reports every reason the declared actions could not be honored together.</summary>
    /// <param name="ruleName">The rule the actions belong to, which every message names.</param>
    /// <param name="actions">The actions as they were declared.</param>
    /// <returns>One message per refused action, empty when the combination is one MailFathom applies.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actions" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Reported rather than thrown, because this is what an operator reads while their configuration is being validated
    /// and every defect in a rule set is reported together. <see cref="Create" /> refuses the same combinations, which
    /// is what keeps a set built without this check from reaching a mailbox.
    /// </remarks>
    public static IReadOnlyList<string> FindErrors(string ruleName, IReadOnlyList<MailRuleAction> actions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleName);
        ArgumentNullException.ThrowIfNull(actions);

        return
        [
            .. FindRefusedActions(actions)
                .Select(refused => $"Rule '{ruleName}' declares {Describe(refused.Action)}, which {refused.Refusal}"),
        ];
    }

    /// <summary>Builds the set of actions a rule applies, in the order they are applied.</summary>
    /// <param name="actions">The actions as they were declared, in any order.</param>
    /// <returns>The set.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actions" /> is <see langword="null" />, or one of them is.</exception>
    /// <exception cref="ArgumentException">Thrown when the actions name a combination MailFathom refuses, which validation reports first.</exception>
    public static MailRuleActionSet Create(IReadOnlyList<MailRuleAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);

        if (actions.Any(action => action is null))
        {
            throw new ArgumentException("A rule cannot declare an action that is null.", nameof(actions));
        }

        if (actions.Count == 0)
        {
            return Empty;
        }

        // Reaching this means a rule set was mapped without having been proven usable, which is a defect in the
        // composition rather than in what an operator wrote, so the reasons are joined in rather than dropped.
        var refusals = FindRefusedActions(actions);

        if (refusals.Count > 0)
        {
            throw new ArgumentException(
                $"A rule declares actions that cannot be honored together. {string.Join(" ", refusals.Select(refused => $"{Describe(refused.Action)} {refused.Refusal}"))}",
                nameof(actions));
        }

        return new MailRuleActionSet([.. actions.OrderBy(ApplicationOrderOf)]);
    }

    /// <summary>Reports why one further action cannot join the actions already being applied to one email.</summary>
    /// <param name="honored">The actions that will be applied, which may come from more than one rule.</param>
    /// <param name="candidate">The action being considered.</param>
    /// <returns>The reason it is refused, or <see langword="null" /> when it can be applied beside them.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// Written over an accumulated list rather than over one rule's declaration, because two rules matching one email
    /// can name incompatible fates exactly as one rule can. Reading both cases through this method is what makes the
    /// answer the same in either: whichever fate is reached first in declared order is the one applied.
    /// </remarks>
    public static string? FindRefusal(IReadOnlyCollection<MailRuleAction> honored, MailRuleAction candidate)
    {
        ArgumentNullException.ThrowIfNull(honored);
        ArgumentNullException.ThrowIfNull(candidate);

        if (honored.Any(action => action.Mutation == candidate.Mutation))
        {
            return "is already asked for; one email is changed at most once each way, and a second destination is a second rule.";
        }

        if (honored.Any(action => action.Mutation == MailboxMutation.Delete))
        {
            return "cannot be honored beside the deletion of the same message, which leaves nothing for it to act on.";
        }

        if (candidate.Mutation == MailboxMutation.Delete && honored.Count > 0)
        {
            return "cannot be honored beside another change to the same message, which the deletion would undo.";
        }

        if (NamesAFate(candidate) && honored.Any(NamesAFate))
        {
            return "names a second fate for one occurrence; whichever ran second would act on a message that is no longer where the rule matched it.";
        }

        if (ContradictsAKeywordReplacement(honored, candidate))
        {
            return "cannot be honored beside a replacement of the same message's keywords, which decides what it carries and what it does not.";
        }

        return null;
    }

    /// <summary>Reports whether one keyword action and another already being applied say two things about one set.</summary>
    /// <remarks>
    /// A replacement states the whole set, so an addition or a removal beside it is an operator saying the set is both
    /// exactly this and this plus one more. Which of the two won would come down to the order they were applied in,
    /// which is exactly the kind of answer this contract refuses to invent — so the pair is refused where the
    /// configuration is read, and withheld where it arrives from two rules at once.
    /// </remarks>
    private static bool ContradictsAKeywordReplacement(
        IReadOnlyCollection<MailRuleAction> honored,
        MailRuleAction candidate) =>
        (candidate.Mutation == MailboxMutation.SetKeywords && honored.Any(ChangesKeywordsIncrementally))
        || (ChangesKeywordsIncrementally(candidate)
            && honored.Any(action => action.Mutation == MailboxMutation.SetKeywords));

    /// <summary>Reports whether an action changes some of a message's keywords rather than all of them.</summary>
    private static bool ChangesKeywordsIncrementally(MailRuleAction action) =>
        action.Mutation == MailboxMutation.AddKeywords || action.Mutation == MailboxMutation.RemoveKeywords;

    /// <summary>Walks the declared actions in order and reports each one the ones before it leave no room for.</summary>
    private static List<RefusedMailRuleAction> FindRefusedActions(IReadOnlyList<MailRuleAction> actions)
    {
        var honored = new List<MailRuleAction>(actions.Count);
        var refused = new List<RefusedMailRuleAction>();

        foreach (var action in actions)
        {
            if (FindRefusal(honored, action) is { } refusal)
            {
                refused.Add(new RefusedMailRuleAction(action, refusal));

                continue;
            }

            honored.Add(action);
        }

        return refused;
    }

    /// <summary>Reports whether an action decides where the matched occurrence ends up, which at most one action may.</summary>
    private static bool NamesAFate(MailRuleAction action) =>
        action.Mutation == MailboxMutation.Relocate
        || action.Mutation == MailboxMutation.Copy
        || action.Mutation == MailboxMutation.Delete;

    /// <summary>Ranks one action within the fixed order every permitted combination is applied in.</summary>
    /// <param name="action">The action to rank.</param>
    /// <returns>Its position in the order, lowest first.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// Every flag and keyword change first and the relocation or the delete last, so every permitted combination acts on
    /// the occurrence the condition matched. It is published because the same order governs the actions of two rules
    /// matching one email, which no single rule's set can order on its own. A closed enumeration's members are not
    /// compile-time constants, so the rank is decided by comparison rather than by a switch over cases.
    /// </para>
    /// <para>
    /// The order among the flag and keyword changes decides nothing observable — each writes a different flag, and the
    /// one pair that would contradict each other is refused before it gets here — so it is fixed for determinism rather
    /// than for meaning.
    /// </para>
    /// </remarks>
    public static int ApplicationOrderOf(MailRuleAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var mutation = action.Mutation;

        if (mutation == MailboxMutation.SetSeen)
        {
            return 0;
        }

        if (mutation == MailboxMutation.SetFlagged)
        {
            return 1;
        }

        if (mutation == MailboxMutation.SetKeywords)
        {
            return 2;
        }

        if (mutation == MailboxMutation.RemoveKeywords)
        {
            return 3;
        }

        if (mutation == MailboxMutation.AddKeywords)
        {
            return 4;
        }

        if (mutation == MailboxMutation.Copy)
        {
            return 5;
        }

        return mutation == MailboxMutation.Relocate ? 6 : 7;
    }

    /// <summary>Names one action the way an operator wrote it, so a refusal points at the key they edit.</summary>
    private static string Describe(MailRuleAction action) => action switch
    {
        { Destination: { } destination } => $"'{action.Mutation.Name}' into '{destination}'",
        { DesiredSeenState: { } isSeen } => $"'{action.Mutation.Name}' to {(isSeen ? "read" : "unread")}",
        { DesiredFlaggedState: { } isFlagged } => $"'{action.Mutation.Name}' to {(isFlagged ? "flagged" : "unflagged")}",
        { Keywords: { IsEmpty: true } } => $"'{action.Mutation.Name}' naming none",
        { Keywords: { } keywords } => $"'{action.Mutation.Name}' naming '{string.Join("', '", keywords.Values)}'",
        _ => $"'{action.Mutation.Name}'",
    };

    /// <summary>One declared action and the reason the actions before it leave no room for it.</summary>
    private readonly record struct RefusedMailRuleAction(MailRuleAction Action, string Refusal);
}
