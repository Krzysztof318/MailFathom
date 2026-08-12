// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Rules.Conditions;

/// <summary>States what a condition may cost: how long it may be, how deeply it may nest, and how long it may run.</summary>
/// <remarks>
/// <para>
/// The condition language has no cost model of its own, so the bound is assembled here out of three things that can be
/// checked. Two of them are checked when the configuration is read, where a refusal reaches the person who typed the
/// condition; the third is checked while it runs, because fact resolution reaches storage and no reading of the text
/// says how long that will take.
/// </para>
/// <para>
/// A length and a depth limit together are what keep the parsed shape bounded. Length alone would admit a short
/// expression nested past what any reader can follow, and depth alone would admit a flat expression of ten thousand
/// terms.
/// </para>
/// </remarks>
public sealed record MailRuleConditionBounds
{
    private MailRuleConditionBounds(int maxLength, int maxNestingDepth, TimeSpan evaluationTimeout)
    {
        this.MaxLength = maxLength;
        this.MaxNestingDepth = maxNestingDepth;
        this.EvaluationTimeout = evaluationTimeout;
    }

    /// <summary>Gets the bounds a deployment that declares none of them runs under.</summary>
    /// <remarks>
    /// A thousand characters is several times the longest condition the documented examples reach, sixteen levels is
    /// deeper than a condition a person can still read, and a second is far longer than an expression over already-loaded
    /// metadata takes — so every default is a ceiling on a mistake rather than a limit ordinary authoring meets.
    /// </remarks>
    public static MailRuleConditionBounds Default { get; } = new(1_000, 16, TimeSpan.FromSeconds(1));

    /// <summary>Gets the greatest number of characters a condition may be written in.</summary>
    public int MaxLength { get; }

    /// <summary>Gets the greatest depth the parsed condition may nest to, counting the whole expression as one level.</summary>
    public int MaxNestingDepth { get; }

    /// <summary>Gets how long one condition may take to evaluate, including resolving the facts it names.</summary>
    public TimeSpan EvaluationTimeout { get; }

    /// <summary>Creates bounds, refusing any value that would leave a condition unbounded or unwritable.</summary>
    /// <param name="maxLength">The greatest number of characters a condition may be written in.</param>
    /// <param name="maxNestingDepth">The greatest depth the parsed condition may nest to.</param>
    /// <param name="evaluationTimeout">How long one condition may take to evaluate.</param>
    /// <returns>The bounds every condition of a rule set is read and run under.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a bound is not positive.</exception>
    public static MailRuleConditionBounds Create(int maxLength, int maxNestingDepth, TimeSpan evaluationTimeout)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxNestingDepth);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(evaluationTimeout, TimeSpan.Zero);

        return new MailRuleConditionBounds(maxLength, maxNestingDepth, evaluationTimeout);
    }
}
