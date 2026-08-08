// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;

namespace MailFathom.AI.Retrieval;

/// <summary>The shape a relevance judgement is answered in, and the only reading of it.</summary>
/// <remarks>
/// <para>
/// One whole number and nothing else. The schema is this small because it is the whole of what the second pass asks:
/// a wider one would be more to get wrong on every call, and there is no second field a filter would act on.
/// </para>
/// <para>
/// The reading is strict on purpose. An answer carrying a word, a unit, a fence, an explanation, or a number outside the
/// scale is refused rather than mined for a number that might be in it, because a lenient reading turns a model that
/// answered something else into a score this system invented — and the candidate it would have decided about is
/// somebody's mail. What a refusal costs is one unjudged candidate, which the filter keeps.
/// </para>
/// </remarks>
internal static class PassageRelevanceJudgement
{
    /// <summary>The greatest number of digits a score on this scale occupies.</summary>
    private const int GreatestScoreDigits = 3;

    /// <summary>Reads what the model answered, or reports that it answered something else.</summary>
    /// <param name="answerText">The answer exactly as the provider returned it.</param>
    /// <returns>The score, or <see langword="null" /> when the answer does not match the schema.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="answerText" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Surrounding whitespace is the one liberty taken, because a provider that appends a newline has still answered the
    /// schema. Everything else — a sign, a decimal point, a separator, a leading or trailing character of any kind — is
    /// refused, which is what <see cref="NumberStyles.None" /> and the digit ceiling together say.
    /// </remarks>
    internal static int? Read(string answerText)
    {
        ArgumentNullException.ThrowIfNull(answerText);

        var answer = answerText.AsSpan().Trim();

        return answer.Length is > 0 and <= GreatestScoreDigits
            && int.TryParse(answer, NumberStyles.None, CultureInfo.InvariantCulture, out var score)
            && score <= PassageRelevanceFilterPlan.GreatestRelevance
                ? score
                : null;
    }
}
