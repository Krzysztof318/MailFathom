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

        if (NestsDeeperThan(conditionText, bounds.MaxNestingDepth))
        {
            return MailRuleConditionCompilation.Refused(
                [$"{opening} nests more than {bounds.MaxNestingDepth} levels deep."]);
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

    /// <summary>Refuses a condition whose parentheses nest past the limit, before a parser has to recurse that deep.</summary>
    /// <remarks>
    /// <para>
    /// The walk over the parsed tree bounds depth as well, and it cannot be the only place that does: the parser is
    /// recursive-descent, so it reaches the bottom of a deeply parenthesized expression before there is a tree to walk,
    /// and running out of stack raises a failure .NET does not let anything catch. A condition nobody could read would
    /// therefore take the whole process down instead of being refused as one bad rule — the opposite of what every other
    /// refusal here does.
    /// </para>
    /// <para>
    /// Counting parentheses is deliberately cruder than the walk, and it is safe in the one direction that matters: each
    /// parenthesis is a level of the tree too, so text this refuses would have been refused after parsing anyway. What
    /// it lets through — depth an expression reaches through operators rather than brackets — is bounded by the length
    /// limit and costs the parser no recursion, and the walk still reports it. Quoted text is skipped, because a
    /// parenthesis inside a string literal opens nothing.
    /// </para>
    /// </remarks>
    private static bool NestsDeeperThan(string conditionText, int maxNestingDepth)
    {
        var depth = 0;
        var quote = '\0';

        for (var position = 0; position < conditionText.Length; position++)
        {
            var character = conditionText[position];

            if (quote != '\0')
            {
                // A quote the author escaped is part of the text rather than the end of it, so the next character is
                // consumed here as well. Reading it as the end would put the rest of a string literal outside the quotes
                // and count parentheses nothing opens.
                if (character == '\\')
                {
                    position++;
                }
                else if (character == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            switch (character)
            {
                case '\'' or '"' or '#':
                    quote = character;

                    break;
                case '(' when ++depth > maxNestingDepth:
                    return true;
                case ')' when depth > 0:
                    depth--;

                    break;
                default:
                    break;
            }
        }

        return false;
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
