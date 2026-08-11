// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Rules.Conditions;
using NCalc;
using NCalc.Helpers;

namespace MailFathom.Infrastructure.Rules;

/// <summary>Reads one authored condition, refusing everything the expression language would have let through.</summary>
/// <remarks>
/// <para>
/// Four things are checked here and each of them is checked because nothing else would. Length, before the text reaches
/// a parser at all. Syntax, which is the one thing the language reports itself. Then the walk that gives every part of
/// the tree a type, which is where an unknown fact, a function that does not exist, an operator this surface does not
/// admit, and a comparison that could never hold are all refused. Finally the root's own type, because a condition that
/// answers with text or a number is not a condition, and treating a non-boolean as truthy would make a rule's meaning
/// depend on a coercion nobody wrote down.
/// </para>
/// <para>
/// Everything is reported together rather than at the first defect, because an operator fixing a rule set should not
/// have to restart a deployment to discover the next mistake in it.
/// </para>
/// <para>
/// Public rather than internal, unlike most of this project, because the composition root constructs one directly. A
/// rule set is proven usable while the host is being composed, which is before the container exists and therefore
/// before anything could be resolved from it. The type holds no state, so one instance serves composition, every
/// reload, and every pass.
/// </para>
/// </remarks>
public sealed class NCalcMailRuleConditionCompiler : IMailRuleConditionCompiler
{
    /// <summary>The settings every condition of every rule set is parsed and evaluated under.</summary>
    /// <remarks>
    /// <para>
    /// Text comparison is ordinal and ignores case. Ignoring case is what an owner means when they compare a sender
    /// domain, and ordinal rather than culture-aware is what makes two instances of one deployment agree whatever
    /// locale their hosts are set to.
    /// </para>
    /// <para>
    /// Arithmetic is checked for overflow, so a computation that leaves the range of its type raises rather than wrapping
    /// silently — which the pass then records as a rule that failed, in the one place a wrapped value would otherwise
    /// have quietly changed what a rule matched.
    /// </para>
    /// <para>
    /// The language's parse cache is off. A compiled condition holds its own tree, so the cache would buy nothing, and
    /// leaving it on would keep every authored condition in a process-wide static for the lifetime of the process.
    /// </para>
    /// </remarks>
    private static readonly ExpressionConfiguration Configuration = new()
    {
        CacheEnabled = false,
        Evaluation = new ExpressionEvaluationOptions
        {
            StringComparer = StringComparer.OrdinalIgnoreCase,
            Math = new MathOptions { OverflowProtection = true },
        },
    };

    /// <inheritdoc />
    public MailRuleConditionCompilation Compile(string ruleName, string? conditionText, MailRuleConditionBounds bounds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleName);
        ArgumentNullException.ThrowIfNull(bounds);

        var opening = MailRuleConditionMessage.For(ruleName);

        if (string.IsNullOrWhiteSpace(conditionText))
        {
            return MailRuleConditionCompilation.Refused(
                [$"{opening} is missing. A rule states what it matches, so there is no condition that means 'everything'."]);
        }

        if (conditionText.Length > bounds.MaxLength)
        {
            return MailRuleConditionCompilation.Refused(
                [$"{opening} is {conditionText.Length} characters long, and a condition may be at most {bounds.MaxLength}."]);
        }

        var expression = new Expression(conditionText, Configuration, cultureInfo: CultureInfo.InvariantCulture);

        if (expression.HasErrors())
        {
            // Only the parser's own positional message is echoed, never the condition it came from: a condition can
            // legitimately carry an address its author typed, and a startup failure is written to a log.
            return MailRuleConditionCompilation.Refused(
                [$"{opening} could not be parsed. {(expression.Error.InnerException ?? expression.Error).Message}"]);
        }

        return CheckParsed(ruleName, expression.LogicalExpression!, bounds, opening);
    }

    private static MailRuleConditionCompilation CheckParsed(
        string ruleName,
        LogicalExpression parsedCondition,
        MailRuleConditionBounds bounds,
        string opening)
    {
        var checker = new MailRuleExpressionTypeChecker(ruleName, bounds.MaxNestingDepth);
        var producedType = parsedCondition.Accept(checker);

        if (checker.Errors.Count > 0)
        {
            return MailRuleConditionCompilation.Refused(checker.Errors);
        }

        if (producedType is not MailRuleExpressionType.Boolean)
        {
            return MailRuleConditionCompilation.Refused(
                [$"{opening} produces {MailRuleExpressionSurface.Describe(producedType)} rather than a boolean, so it says nothing about whether an email matches."]);
        }

        return MailRuleConditionCompilation.Compiled(
            new NCalcMailRuleCondition(ruleName, parsedCondition, Configuration, checker.ReferencedFacts));
    }
}
