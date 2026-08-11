// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.Conditions;

namespace MailFathom.Application.Rules;

/// <summary>The rules one configuration revision declares, in the order it declares them, under one identity.</summary>
/// <remarks>
/// <para>
/// The order is the contract. Two rules matching one email must produce the same outcome on every run and on every
/// instance, so which of them is reached first is a property of the declared set rather than of anything observed while
/// it runs, and nothing here sorts, groups, or reorders what it was given.
/// </para>
/// <para>
/// Immutable, because a pass reads the set once when it starts and uses that instance for its duration. A rule set
/// edited while a pass is running finishes against the revision the pass began with, and the next pass reads the new
/// one.
/// </para>
/// <para>
/// The bounds travel with the set for that same reason. They are declared in the same section as the rules, so a pass
/// that has taken a set has taken the limits that set was read under; two of the three were already spent proving the
/// conditions usable, and the third bounds every evaluation the pass is about to make.
/// </para>
/// </remarks>
public sealed class MailRuleSet
{
    private MailRuleSet(IReadOnlyList<MailRule> rules, MailRuleSetRevision revision, MailRuleConditionBounds bounds)
    {
        this.Rules = rules;
        this.Revision = revision;
        this.Bounds = bounds;
    }

    /// <summary>Gets the rules in the order they were declared.</summary>
    public IReadOnlyList<MailRule> Rules { get; }

    /// <summary>Gets the identity of this revision of the rule set.</summary>
    public MailRuleSetRevision Revision { get; }

    /// <summary>Gets the bounds the rules were read under and are evaluated under.</summary>
    public MailRuleConditionBounds Bounds { get; }

    /// <summary>Gets whether the set declares any rule at all.</summary>
    public bool IsEmpty => this.Rules.Count == 0;

    /// <summary>Creates a rule set from rules that have already been proven usable.</summary>
    /// <param name="rules">The rules, in declared order.</param>
    /// <param name="revision">The identity derived from the declarations the rules were compiled from.</param>
    /// <param name="bounds">The bounds the rules were read under.</param>
    /// <returns>The rule set a pass runs against.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when a rule is <see langword="null" />, two rules share a name, or the revision is unspecified.</exception>
    /// <remarks>
    /// Names are unique because a rule is reported by its name, and two rules answering to one name would leave a record
    /// of a match naming something the configuration does not identify. Comparison ignores case, so a set cannot declare
    /// two rules an operator would read as one.
    /// </remarks>
    public static MailRuleSet Create(
        IReadOnlyList<MailRule> rules,
        MailRuleSetRevision revision,
        MailRuleConditionBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(bounds);

        if (rules.Any(rule => rule is null))
        {
            throw new ArgumentException("A rule set cannot carry a rule that is null.", nameof(rules));
        }

        if (rules.Select(rule => rule.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != rules.Count)
        {
            throw new ArgumentException("A rule set cannot declare two rules under one name.", nameof(rules));
        }

        if (!revision.IsSpecified)
        {
            throw new ArgumentException("A rule set must carry a derived revision identity.", nameof(revision));
        }

        return new MailRuleSet([.. rules], revision, bounds);
    }
}
