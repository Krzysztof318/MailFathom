// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Application.SensitiveContent;

/// <summary>What this deployment scans for, and what one scan may spend doing it.</summary>
/// <remarks>
/// <para>
/// A plan exists only where at least one switch is on. A deployment that switched both off composes none, registers
/// none, and constructs nothing behind one, which is what makes an opt-in nobody took cost nothing on any path.
/// </para>
/// <para>
/// It is composed once from validated configuration, so everything below it — the scanners, the redaction, the
/// placeholders — reads decided values rather than repeating the resolution of defaults, spellings, and suppressions
/// that produced them.
/// </para>
/// </remarks>
public sealed record SensitiveContentPlan
{
    private SensitiveContentPlan(
        SensitiveContentScanBounds bounds,
        IReadOnlyList<SensitiveContentScannerPlan> scanners)
    {
        this.Bounds = bounds;
        this.Scanners = scanners;
    }

    /// <summary>Gets what one scan may spend.</summary>
    public SensitiveContentScanBounds Bounds { get; }

    /// <summary>Gets the plan of every switched-on scanner, ordered by scanner.</summary>
    public IReadOnlyList<SensitiveContentScannerPlan> Scanners { get; }

    /// <summary>Composes the plan of a deployment with at least one scanner switched on.</summary>
    /// <param name="bounds">What one scan may spend.</param>
    /// <param name="scanners">The plan of every switched-on scanner.</param>
    /// <returns>The validated plan.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when no scanner is switched on, or one is planned twice.</exception>
    /// <remarks>
    /// Scanners are ordered by <see cref="SensitiveContentScannerKind" /> rather than by the order they were configured
    /// or registered in, because that order decides which category names an overlapping redaction, and a placeholder
    /// that depended on registration order would not be reproducible.
    /// </remarks>
    public static SensitiveContentPlan Create(
        SensitiveContentScanBounds bounds,
        IReadOnlyList<SensitiveContentScannerPlan> scanners)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(scanners);

        if (scanners.Count == 0)
        {
            throw new ArgumentException(
                "A sensitive-content plan exists only where a scanner is switched on. A deployment that switched both off composes none.",
                nameof(scanners));
        }

        var duplicated = scanners
            .GroupBy(scanner => scanner.Scanner)
            .FirstOrDefault(sameScanner => sameScanner.Count() > 1);

        if (duplicated is not null)
        {
            throw new ArgumentException(
                $"The {duplicated.Key} scanner is planned more than once.",
                nameof(scanners));
        }

        return new SensitiveContentPlan(bounds, [.. scanners.OrderBy(scanner => scanner.Scanner)]);
    }

    /// <summary>Finds the plan of one scanner.</summary>
    /// <param name="scanner">The scanner to look up.</param>
    /// <param name="plan">The plan when that scanner is switched on; otherwise <see langword="null" />.</param>
    /// <returns><see langword="true" /> when the scanner is switched on; otherwise <see langword="false" />.</returns>
    public bool TryGetScanner(
        SensitiveContentScannerKind scanner,
        [NotNullWhen(true)] out SensitiveContentScannerPlan? plan)
    {
        plan = this.Scanners.FirstOrDefault(candidate => candidate.Scanner == scanner);

        return plan is not null;
    }
}
