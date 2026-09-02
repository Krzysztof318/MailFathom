// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Spam;

namespace MailFathom.Application.Spam.Scanning;

/// <summary>What a scanner answered about one message, bounded in every direction.</summary>
/// <remarks>
/// <para>
/// Nothing scanner-specific reaches this type: no socket, no protocol reply, no vocabulary of a particular corpus. What
/// crosses the port is a score against a threshold, the names of the rules that fired, and the identity of the corpus
/// they came from — which is what a provenance record needs and the smallest thing a second implementation would have
/// to be able to produce.
/// </para>
/// <para>
/// A rule name is the scanner's own identifier and carries nothing from the message, so unlike a signal's observation it
/// is safe to report in telemetry.
/// </para>
/// </remarks>
public sealed record SpamScanResult
{
    /// <summary>The greatest number of fired rule names one answer carries.</summary>
    /// <remarks>
    /// A corpus can fire dozens of rules on one message and a hostile message can be built to fire many. The bound is
    /// generous against a real answer and keeps one message's derived data bounded whatever the scanner returns.
    /// </remarks>
    public const int MaximumFiredRules = 48;

    private SpamScanResult(
        SpamScanOutcome outcome,
        SpamAssessment? assessment,
        IReadOnlyList<string> firedRules,
        string? corpusRevision)
    {
        this.Outcome = outcome;
        this.Assessment = assessment;
        this.FiredRules = firedRules;
        this.CorpusRevision = corpusRevision;
    }

    /// <summary>Gets how the scan ended.</summary>
    public SpamScanOutcome Outcome { get; }

    /// <summary>Gets the score and the threshold, present exactly when the outcome is <see cref="SpamScanOutcome.Scored" />.</summary>
    public SpamAssessment? Assessment { get; }

    /// <summary>Gets the names of the rules that fired, empty when none did or when nothing was scanned.</summary>
    public IReadOnlyList<string> FiredRules { get; }

    /// <summary>Gets the corpus the rules came from, present exactly when the outcome is <see cref="SpamScanOutcome.Scored" />.</summary>
    public string? CorpusRevision { get; }

    /// <summary>Records what a scanner answered.</summary>
    /// <param name="assessment">The score and the threshold it was judged against.</param>
    /// <param name="firedRules">The names of the rules that fired.</param>
    /// <param name="corpusRevision">The corpus the rules came from.</param>
    /// <returns>The result, holding no more than <see cref="MaximumFiredRules" /> rule names.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="assessment" /> or <paramref name="firedRules" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="corpusRevision" /> is blank.</exception>
    public static SpamScanResult Scored(
        SpamAssessment assessment,
        IReadOnlyList<string> firedRules,
        string corpusRevision)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentNullException.ThrowIfNull(firedRules);
        ArgumentException.ThrowIfNullOrWhiteSpace(corpusRevision);

        return new SpamScanResult(
            SpamScanOutcome.Scored,
            assessment,
            [.. firedRules.Where(static rule => !string.IsNullOrWhiteSpace(rule)).Take(MaximumFiredRules)],
            corpusRevision);
    }

    /// <summary>Records that the scanner did not answer usably.</summary>
    /// <returns>The result.</returns>
    public static SpamScanResult Unavailable() =>
        new(SpamScanOutcome.Unavailable, assessment: null, [], corpusRevision: null);

    /// <summary>Records that the message was too large for the scanner to be sent it.</summary>
    /// <returns>The result.</returns>
    public static SpamScanResult ContentTooLarge() =>
        new(SpamScanOutcome.ContentTooLarge, assessment: null, [], corpusRevision: null);
}
