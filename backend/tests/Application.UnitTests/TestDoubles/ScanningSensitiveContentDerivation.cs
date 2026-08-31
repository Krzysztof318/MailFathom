// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Derivation;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.SensitiveContent.Redaction;
using MailFathom.Domain.Access;
using MailFathom.TestSupport;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>A deployment with one scanner switched on, as a derived write meets it.</summary>
/// <remarks>
/// <para>
/// Every owner reads the same posture, because what most of these suites are about is what a derived write does with a
/// finding rather than whose mail was scanned. <see cref="FindingForOneOwner" /> is the exception, for the one claim
/// that is about the owner: that a consumer scans under the owner it was handed rather than under one of its own.
/// </para>
/// <para>
/// It holds the permits the redaction runs under, which is what makes it disposable: they are the process's budget of
/// scans running at once, and a suite that left one per test behind would be leaking the thing the bound exists to
/// hold.
/// </para>
/// </remarks>
internal sealed class ScanningSensitiveContentDerivation : IDisposable
{
    private readonly SensitiveContentScanConcurrency concurrency;

    private ScanningSensitiveContentDerivation(
        MarkerSensitiveContentScanner scanner,
        IReadOnlyList<SensitiveContentCategory> categories,
        TimeProvider timeProvider,
        MailOwnerId scannedOwner = default)
    {
        var plan = SensitiveContentPlan.Create(
            SensitiveContentScanBounds.Default,
            [SensitiveContentScannerPlan.Create(scanner.Scanner, categories, [])]);

        this.concurrency = new SensitiveContentScanConcurrency(plan.Bounds.MaximumConcurrentScans);
        this.Scanner = scanner;
        this.Telemetry = new RecordingSensitiveContentDerivationTelemetry();

        var scanning = SensitiveContentPosture.Scanning(
            [scanner.Scanner],
            new SensitiveContentRedactor(plan, [scanner], timeProvider, this.concurrency),
            SensitiveContentScreeningPolicy.ScreeningNothing(),
            SensitiveContentDerivationStamp.Compute(plan, [scanner]));

        this.Postures = scannedOwner.IsSpecified
            ? FixedSensitiveContentPostures.Of(SensitiveContentPosture.ScanningNothing, (scannedOwner, scanning))
            : FixedSensitiveContentPostures.ForEveryOwner(scanning);
        this.Guard = new SensitiveContentDerivationGuard(this.Postures, this.Telemetry, timeProvider);
    }

    /// <summary>Gets the owner whose mail this deployment is exercised over.</summary>
    public static MailOwnerId Owner => SyntheticMailOwner.Deployment;

    /// <summary>Gets what every owner's mail is scanned under, for a consumer that resolves the owner itself.</summary>
    public FixedSensitiveContentPostures Postures { get; }

    /// <summary>Gets the guard a derived write is handed.</summary>
    public SensitiveContentDerivationGuard Guard { get; }

    /// <summary>Gets what the guard reported, so a test can read what was redacted and what was refused.</summary>
    public RecordingSensitiveContentDerivationTelemetry Telemetry { get; }

    /// <summary>Gets the detector behind the guard, which records every text it was handed.</summary>
    public MarkerSensitiveContentScanner Scanner { get; }

    /// <summary>Builds a deployment whose scanner reports one literal wherever it occurs.</summary>
    /// <param name="marker">The literal a finding covers, which the placeholder replaces.</param>
    /// <param name="timeProvider">Times the per-call budget and stamps the findings.</param>
    /// <param name="categories">The categories the plan names, defaulting to the one the marker scanner reports.</param>
    /// <returns>The deployment, which the test disposes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider" /> is <see langword="null" />.</exception>
    public static ScanningSensitiveContentDerivation Finding(
        string marker,
        TimeProvider timeProvider,
        IReadOnlyList<SensitiveContentCategory>? categories = null)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        return new ScanningSensitiveContentDerivation(
            new MarkerSensitiveContentScanner(marker, SensitiveContentScannerKind.Secrets, timeProvider),
            categories ?? [MarkerSensitiveContentScanner.Category],
            timeProvider);
    }

    /// <summary>Builds a deployment where one owner asked for scanning and nobody else did.</summary>
    /// <param name="scannedOwner">The owner whose mail the scanner runs over; every other owner reads a posture that scans nothing.</param>
    /// <param name="marker">The literal a finding covers, which the placeholder replaces.</param>
    /// <param name="timeProvider">Times the per-call budget and stamps the findings.</param>
    /// <returns>The deployment, which the test disposes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// This is what a consumer that forwards the owner it was handed is told apart by: the same text, read for two
    /// owners, comes back redacted for one and untouched for the other, so a caller resolving the posture from anything
    /// but its own argument fails rather than passing.
    /// </remarks>
    public static ScanningSensitiveContentDerivation FindingForOneOwner(
        MailOwnerId scannedOwner,
        string marker,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        return new ScanningSensitiveContentDerivation(
            new MarkerSensitiveContentScanner(marker, SensitiveContentScannerKind.Secrets, timeProvider),
            [MarkerSensitiveContentScanner.Category],
            timeProvider,
            scannedOwner);
    }

    /// <summary>Builds a deployment whose scanner cannot say what a text carries.</summary>
    /// <param name="timeProvider">Times the per-call budget.</param>
    /// <returns>The deployment, whose guard refuses every text it is handed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider" /> is <see langword="null" />.</exception>
    public static ScanningSensitiveContentDerivation Unavailable(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        return new ScanningSensitiveContentDerivation(
            new MarkerSensitiveContentScanner("unreachable", SensitiveContentScannerKind.Secrets, timeProvider)
            {
                Failure = new InvalidOperationException("The detector is not answering."),
            },
            [MarkerSensitiveContentScanner.Category],
            timeProvider);
    }

    /// <summary>Builds the guard of a deployment nobody's mail is scanned for.</summary>
    /// <returns>A guard that returns every text it is handed, constructs no detector, and stamps nothing.</returns>
    public static SensitiveContentDerivationGuard Inactive() => new(
        FixedSensitiveContentPostures.ScanningNothing(),
        new RecordingSensitiveContentDerivationTelemetry(),
        TimeProvider.System);

    /// <inheritdoc />
    public void Dispose() => this.concurrency.Dispose();
}
