// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.Facts;
using NCalc;
using NCalc.Visitors;

namespace MailFathom.Infrastructure.Rules;

/// <summary>Walks a parsed condition and reports everything the expression language itself would not have noticed.</summary>
/// <remarks>
/// <para>
/// The language checks syntax and nothing else: not whether an identifier names a fact, not whether a function exists,
/// and not whether a comparison could ever hold. This walk supplies all three by giving every part of the tree a type
/// from the declared fact surface and judging each operator and call against the types of its operands. It runs when the
/// configuration is read, so an unknown fact and an impossible comparison are refused before any mail is seen rather
/// than raised over somebody's real correspondence.
/// </para>
/// <para>
/// It also produces the two things an evaluation needs afterwards: the facts the condition names, which are the only
/// ones an evaluation may resolve, and the depth of the tree, which is one half of the cost bound.
/// </para>
/// <para>
/// A part already reported as wrong carries <see cref="MailRuleExpressionType.Invalid" /> upwards, and every rule here
/// passes that through without adding a message of its own. Otherwise one mistyped fact name would produce a message
/// for itself and another for every operator above it.
/// </para>
/// </remarks>
internal sealed class MailRuleExpressionTypeChecker : ILogicalExpressionVisitor<MailRuleExpressionType>
{
    private readonly string ruleName;
    private readonly int maxNestingDepth;
    private readonly List<string> errors = [];
    private readonly List<MailRuleFact> referencedFacts = [];

    private int depth;
    private bool depthAlreadyReported;

    /// <summary>Initializes a walk over one rule's condition.</summary>
    /// <param name="ruleName">The rule the condition belongs to, which every message names.</param>
    /// <param name="maxNestingDepth">The greatest depth the tree may reach, counting the whole expression as one level.</param>
    public MailRuleExpressionTypeChecker(string ruleName, int maxNestingDepth)
    {
        this.ruleName = ruleName;
        this.maxNestingDepth = maxNestingDepth;
    }

    /// <summary>Gets one message per defect found, each naming the rule, what is wrong, and which part of the condition it is in.</summary>
    public IReadOnlyList<string> Errors => this.errors;

    /// <summary>Gets the facts the condition names, without repeats and in the order they were first met.</summary>
    public IReadOnlyList<MailRuleFact> ReferencedFacts => this.referencedFacts;

    /// <inheritdoc />
    public MailRuleExpressionType Visit(Identifier identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        if (!MailRuleFact.TryParseName(identifier.Name, out var fact))
        {
            return this.Refuse(
                $"names '{identifier.Name}', which is not a fact a condition can read. The facts are {DescribeFacts()}.");
        }

        if (!this.referencedFacts.Contains(fact))
        {
            this.referencedFacts.Add(fact);
        }

        return fact.ValueType switch
        {
            MailRuleFactType.Text => MailRuleExpressionType.Text,
            MailRuleFactType.TextSet => MailRuleExpressionType.TextSet,
            MailRuleFactType.Number => MailRuleExpressionType.Number,
            MailRuleFactType.Boolean => MailRuleExpressionType.Boolean,
            _ => MailRuleExpressionType.Timestamp,
        };
    }

    /// <inheritdoc />
    /// <remarks>
    /// The literal's own runtime value decides its type rather than the parser's classification, which keeps this file
    /// clear of the parser's <c>ValueType</c> — a name the base class library also publishes, and one that would have to
    /// be spelled out at every use here to stay unambiguous.
    /// </remarks>
    public MailRuleExpressionType Visit(ValueExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        return expression.Value switch
        {
            string => MailRuleExpressionType.Text,
            bool => MailRuleExpressionType.Boolean,
            DateTime => MailRuleExpressionType.Timestamp,
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal =>
                MailRuleExpressionType.Number,
            _ => this.Refuse("carries a literal that is not text, a number, a boolean, or a date."),
        };
    }

    /// <inheritdoc />
    public MailRuleExpressionType Visit(UnaryExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        this.depth++;

        try
        {
            if (this.IsTooDeep())
            {
                return MailRuleExpressionType.Invalid;
            }

            var operand = expression.Expression.Accept(this);

            if (!MailRuleExpressionSurface.UnaryOperators.ContainsKey(expression.Type))
            {
                return this.Refuse(
                    $"uses the operator '{MailRuleExpressionSurface.Describe(expression.Type)}', which a condition may not use.");
            }

            if (operand is MailRuleExpressionType.Invalid)
            {
                return MailRuleExpressionType.Invalid;
            }

            var required = expression.Type is UnaryExpressionType.Not
                ? MailRuleExpressionType.Boolean
                : MailRuleExpressionType.Number;

            return operand == required
                ? required
                : this.RefuseOperand(MailRuleExpressionSurface.Describe(expression.Type), required, operand);
        }
        finally
        {
            this.depth--;
        }
    }

