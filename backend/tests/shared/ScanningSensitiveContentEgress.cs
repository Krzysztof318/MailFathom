// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.SensitiveContent.Redaction;

namespace MailFathom.TestSupport;

/// <summary>A deployment with one scanner switched on, as an egress point's consumer meets it.</summary>
/// <remarks>
/// <para>
/// Every boundary that hands text to somebody else asserts the same two things about a switched-on scanner — that a
/// detected value is replaced before the text leaves, and that a detector which cannot answer refuses the operation —
/// so both are built here rather than assembled per suite out of a plan, a redactor, and a scanner.
/// </para>
/// <para>
/// It holds the redactor the guard runs through, which is what makes it disposable: the redactor owns the concurrency
/// permits of a whole deployment, and a suite that left one per test behind would be leaking the thing the bound exists
/// to hold.
/// </para>
/// </remarks>
internal sealed class ScanningSensitiveContentEgress : IDisposable
{
    private readonly SensitiveContentRedactor redactor;

    private ScanningSensitiveContentEgress(
        MarkerSensitiveContentScanner scanner,
        SensitiveContentScanBounds bounds,
        TimeProvider timeProvider)
    {
        this.Scanner = scanner;
        this.Telemetry = new RecordingSensitiveContentEgressTelemetry();
        this.redactor = new SensitiveContentRedactor(
            SensitiveContentPlan.Create(
                bounds,
                [
                    SensitiveContentScannerPlan.Create(
                        scanner.Scanner,
                        [MarkerSensitiveContentScanner.Category],
                        []),
                ]),
            [scanner],
            timeProvider);
        this.Guard = new SensitiveContentEgressGuard(this.redactor, this.Telemetry, timeProvider);
    }

    /// <summary>Gets the guard a consumer is handed.</summary>
    public SensitiveContentEgressGuard Guard { get; }

    /// <summary>Gets what the guard reported, so a test can read which egress points were guarded and which refused.</summary>
    public RecordingSensitiveContentEgressTelemetry Telemetry { get; }

    /// <summary>Gets the detector behind the guard, which records every text it was handed.</summary>
    public MarkerSensitiveContentScanner Scanner { get; }

    /// <summary>Builds a deployment whose scanner reports one literal wherever it occurs.</summary>
    /// <param name="marker">The literal a finding covers, which the placeholder replaces.</param>
    /// <param name="timeProvider">Times the per-call budget and stamps the findings.</param>
    /// <param name="scanner">Which of the two switches this deployment has on, defaulting to secret detection.</param>
    /// <param name="bounds">What one scan may spend, defaulting to the bounds a deployment stating none receives.</param>
    /// <returns>The deployment, which the test disposes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The switch is a parameter because a consumer has to behave identically under either one, and the bounds are
    /// because a ceiling low enough to cut an ordinary test message is the only way to exercise the one bound in this
    /// feature that truncates instead of refusing.
    /// </remarks>
    public static ScanningSensitiveContentEgress Finding(
        string marker,
        TimeProvider timeProvider,
        SensitiveContentScannerKind scanner = SensitiveContentScannerKind.Secrets,
        SensitiveContentScanBounds? bounds = null)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        return new ScanningSensitiveContentEgress(
            new MarkerSensitiveContentScanner(marker, scanner, timeProvider),
            bounds ?? SensitiveContentScanBounds.Default,
            timeProvider);
    }

    /// <summary>Builds a deployment whose scanner cannot say what a text carries.</summary>
    /// <param name="timeProvider">Times the per-call budget.</param>
    /// <returns>The deployment, whose guard refuses every text it is handed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider" /> is <see langword="null" />.</exception>
    public static ScanningSensitiveContentEgress Unavailable(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        return new ScanningSensitiveContentEgress(
            new MarkerSensitiveContentScanner("unreachable", SensitiveContentScannerKind.Secrets, timeProvider)
            {
                Failure = new InvalidOperationException("The detector is not answering."),
            },
            SensitiveContentScanBounds.Default,
            timeProvider);
    }

    /// <inheritdoc />
    public void Dispose() => this.redactor.Dispose();
}
