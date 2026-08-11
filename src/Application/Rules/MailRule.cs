// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.Conditions;

namespace MailFathom.Application.Rules;

/// <summary>One rule of a bound rule set: a name, a condition proven usable, and what a match does to the pass.</summary>
/// <remarks>
/// Carries a compiled condition rather than the text it was written from, so nothing downstream of binding can read what
/// an operator typed. What a rule can be reported by is its name, which is MailFathom's own configured name for it and
/// carries no personal data of its own.
/// </remarks>
public sealed class MailRule
{
    private MailRule(string name, IMailRuleCondition condition, bool stopWhenMatched)
    {
        this.Name = name;
        this.Condition = condition;
        this.StopWhenMatched = stopWhenMatched;
    }

    /// <summary>Gets the name the rule is declared and reported under.</summary>
    public string Name { get; }

    /// <summary>Gets the condition an email is matched against.</summary>
    public IMailRuleCondition Condition { get; }

    /// <summary>Gets whether a match ends the pass rather than continuing to the rules declared below this one.</summary>
    public bool StopWhenMatched { get; }

    /// <summary>Creates a rule from a condition that has already been proven usable.</summary>
    /// <param name="name">The name the rule is declared and reported under.</param>
    /// <param name="condition">The compiled condition.</param>
    /// <param name="stopWhenMatched">Whether a match ends the pass.</param>
    /// <returns>The rule.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="condition" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name" /> is empty or whitespace.</exception>
    public static MailRule Create(string name, IMailRuleCondition condition, bool stopWhenMatched)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(condition);

        return new MailRule(name, condition, stopWhenMatched);
    }
}
