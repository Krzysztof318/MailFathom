// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.SensitiveContent.Detection;

/// <summary>One region of analyzed text a scanner reported as sensitive.</summary>
/// <remarks>
/// <para>
/// <b>A finding never carries the detected value.</b> It names the rule that matched and the kind of material that
/// rule looks for, where it sits, how sure the detector was, and what produced it — everything a consumer needs in
/// order to redact, to count, and to attribute, and nothing that would recreate the leak in a log line, an audit
/// record, or a stored attribution.
/// </para>
/// <para>
/// The detector and its revision travel with the finding rather than being read from the deployment, because redaction
/// is only reproducible against a stated corpus: the same text under a newer rule set is a different result, and a
/// consumer that stored one has to be able to say which one it stored.
/// </para>
/// </remarks>
public sealed record SensitiveContentFinding
{
    private SensitiveContentFinding(
        SensitiveContentRule rule,
        SensitiveContentSpan span,
        double confidence,
        SensitiveContentDetector detector,
        DateTimeOffset detectedAt)
    {
        this.Rule = rule;
        this.Span = span;
        this.Confidence = confidence;
        this.Detector = detector;
        this.DetectedAt = detectedAt;
    }

    /// <summary>Gets the corpus entry that matched, which is the name a suppression would silence.</summary>
    /// <remarks>
    /// A rule rather than only a category, because an operator meeting a false positive has to be able to switch off the
    /// one entry that produced it, and the finding is the only place that names it. It stays safe to record for the same
    /// reason the detector is: a rule name is the corpus's own name for a pattern, never any part of what the pattern
    /// matched.
    /// </remarks>
    public SensitiveContentRule Rule { get; }

    /// <summary>Gets the kind of sensitive material found, which is what the placeholder replacing it names.</summary>
    public SensitiveContentCategory Category => this.Rule.Category;

    /// <summary>Gets the region of the analyzed text the finding covers.</summary>
    public SensitiveContentSpan Span { get; }

    /// <summary>Gets how sure the detector was, from 0 to 1 inclusive.</summary>
    /// <remarks>
    /// A pattern that identifies its own format reports 1, which is what the secret scanner's matches are; an analyzer
    /// that scores a candidate reports what it scored. Redaction does not read this today — a reported finding is
    /// redacted whatever its confidence — and it is carried so a consumer counting or auditing findings can say how the
    /// detector reached them.
    /// </remarks>
    public double Confidence { get; }

    /// <summary>Gets what produced the finding, and the corpus or profile revision it ran under.</summary>
    public SensitiveContentDetector Detector { get; }

    /// <summary>Gets when the scan that produced the finding evaluated the text.</summary>
    public DateTimeOffset DetectedAt { get; }

    /// <summary>Creates a finding.</summary>
    /// <param name="rule">The corpus entry that matched, which carries the category it belongs to.</param>
    /// <param name="span">The region of the analyzed text it covers.</param>
    /// <param name="confidence">How sure the detector was, from 0 to 1 inclusive.</param>
    /// <param name="detector">What produced the finding, with its corpus or profile revision.</param>
    /// <param name="detectedAt">When the scan evaluated the text.</param>
    /// <returns>The validated finding.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="rule" /> or <paramref name="detector" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="confidence" /> is outside 0 to 1, or <paramref name="span" /> describes no region.</exception>
    public static SensitiveContentFinding Create(
        SensitiveContentRule rule,
        SensitiveContentSpan span,
        double confidence,
        SensitiveContentDetector detector,
        DateTimeOffset detectedAt)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(detector);
        ArgumentOutOfRangeException.ThrowIfLessThan(confidence, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(confidence, 1);

        if (!span.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(
                nameof(span),
                "A finding must cover a region of the analyzed text rather than the unspecified span.");
        }

        return new SensitiveContentFinding(rule, span, confidence, detector, detectedAt);
    }
}