    /// <inheritdoc />
    public MailRuleExpressionType Visit(BinaryExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        this.depth++;

        try
        {
            if (this.IsTooDeep())
            {
                return MailRuleExpressionType.Invalid;
            }

            return expression.Type is BinaryExpressionType.In or BinaryExpressionType.NotIn
                ? this.CheckMembership(expression)
                : this.CheckBinaryOperator(expression);
        }
        finally
        {
            this.depth--;
        }
    }

    /// <inheritdoc />
    public MailRuleExpressionType Visit(TernaryExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        this.depth++;

        try
        {
            if (this.IsTooDeep())
            {
                return MailRuleExpressionType.Invalid;
            }

            var condition = expression.LeftExpression.Accept(this);
            var whenTrue = expression.MiddleExpression.Accept(this);
            var whenFalse = expression.RightExpression.Accept(this);

            return this.CheckChoice("the '? :' operator", condition, whenTrue, whenFalse);
        }
        finally
        {
            this.depth--;
        }
    }

    /// <inheritdoc />
    public MailRuleExpressionType Visit(Function function)
    {
        ArgumentNullException.ThrowIfNull(function);

        this.depth++;

        try
        {
            if (this.IsTooDeep())
            {
                return MailRuleExpressionType.Invalid;
            }

            var name = function.Identifier.Name;
            var arguments = function.Parameters.Select(argument => argument.Accept(this)).ToArray();

            if (!MailRuleExpressionSurface.FunctionNames.Contains(name, StringComparer.Ordinal))
            {
                return this.Refuse(
                    $"calls '{name}', which is not a function a condition can call. The functions are {string.Join(", ", MailRuleExpressionSurface.FunctionNames.Select(available => $"'{available}'"))}.");
            }

            return arguments.Contains(MailRuleExpressionType.Invalid)
                ? MailRuleExpressionType.Invalid
                : this.CheckCall(name, arguments);
        }
        finally
        {
            this.depth--;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// A list is legitimate only to the right of a membership test, and that position is checked without dispatching
    /// here. Reaching this method therefore means the list is somewhere the surface does not admit one.
    /// </remarks>
    public MailRuleExpressionType Visit(LogicalExpressionList list)
    {
        ArgumentNullException.ThrowIfNull(list);

        foreach (var element in list)
        {
            element.Accept(this);
        }

        return this.Refuse("writes a parenthesized list of values somewhere other than to the right of 'in'.");
    }

    private static string DescribeFacts() =>
        string.Join(", ", MailRuleFact.All.Select(fact => $"'{fact.Name}'"));

    private MailRuleExpressionType CheckBinaryOperator(BinaryExpression expression)
    {
        var left = expression.LeftExpression.Accept(this);
        var right = expression.RightExpression.Accept(this);
        var written = MailRuleExpressionSurface.Describe(expression.Type);

        if (!MailRuleExpressionSurface.BinaryOperators.ContainsKey(expression.Type))
        {
            return this.Refuse($"uses the operator '{written}', which a condition may not use.");
        }

        if (left is MailRuleExpressionType.Invalid || right is MailRuleExpressionType.Invalid)
        {
            return MailRuleExpressionType.Invalid;
        }

        return expression.Type switch
        {
            BinaryExpressionType.And or BinaryExpressionType.Or =>
                this.RequireBoth(written, MailRuleExpressionType.Boolean, left, right, MailRuleExpressionType.Boolean),
            BinaryExpressionType.Equal or BinaryExpressionType.NotEqual =>
                this.CheckEquality(written, left, right),
            BinaryExpressionType.Lesser or BinaryExpressionType.LesserOrEqual
                or BinaryExpressionType.Greater or BinaryExpressionType.GreaterOrEqual =>
                this.CheckOrdering(written, left, right),
            _ => this.RequireBoth(written, MailRuleExpressionType.Number, left, right, MailRuleExpressionType.Number),
        };
    }

    /// <summary>Checks a membership test, which is the one place a parenthesized list of values belongs.</summary>
    /// <remarks>
    /// The listed values are compared against the left operand one at a time, so the list is bounded by the length limit
    /// on the condition and costs nothing beyond it. A fact that is itself a set is tested with <c>contains</c> instead,
    /// which keeps the two shapes of membership from being written the same way and read differently.
    /// </remarks>
    private MailRuleExpressionType CheckMembership(BinaryExpression expression)
    {
        var left = expression.LeftExpression.Accept(this);
        var written = MailRuleExpressionSurface.Describe(expression.Type);

        if (expression.RightExpression is not LogicalExpressionList candidates)
        {
            expression.RightExpression.Accept(this);

            return this.Refuse(
                $"writes '{written}' without a parenthesized list of values after it, as in \"folder in ('inbox', 'archive')\".");
        }

        var candidateTypes = candidates.Select(candidate => candidate.Accept(this)).ToArray();

        if (left is MailRuleExpressionType.Invalid || candidateTypes.Contains(MailRuleExpressionType.Invalid))
        {
            return MailRuleExpressionType.Invalid;
        }

        if (left is MailRuleExpressionType.TextSet or MailRuleExpressionType.Boolean)
        {
            return this.Refuse(
                $"tests {MailRuleExpressionSurface.Describe(left)} for membership with '{written}', which compares text, a number, or a timestamp against a list. Use 'contains' for a text set.");
        }

        var mismatch = candidateTypes
            .Where(candidate => candidate != left)
            .Select(candidate => (MailRuleExpressionType?)candidate)
            .FirstOrDefault();

        return mismatch is { } wrongType
            ? this.Refuse(
                $"lists {MailRuleExpressionSurface.Describe(wrongType)} after '{written}' while comparing {MailRuleExpressionSurface.Describe(left)}, so that value could never match.")
            : MailRuleExpressionType.Boolean;
    }

    private MailRuleExpressionType CheckCall(string name, IReadOnlyList<MailRuleExpressionType> arguments) => name switch
    {
        MailRuleExpressionSurface.If => this.CheckIf(arguments),
        MailRuleExpressionSurface.In => this.CheckInCall(arguments),
        MailRuleExpressionSurface.IsNull => this.CheckArity(name, arguments, 1)
            ? MailRuleExpressionType.Boolean
            : MailRuleExpressionType.Invalid,
        MailRuleExpressionSurface.IsNullOrEmpty => this.CheckTextArguments(name, arguments, 1),
        MailRuleExpressionSurface.Contains => this.CheckContains(arguments),
        _ => this.CheckTextArguments(name, arguments, 2),
    };

    private MailRuleExpressionType CheckIf(IReadOnlyList<MailRuleExpressionType> arguments) =>
        this.CheckArity(MailRuleExpressionSurface.If, arguments, 3)
            ? this.CheckChoice($"'{MailRuleExpressionSurface.If}'", arguments[0], arguments[1], arguments[2])
            : MailRuleExpressionType.Invalid;

    private MailRuleExpressionType CheckInCall(IReadOnlyList<MailRuleExpressionType> arguments)
    {
        if (arguments.Count < 2)
        {
            return this.Refuse(
                $"calls '{MailRuleExpressionSurface.In}' with {arguments.Count} argument(s), and it takes a value followed by at least one value to compare it against.");
        }

        var value = arguments[0];

        if (value is MailRuleExpressionType.TextSet or MailRuleExpressionType.Boolean)
        {
            return this.Refuse(
                $"passes {MailRuleExpressionSurface.Describe(value)} to '{MailRuleExpressionSurface.In}', which compares text, a number, or a timestamp. Use 'contains' for a text set.");
        }

        var mismatch = arguments
            .Skip(1)
            .Where(argument => argument != value)
            .Select(argument => (MailRuleExpressionType?)argument)
            .FirstOrDefault();

        return mismatch is { } wrongType
            ? this.Refuse(
                $"passes {MailRuleExpressionSurface.Describe(wrongType)} to '{MailRuleExpressionSurface.In}' while comparing {MailRuleExpressionSurface.Describe(value)}, so that value could never match.")
            : MailRuleExpressionType.Boolean;
    }

    private MailRuleExpressionType CheckContains(IReadOnlyList<MailRuleExpressionType> arguments)
    {
        if (!this.CheckArity(MailRuleExpressionSurface.Contains, arguments, 2))
        {
            return MailRuleExpressionType.Invalid;
        }

        if (arguments[0] is not (MailRuleExpressionType.Text or MailRuleExpressionType.TextSet))
        {
            return this.Refuse(
                $"passes {MailRuleExpressionSurface.Describe(arguments[0])} as the first argument of '{MailRuleExpressionSurface.Contains}', which searches text or a text set.");
        }

        return arguments[1] is MailRuleExpressionType.Text
            ? MailRuleExpressionType.Boolean
            : this.Refuse(
                $"passes {MailRuleExpressionSurface.Describe(arguments[1])} as the second argument of '{MailRuleExpressionSurface.Contains}', which looks for text.");
    }

    private MailRuleExpressionType CheckTextArguments(
        string name,
        IReadOnlyList<MailRuleExpressionType> arguments,
        int expectedCount)
    {
        if (!this.CheckArity(name, arguments, expectedCount))
        {
            return MailRuleExpressionType.Invalid;
        }

        var wrongArgument = arguments
            .Where(argument => argument is not MailRuleExpressionType.Text)
            .Select(argument => (MailRuleExpressionType?)argument)
            .FirstOrDefault();

        return wrongArgument is { } wrongType
            ? this.Refuse(
                $"passes {MailRuleExpressionSurface.Describe(wrongType)} to '{name}', which takes text.")
            : MailRuleExpressionType.Boolean;
    }

    private MailRuleExpressionType CheckChoice(
        string written,
        MailRuleExpressionType condition,
        MailRuleExpressionType whenTrue,
        MailRuleExpressionType whenFalse)
    {
        if (condition is MailRuleExpressionType.Invalid
            || whenTrue is MailRuleExpressionType.Invalid
            || whenFalse is MailRuleExpressionType.Invalid)
        {
            return MailRuleExpressionType.Invalid;
        }

        if (condition is not MailRuleExpressionType.Boolean)
        {
            return this.RefuseOperand(written, MailRuleExpressionType.Boolean, condition);
        }

        return whenTrue == whenFalse
            ? whenTrue
            : this.Refuse(
                $"chooses with {written} between {MailRuleExpressionSurface.Describe(whenTrue)} and {MailRuleExpressionSurface.Describe(whenFalse)}, so what it produces would depend on the email.");
    }

    private MailRuleExpressionType CheckEquality(
        string written,
        MailRuleExpressionType left,
        MailRuleExpressionType right)
    {
        if (left is MailRuleExpressionType.TextSet || right is MailRuleExpressionType.TextSet)
        {
            return this.Refuse(
                $"compares a text set with '{written}'. A set is tested with 'contains' rather than compared as a whole.");
        }

        return left == right
            ? MailRuleExpressionType.Boolean
            : this.RefuseComparison(written, left, right);
    }

    private MailRuleExpressionType CheckOrdering(
        string written,
        MailRuleExpressionType left,
        MailRuleExpressionType right)
    {
        if (left != right)
        {
            return this.RefuseComparison(written, left, right);
        }

        return left is MailRuleExpressionType.Number or MailRuleExpressionType.Timestamp
            ? MailRuleExpressionType.Boolean
            : this.Refuse(
                $"orders {MailRuleExpressionSurface.Describe(left)} with '{written}', which compares numbers and timestamps.");
    }

    private MailRuleExpressionType RequireBoth(
        string written,
        MailRuleExpressionType required,
        MailRuleExpressionType left,
        MailRuleExpressionType right,
        MailRuleExpressionType result)
    {
        if (left != required)
        {
            return this.RefuseOperand(written, required, left);
        }

        return right == required
            ? result
            : this.RefuseOperand(written, required, right);
    }

    private bool CheckArity(string name, IReadOnlyList<MailRuleExpressionType> arguments, int expectedCount)
    {
        if (arguments.Count == expectedCount)
        {
            return true;
        }

        this.Refuse($"calls '{name}' with {arguments.Count} argument(s), and it takes {expectedCount}.");

        return false;
    }

    private bool IsTooDeep()
    {
        if (this.depth <= this.maxNestingDepth)
        {
            return false;
        }

        if (!this.depthAlreadyReported)
        {
            this.depthAlreadyReported = true;

            this.Refuse($"nests more than {this.maxNestingDepth} levels deep.");
        }

        return true;
    }

    private MailRuleExpressionType RefuseComparison(
        string written,
        MailRuleExpressionType left,
        MailRuleExpressionType right) =>
        this.Refuse(
            $"compares {MailRuleExpressionSurface.Describe(left)} with {MailRuleExpressionSurface.Describe(right)} using '{written}', which could never hold.");

    private MailRuleExpressionType RefuseOperand(
        string written,
        MailRuleExpressionType required,
        MailRuleExpressionType actual) =>
        this.Refuse(
            $"gives {MailRuleExpressionSurface.Describe(actual)} to '{written}', which takes {MailRuleExpressionSurface.Describe(required)}.");

    private MailRuleExpressionType Refuse(string detail)
    {
        this.errors.Add($"{MailRuleConditionMessage.For(this.ruleName)} {detail}");

        return MailRuleExpressionType.Invalid;
    }
}
