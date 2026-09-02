// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Derivation;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.SensitiveContent.Redaction;
using MailFathom.Domain.Access;

namespace MailFathom.TestSupport;

/// <summary>A deployment with one scanner switched on, as an egress point's consumer meets it.</summary>
/// <remarks>
/// <para>
/// Every boundary that hands text to somebody else asserts the same two things about a switched-on scanner — that a
/// detected value is replaced before the text leaves, and that a detector which cannot answer refuses the operation —
/// so both are built here rather than assembled per suite out of a plan, a redactor, and a scanner. The screen beside
/// the guard is the same deployment answering the other question, which is whether an act may happen at all.
/// </para>
/// <para>
/// Every owner reads the same posture, because what these suites are about is what a boundary does with a finding
/// rather than whose mail was scanned. A suite about the difference between two owners states two postures itself,
/// through <see cref="FixedSensitiveContentPostures" />.
/// </para>
/// <para>
/// It holds the permits the redaction runs under, which is what makes it disposable: they are the process's budget of
/// scans running at once, and a suite that left one per test behind would be leaking the thing the bound exists to
/// hold.
/// </para>
/// </remarks>
internal sealed class ScanningSensitiveContentEgress : IDisposable
{
    private readonly SensitiveContentScanConcurrency concurrency;

    private ScanningSensitiveContentEgress(
        MarkerSensitiveContentScanner scanner,
        SensitiveContentScanBounds bounds,
        TimeProvider timeProvider)
    {
        var plan = SensitiveContentPlan.Create(
            bounds,
            [
                SensitiveContentScannerPlan.Create(
                    scanner.Scanner,
                    [MarkerSensitiveContentScanner.Category],
                    []),
            ]);

        this.concurrency = new SensitiveContentScanConcurrency(bounds.MaximumConcurrentScans);
        this.Scanner = scanner;
        this.Telemetry = new RecordingSensitiveContentEgressTelemetry();
        this.Postures = FixedSensitiveContentPostures.ForEveryOwner(
            SensitiveContentPosture.Scanning(
                [scanner.Scanner],
                new SensitiveContentRedactor(plan, [scanner], timeProvider, this.concurrency),
                SensitiveContentScreeningPolicy.Create(plan, [scanner.Scanner]),
                SensitiveContentDerivationStamp.Compute(plan, [scanner])));
        this.Guard = new SensitiveContentEgressGuard(this.Postures, this.Telemetry, timeProvider);
        this.Screen = new SensitiveContentEgressScreen(this.Postures, this.Telemetry, timeProvider);
    }

    /// <summary>Gets the owner whose mail this deployment is exercised over.</summary>
    public static MailOwnerId Owner => SyntheticMailOwner.Deployment;

    /// <summary>Gets what every owner's mail is scanned under, for a consumer that resolves the owner itself.</summary>
    public FixedSensitiveContentPostures Postures { get; }

    /// <summary>Gets the guard a consumer is handed.</summary>
    public SensitiveContentEgressGuard Guard { get; }

    /// <summary>Gets the screen a consumer is handed, which refuses an act rather than redacting a result.</summary>
    /// <remarks>
    /// It stops on the same scanner the guard redacts through, so a test never has to state twice which switch this
    /// deployment has on. A consumer wanting a deployment that detects the marker and lets it leave anyway builds the
    /// screen itself, which is a case of its own and belongs where it is asserted.
    /// </remarks>
    public SensitiveContentEgressScreen Screen { get; }

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

    /// <summary>States that what follows is that owner's mail, as a use case does before it reads any.</summary>
    /// <returns>The scope, which the test disposes.</returns>
    /// <remarks>
    /// Needed by a test that exercises the guard directly rather than through a use case. Everything reached inside a
    /// use case is already acting for the owner it resolved, so a suite going through one never opens this.
    /// </remarks>
    public IDisposable ActingForOwner() => this.Guard.ActingFor(Owner);

    /// <inheritdoc />
    public void Dispose() => this.concurrency.Dispose();
}
