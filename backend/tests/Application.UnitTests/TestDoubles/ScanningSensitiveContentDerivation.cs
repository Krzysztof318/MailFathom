// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Derivation;
using MailFathom.Application.SensitiveContent.Redaction;
using MailFathom.TestSupport;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>A deployment with one scanner switched on, as a derived write meets it.</summary>
/// <remarks>
/// It holds the redactor the guard runs through, which is what makes it disposable: the redactor owns the concurrency
/// permits of a whole deployment, and a suite that left one per test behind would be leaking the thing the bound exists
/// to hold.
/// </remarks>
internal sealed class ScanningSensitiveContentDerivation : IDisposable
{
    private readonly SensitiveContentRedactor redactor;

    private ScanningSensitiveContentDerivation(
        MarkerSensitiveContentScanner scanner,
        IReadOnlyList<SensitiveContentCategory> categories,
        TimeProvider timeProvider)
    {
        var plan = SensitiveContentPlan.Create(
            SensitiveContentScanBounds.Default,
            [SensitiveContentScannerPlan.Create(scanner.Scanner, categories, [])]);

        this.Scanner = scanner;
        this.Telemetry = new RecordingSensitiveContentDerivationTelemetry();
        this.redactor = new SensitiveContentRedactor(plan, [scanner], timeProvider);
        this.Guard = new SensitiveContentDerivationGuard(
            this.redactor,
            SensitiveContentDerivationStamp.Compute(plan, [scanner]),
            this.Telemetry,
            timeProvider);
    }

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

    /// <summary>Builds the guard of a deployment with both switches off.</summary>
    /// <returns>A guard that returns every text it is handed, constructs no detector, and stamps nothing.</returns>
    public static SensitiveContentDerivationGuard Inactive() => new(
        redactor: null,
        stamp: null,
        new RecordingSensitiveContentDerivationTelemetry(),
        TimeProvider.System);

    /// <inheritdoc />
    public void Dispose() => this.redactor.Dispose();
}
