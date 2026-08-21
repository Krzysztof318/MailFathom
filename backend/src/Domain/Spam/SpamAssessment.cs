// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Spam;

/// <summary>A numeric score together with the threshold it was judged against.</summary>
/// <remarks>
/// The two are one value because neither means anything alone: a score of 6 is spam under one threshold and ordinary
/// mail under another, so a record that kept only the score could not be re-read after an operator moved the threshold.
/// Not every classification reaches one — sender authentication and a folder placement are facts rather than
/// measurements — which is why the classification holds this optionally.
/// </remarks>
public sealed record SpamAssessment
{
    private SpamAssessment(double score, double threshold)
    {
        this.Score = score;
        this.Threshold = threshold;
    }

    /// <summary>Gets the score the message reached.</summary>
    public double Score { get; }

    /// <summary>Gets the score at or above which the message counts as spam.</summary>
    public double Threshold { get; }

    /// <summary>Gets whether the score cleared the threshold.</summary>
    /// <remarks>
    /// Inclusive at the threshold, which is how SpamAssassin's own <c>Spam: True ; 15.0 / 5.0</c> answer reads and what
    /// keeps a configured threshold meaning "spam from here up" rather than "spam above here".
    /// </remarks>
    public bool ClearsThreshold => this.Score >= this.Threshold;

    /// <summary>Records a score against the threshold it was judged by.</summary>
    /// <param name="score">The score reached.</param>
    /// <param name="threshold">The score at or above which the message counts as spam.</param>
    /// <returns>The assessment.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when either value is not a finite number.</exception>
    /// <remarks>
    /// Both are refused unless finite. A scanner answering with a NaN would otherwise produce a record whose comparison
    /// against the threshold is false whichever way it is written, which reads as clean mail rather than as a scanner
    /// that failed.
    /// </remarks>
    public static SpamAssessment Create(double score, double threshold)
    {
        if (!double.IsFinite(score))
        {
            throw new ArgumentOutOfRangeException(nameof(score), score, "A spam score is a finite number.");
        }

        if (!double.IsFinite(threshold))
        {
            throw new ArgumentOutOfRangeException(nameof(threshold), threshold, "A spam threshold is a finite number.");
        }

        return new SpamAssessment(score, threshold);
    }
}
