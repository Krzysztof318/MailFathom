// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;

namespace MailFathom.TestSupport;

/// <summary>A detector that finds one literal, so a test can put a "credential" in mail and watch where it stops.</summary>
/// <remarks>
/// <para>
/// Shared because every boundary holding a guarded egress point asserts the same three things about it — that a
/// detected value is replaced before the text leaves, that a detector which cannot answer refuses the operation, and
/// that a deployment scanning nothing is untouched — and a scanner written per suite would let those three drift into
/// three slightly different claims.
/// </para>
/// <para>
/// It finds a literal rather than a pattern deliberately. What these tests are about is where a guard sits, so the
/// detection has to be uninteresting: a test asserting an egress point must not be able to fail because a regular
/// expression did or did not match.
/// </para>
/// </remarks>
internal sealed class MarkerSensitiveContentScanner : ISensitiveContentScanner
{
    /// <summary>The category every finding this scanner reports belongs to, and therefore the placeholder it produces.</summary>
    internal static readonly SensitiveContentCategory Category = SensitiveContentCategory.Create("CloudKey");

    private static readonly SensitiveContentRule Rule = SensitiveContentRule.Create(Category, "marker");

    private readonly string marker;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a scanner that reports the given literal wherever it occurs.</summary>
    /// <param name="marker">The literal a finding covers.</param>
    /// <param name="scanner">Which of the two switches this scanner answers for.</param>
    /// <param name="timeProvider">Stamps when a finding was evaluated.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="marker" /> is empty.</exception>
    internal MarkerSensitiveContentScanner(
        string marker,
        SensitiveContentScannerKind scanner,
        TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrEmpty(marker);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.marker = marker;
        this.Scanner = scanner;
        this.timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public SensitiveContentScannerKind Scanner { get; }

    /// <inheritdoc />
    public SensitiveContentDetector Detector { get; } = SensitiveContentDetector.Create("marker", "2026.08.12");

    /// <summary>Gets or sets the failure raised instead of findings, or <see langword="null" /> to answer.</summary>
    /// <remarks>Set to make the scanner the one thing a fail-closed test needs: a detector that cannot say what a text carries.</remarks>
    public Exception? Failure { get; set; }

    /// <summary>Gets or sets what happens while one scan is running, or <see langword="null" /> to answer immediately.</summary>
    /// <remarks>
    /// Set it to advance a fake clock, which is the only way a test can state what a scan cost: everything a caller
    /// measures around this scanner is measured across this call, so a scan that takes no time at all makes every such
    /// assertion hold whatever the caller timed.
    /// </remarks>
    public Action? WhileScanning { get; set; }

    /// <summary>Gets the texts this scanner was handed, in order.</summary>
    public IReadOnlyList<string> ScannedTexts => this.Scanned;

    private List<string> Scanned { get; } = [];

    /// <inheritdoc />
    public Task<IReadOnlyList<SensitiveContentFinding>> ScanAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();

        this.Scanned.Add(text);
        this.WhileScanning?.Invoke();

        if (this.Failure is { } failure)
        {
            throw failure;
        }

        var findings = new List<SensitiveContentFinding>();
        var detectedAt = this.timeProvider.GetUtcNow();

        for (var at = text.IndexOf(this.marker, StringComparison.Ordinal);
            at >= 0;
            at = text.IndexOf(this.marker, at + this.marker.Length, StringComparison.Ordinal))
        {
            findings.Add(SensitiveContentFinding.Create(
                Rule,
                SensitiveContentSpan.Create(at, this.marker.Length),
                confidence: 1,
                this.Detector,
                detectedAt));
        }

        return Task.FromResult<IReadOnlyList<SensitiveContentFinding>>(findings);
    }
}
