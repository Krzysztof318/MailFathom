// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Rules.Conditions;

/// <summary>Reads one authored condition and either compiles it or reports everything wrong with it.</summary>
/// <remarks>
/// <para>
/// The port exists so that the expression language stays inside one adapter. Everything above this line knows that a
/// condition is text an operator wrote and that reading it either succeeds or produces messages; nothing above it knows
/// which language the text is in, and no type from that language crosses the boundary.
/// </para>
/// <para>
/// Compilation runs when a rule set is bound, which is before any mail has been seen — at startup, and again for every
/// candidate a configuration reload produces. That placement is the whole point: the language has no static type
/// checker of its own, so a fact that does not exist or a comparison that cannot hold would otherwise be discovered on
/// somebody's real mail.
/// </para>
/// </remarks>
public interface IMailRuleConditionCompiler
{
    /// <summary>Reads one rule's condition under the bounds the rule set was declared with.</summary>
    /// <param name="ruleName">The rule the condition belongs to, which every message names.</param>
    /// <param name="conditionText">The condition as the operator wrote it.</param>
    /// <param name="bounds">The length, depth, and timeout the condition is read and run under.</param>
    /// <returns>The compiled condition, or every reason it cannot be used.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="bounds" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="ruleName" /> is empty or whitespace.</exception>
    MailRuleConditionCompilation Compile(string ruleName, string? conditionText, MailRuleConditionBounds bounds);
}
