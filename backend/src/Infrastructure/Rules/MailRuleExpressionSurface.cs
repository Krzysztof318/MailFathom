// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Frozen;
using NCalc;

namespace MailFathom.Infrastructure.Rules;

/// <summary>Declares everything a condition may write, which is the whole of the environment an expression can reach.</summary>
/// <remarks>
/// <para>
/// The expression language ships a larger surface than a mail rule needs — a mathematical library, bitwise and shift
/// operators, a factorial, and a SQL-style pattern match among them — and offers no switch that removes any of it. The
/// closure is therefore enforced where it can be: an expression naming anything absent from these three sets is refused
/// when the configuration is read, so nothing outside them ever reaches an evaluation.
/// </para>
/// <para>
/// Two of the omissions are about cost rather than tidiness, and they are the reason this is a list of what is kept
/// rather than a list of what is removed. The factorial operator turns a short expression into arbitrarily large
/// arithmetic, and the pattern-matching operators take an authored pattern and match it against mail content. Neither
/// has a place in a file that is read at startup and run against attacker-controlled input, and neither can come back
/// by accident from a set that has to name what it admits.
/// </para>
/// </remarks>
internal static class MailRuleExpressionSurface
{
    /// <summary>The name of the function that chooses between two values.</summary>
    public const string If = "if";

    /// <summary>The name of the function that tests membership of a listed set.</summary>
    public const string In = "in";

    /// <summary>The name of the function that tests whether a value is absent.</summary>
    public const string IsNull = "isNull";

    /// <summary>The name of the function that tests whether text is absent or empty.</summary>
    public const string IsNullOrEmpty = "isNullOrEmpty";

    /// <summary>The name of the function that tests whether text carries a term, or a text set carries a member.</summary>
    public const string Contains = "contains";

    /// <summary>The name of the function that tests whether text begins with another.</summary>
    public const string StartsWith = "startsWith";

    /// <summary>The name of the function that tests whether text ends with another.</summary>
    public const string EndsWith = "endsWith";

    /// <summary>Gets every function a condition may call, in the order the documentation presents them.</summary>
    public static IReadOnlyList<string> FunctionNames { get; } =
    [
        If,
        In,
        IsNull,
        IsNullOrEmpty,
        Contains,
        StartsWith,
        EndsWith,
    ];

    /// <summary>Gets the functions MailFathom implements itself, which are the ones registered into an evaluation.</summary>
    /// <remarks>
    /// The other four are the language's own and need no registration. These four are registered as asynchronous
    /// functions even though none of them performs I/O, because an argument may be a fact whose resolution does, and a
    /// synchronous function cannot evaluate an argument that resolves asynchronously.
    /// </remarks>
    public static IReadOnlyList<string> RegisteredFunctionNames { get; } =
    [
        Contains,
        StartsWith,
        EndsWith,
    ];

    /// <summary>Gets every binary operator a condition may use, mapped to the form the documentation writes it in.</summary>
    public static FrozenDictionary<BinaryExpressionType, string> BinaryOperators { get; } =
        new Dictionary<BinaryExpressionType, string>
        {
            [BinaryExpressionType.And] = "and",
            [BinaryExpressionType.Or] = "or",
            [BinaryExpressionType.Equal] = "==",
            [BinaryExpressionType.NotEqual] = "!=",
            [BinaryExpressionType.Lesser] = "<",
            [BinaryExpressionType.LesserOrEqual] = "<=",
            [BinaryExpressionType.Greater] = ">",
            [BinaryExpressionType.GreaterOrEqual] = ">=",
            [BinaryExpressionType.In] = "in",
            [BinaryExpressionType.NotIn] = "not in",
            [BinaryExpressionType.Plus] = "+",
            [BinaryExpressionType.Minus] = "-",
            [BinaryExpressionType.Times] = "*",
            [BinaryExpressionType.Div] = "/",
            [BinaryExpressionType.Modulo] = "%",
        }.ToFrozenDictionary();

    /// <summary>Gets every unary operator a condition may use, mapped to the form the documentation writes it in.</summary>
    public static FrozenDictionary<UnaryExpressionType, string> UnaryOperators { get; } =
        new Dictionary<UnaryExpressionType, string>
        {
            [UnaryExpressionType.Not] = "not",
            [UnaryExpressionType.Negate] = "-",
            [UnaryExpressionType.Positive] = "+",
        }.ToFrozenDictionary();

    /// <summary>Names an operator the way an operator writes it, so a refusal quotes what they typed.</summary>
    /// <param name="type">The parsed operator.</param>
    /// <returns>The written form, or the parser's own name for an operator this surface does not admit.</returns>
    public static string Describe(BinaryExpressionType type) =>
        BinaryOperators.TryGetValue(type, out var written) ? written : type.ToString();

    /// <summary>Names an operator the way an operator writes it, so a refusal quotes what they typed.</summary>
    /// <param name="type">The parsed operator.</param>
    /// <returns>The written form, or the parser's own name for an operator this surface does not admit.</returns>
    public static string Describe(UnaryExpressionType type) =>
        UnaryOperators.TryGetValue(type, out var written) ? written : type.ToString();

    /// <summary>Names a type the way the documentation names it, so a refusal reads in the same vocabulary.</summary>
    /// <param name="type">The type the walk worked out.</param>
    /// <returns>The written name.</returns>
    public static string Describe(MailRuleExpressionType type) => type switch
    {
        MailRuleExpressionType.Text => "text",
        MailRuleExpressionType.TextSet => "a text set",
        MailRuleExpressionType.Number => "a number",
        MailRuleExpressionType.Boolean => "a boolean",
        MailRuleExpressionType.Timestamp => "a timestamp",
        MailRuleExpressionType.ArgumentList => "a list of values",
        _ => "an unusable value",
    };
}
