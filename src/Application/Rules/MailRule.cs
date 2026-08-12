// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Frozen;
using MailFathom.Application.Rules.Actions;
using MailFathom.Application.Rules.Conditions;

namespace MailFathom.Application.Rules;

/// <summary>One rule of a bound rule set: a name, the accounts it applies to, a condition, and what a match does.</summary>
/// <remarks>
/// Carries a compiled condition rather than the text it was written from, so nothing downstream of binding can read what
/// an operator typed. What a rule can be reported by is its name, which is MailFathom's own configured name for it and
/// carries no personal data of its own.
/// </remarks>
public sealed class MailRule
{
    private MailRule(
        string name,
        IMailRuleCondition condition,
        MailRuleActionSet actions,
        bool stopWhenMatched,
        FrozenSet<string> accounts)
    {
        this.Name = name;
        this.Condition = condition;
        this.Actions = actions;
        this.StopWhenMatched = stopWhenMatched;
        this.Accounts = accounts;
    }

    /// <summary>Gets the name the rule is declared and reported under.</summary>
    public string Name { get; }

    /// <summary>Gets the condition an email is matched against.</summary>
    public IMailRuleCondition Condition { get; }

    /// <summary>Gets what a match does to the matching email, in the order the changes are applied.</summary>
    /// <remarks>
    /// Empty for a rule that selects mail and changes nothing, which is what a rule ending the pass declares to keep the
    /// mail it names away from the rules below it.
    /// </remarks>
    public MailRuleActionSet Actions { get; }

    /// <summary>Gets whether a match ends the pass rather than continuing to the rules declared below this one.</summary>
    public bool StopWhenMatched { get; }

    /// <summary>Gets the accounts this rule applies to, empty for a rule that applies to every account.</summary>
    /// <remarks>
    /// One rule reaches one or more accounts, and a rule that names none is general. Empty means every account rather
    /// than no account, because a rule reaching nothing is a rule nobody would write, and reading absence that way is
    /// what lets a deployment with one account write the scope nowhere.
    /// </remarks>
    public IReadOnlySet<string> Accounts { get; }

    /// <summary>Creates a rule from a condition that has already been proven usable.</summary>
    /// <param name="name">The name the rule is declared and reported under.</param>
    /// <param name="condition">The compiled condition.</param>
    /// <param name="actions">What a match does to the matching email, or nothing for a rule that changes nothing.</param>
    /// <param name="stopWhenMatched">Whether a match ends the pass.</param>
    /// <param name="accounts">The accounts the rule applies to, or nothing for a rule that applies to every account.</param>
    /// <returns>The rule.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="condition" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name" /> is empty or whitespace, or an account is.</exception>
    public static MailRule Create(
        string name,
        IMailRuleCondition condition,
        MailRuleActionSet? actions = null,
        bool stopWhenMatched = false,
        IReadOnlyList<string>? accounts = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(condition);

        if (accounts?.Any(string.IsNullOrWhiteSpace) == true)
        {
            throw new ArgumentException("A rule cannot be scoped to an account with no identifier.", nameof(accounts));
        }

        return new MailRule(
            name,
            condition,
            actions ?? MailRuleActionSet.Empty,
            stopWhenMatched,
            accounts is null ? FrozenSet<string>.Empty : accounts.Select(account => account.Trim()).ToFrozenSet(StringComparer.Ordinal));
    }

    /// <summary>Reports whether this rule is one of the rules the given account's mail is passed through.</summary>
    /// <param name="account">The configured identifier of the account the email belongs to.</param>
    /// <returns><see langword="true" /> when the rule is general or names this account.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="account" /> is empty or whitespace.</exception>
    /// <remarks>
    /// The comparison is ordinal, which is how the synchronization section already tells two account identifiers apart:
    /// two accounts differing only in case are two accounts there, so a scope that matched them both here would send one
    /// account's mail through the other's rules.
    /// </remarks>
    public bool AppliesTo(string account)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account);

        return this.Accounts.Count == 0 || this.Accounts.Contains(account.Trim());
    }
}
