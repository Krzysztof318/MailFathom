// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Spam;

namespace MailFathom.Application.Spam;

/// <summary>What one attempt to classify an occurrence produced.</summary>
/// <param name="Outcome">How the attempt ended.</param>
/// <param name="Classification">What was recorded, present exactly when the outcome is <see cref="SpamClassificationOutcome.Classified" />.</param>
/// <remarks>
/// The classification travels back with the outcome so a caller that has to act on the verdict — filing junk on the
/// server, or holding an occurrence back from embedding — does not read it again from the store it was just written to.
/// It is derived data about mail and therefore not loggable; the outcome is.
/// </remarks>
public sealed record SpamClassificationResult(
    SpamClassificationOutcome Outcome,
    SpamClassification? Classification)
{
    /// <summary>Records that nothing was classified, and why.</summary>
    /// <param name="outcome">The reason.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="outcome" /> is <see cref="SpamClassificationOutcome.Classified" />, which records a classification instead.</exception>
    public static SpamClassificationResult NotClassified(SpamClassificationOutcome outcome) =>
        outcome is SpamClassificationOutcome.Classified
            ? throw new ArgumentOutOfRangeException(
                nameof(outcome),
                outcome,
                "A result that classified nothing does not carry the classified outcome.")
            : new SpamClassificationResult(outcome, Classification: null);

    /// <summary>Records what was classified.</summary>
    /// <param name="classification">The verdict and the facts it rests on.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="classification" /> is <see langword="null" />.</exception>
    public static SpamClassificationResult Classified(SpamClassification classification)
    {
        ArgumentNullException.ThrowIfNull(classification);

        return new SpamClassificationResult(SpamClassificationOutcome.Classified, classification);
    }
}
