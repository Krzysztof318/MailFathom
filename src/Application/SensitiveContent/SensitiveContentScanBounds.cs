// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;

namespace MailFathom.Application.SensitiveContent;

/// <summary>What one scan may spend, so a single large message cannot stall the pipeline behind it.</summary>
/// <remarks>
/// The three bounds answer three different ways a scan goes wrong. A message far larger than any mail body caps what is
/// analyzed at all; a detector that stops answering caps what one call may wait; and a burst of concurrent work caps how
/// many scans run at once, which matters most for the scanner that reaches a container over the network and would
/// otherwise open a connection per caller.
/// </remarks>
public sealed record SensitiveContentScanBounds
{
    private SensitiveContentScanBounds(
        int maximumAnalyzedCharacters,
        TimeSpan scanTimeout,
        int maximumConcurrentScans)
    {
        this.MaximumAnalyzedCharacters = maximumAnalyzedCharacters;
        this.ScanTimeout = scanTimeout;
        this.MaximumConcurrentScans = maximumConcurrentScans;
    }

    /// <summary>Gets the bounds a deployment that states none receives.</summary>
    /// <remarks>
    /// The analyzed ceiling matches what one content read may return in total, so an ordinary mail body is analyzed
    /// whole and only something pathological reaches the ceiling at all.
    /// </remarks>
    public static SensitiveContentScanBounds Default { get; } = new(200_000, TimeSpan.FromSeconds(5), 4);

    /// <summary>Gets the greatest number of characters one scan analyzes.</summary>
    /// <remarks>
    /// Text beyond it is not analyzed, and therefore is not handed on either: what a redaction returns stops at this
    /// ceiling and reports how much it dropped. Emitting the remainder unanalyzed would let the one input nobody scanned
    /// be the one input that leaves.
    /// </remarks>
    public int MaximumAnalyzedCharacters { get; }

    /// <summary>Gets how long one call to one scanner may take before the operation it guards is refused.</summary>
    public TimeSpan ScanTimeout { get; }

    /// <summary>Gets how many scans may run at once across the process.</summary>
    public int MaximumConcurrentScans { get; }

    /// <summary>Creates bounds, refusing values no scan could run under.</summary>
    /// <param name="maximumAnalyzedCharacters">The greatest number of characters one scan analyzes.</param>
    /// <param name="scanTimeout">How long one call to one scanner may take.</param>
    /// <param name="maximumConcurrentScans">How many scans may run at once.</param>
    /// <returns>The validated bounds.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a value is outside the range this type accepts.</exception>
    public static SensitiveContentScanBounds Create(
        int maximumAnalyzedCharacters,
        TimeSpan scanTimeout,
        int maximumConcurrentScans)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumAnalyzedCharacters, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(scanTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumConcurrentScans, 1);

        return new SensitiveContentScanBounds(maximumAnalyzedCharacters, scanTimeout, maximumConcurrentScans);
    }

    /// <inheritdoc />
    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture,
        "at most {0} characters within {1}, {2} at a time",
        this.MaximumAnalyzedCharacters,
        this.ScanTimeout,
        this.MaximumConcurrentScans);
}
