// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Frozen;
using NCalc.Handlers;

namespace MailFathom.Infrastructure.Rules;

/// <summary>The three text functions MailFathom adds to the condition language, and nothing else.</summary>
/// <remarks>
/// <para>
/// Every one of them searches for a literal term rather than matching a pattern. That is the point: a condition is
/// authored text and a message body is attacker-controlled input, so a pattern language over the two would be a way to
/// make matching cost whatever the pattern says. A substring search costs the length of the two strings and nothing
/// else, and both are already bounded — the term by the length limit on the condition, the text by what extraction
/// stores.
/// </para>
/// <para>
/// Comparison is ordinal and ignores case, matching how the rest of a condition compares text. Ordinal rather than
/// culture-aware so that two instances of the same deployment reach the same answer whatever locale their host is set
/// to, which is half of what makes a rule set deterministic.
/// </para>
/// <para>
/// Registered as asynchronous functions although none of them awaits anything of its own. An argument may be a fact
/// whose resolution reads stored content, and a synchronous function cannot evaluate an argument that resolves
/// asynchronously.
/// </para>
/// </remarks>
internal static class MailRuleConditionFunctions
{
    /// <summary>Gets the functions to register into an evaluation, by the name a condition calls them with.</summary>
    public static FrozenDictionary<string, AsyncExpressionFunction> All { get; } =
        new Dictionary<string, AsyncExpressionFunction>(StringComparer.Ordinal)
        {
            [MailRuleExpressionSurface.Contains] = ContainsAsync,
            [MailRuleExpressionSurface.StartsWith] = StartsWithAsync,
            [MailRuleExpressionSurface.EndsWith] = EndsWithAsync,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Tests whether text carries a term, or whether a text set has a member equal to one.</summary>
    /// <remarks>
    /// The two shapes are one function because they are one question an operator asks — "is this in there" — and the
    /// difference between them is a property of the fact rather than of what was asked. An absent value carries nothing,
    /// which is answered rather than raised.
    /// </remarks>
    private static async Task<object?> ContainsAsync(FunctionData data)
    {
        var searched = await data.EvaluateAsync(0);

        if (await data.EvaluateAsync(1) is not string term)
        {
            return false;
        }

        return searched switch
        {
            string text => text.Contains(term, StringComparison.OrdinalIgnoreCase),
            IEnumerable<string> members => members.Contains(term, StringComparer.OrdinalIgnoreCase),
            _ => false,
        };
    }

    /// <summary>Tests whether text begins with another.</summary>
    private static async Task<object?> StartsWithAsync(FunctionData data) =>
        await ComparePrefixAsync(data, (text, term) => text.StartsWith(term, StringComparison.OrdinalIgnoreCase));

    /// <summary>Tests whether text ends with another.</summary>
    private static async Task<object?> EndsWithAsync(FunctionData data) =>
        await ComparePrefixAsync(data, (text, term) => text.EndsWith(term, StringComparison.OrdinalIgnoreCase));

    private static async Task<object?> ComparePrefixAsync(FunctionData data, Func<string, string, bool> compare) =>
        await data.EvaluateAsync(0) is string text && await data.EvaluateAsync(1) is string term
        && compare(text, term);
}
