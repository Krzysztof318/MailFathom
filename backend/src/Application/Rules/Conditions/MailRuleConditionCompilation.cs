// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Application.Rules.Conditions;

/// <summary>What reading one condition produced: either a condition usable against mail, or every reason it is not.</summary>
/// <remarks>
/// A result type rather than an exception, because the caller is the configuration binder and it acts on the failure: it
/// collects the reasons from every rule of the set and reports them together, so an operator who mistyped three
/// conditions fixes three rather than finding the next one on the next restart.
/// </remarks>
public sealed class MailRuleConditionCompilation
{
    private MailRuleConditionCompilation(IMailRuleCondition? condition, IReadOnlyList<string> errors)
    {
        this.Condition = condition;
        this.Errors = errors;
    }

    /// <summary>Gets the compiled condition, which is present exactly when <see cref="IsCompiled" /> is set.</summary>
    public IMailRuleCondition? Condition { get; }

    /// <summary>Gets one message per reason the condition cannot be used, each naming the rule, the defect, and where it is.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>Gets whether the condition can be used against mail.</summary>
    [MemberNotNullWhen(true, nameof(Condition))]
    public bool IsCompiled => this.Condition is not null;

    /// <summary>Reports a condition that parsed, type-checked, and answers with a boolean.</summary>
    /// <param name="condition">The compiled condition.</param>
    /// <returns>The successful reading.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="condition" /> is <see langword="null" />.</exception>
    public static MailRuleConditionCompilation Compiled(IMailRuleCondition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);

        return new MailRuleConditionCompilation(condition, []);
    }

    /// <summary>Reports a condition that cannot be used, with every reason found while reading it.</summary>
    /// <param name="errors">One message per defect, each naming the rule, what was wrong, and where.</param>
    /// <returns>The refusal.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="errors" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="errors" /> is empty, which would report a refusal nobody could act on.</exception>
    public static MailRuleConditionCompilation Refused(IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        if (errors.Count == 0)
        {
            throw new ArgumentException("A refused condition must carry at least one reason.", nameof(errors));
        }

        return new MailRuleConditionCompilation(condition: null, errors);
    }
}
