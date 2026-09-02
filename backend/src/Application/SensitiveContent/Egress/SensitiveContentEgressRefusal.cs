// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.SensitiveContent.Egress;

/// <summary>What one screened egress was stopped by, in the terms a caller may be told.</summary>
/// <remarks>
/// <para>
/// It is the whole of what leaves <see cref="SensitiveContentEgressScreen" />. A finding does not: it carries a rule
/// name, a position, and a confidence, and a consumer holding one is a consumer that could write the position of a
/// credential into a refusal message, a log line, or an audit record. What crosses out instead is the scanner and the
/// category, which are MailFathom's own closed sets and are already what the instruments beside the screen record.
/// </para>
/// <para>
/// Both values are absent on a ceiling refusal, because nothing was found: the text simply ran past what one scan
/// analyzes. That is why they are nullable rather than defaulted to the first scanner — a refusal reporting
/// <c>Secrets</c> for a message no scanner had an opinion about would be read as a credential having been detected.
/// </para>
/// </remarks>
public sealed record SensitiveContentEgressRefusal
{
    private SensitiveContentEgressRefusal(
        SensitiveContentEgressRefusalReason reason,
        SensitiveContentScannerKind? scanner,
        SensitiveContentCategory? category)
    {
        this.Reason = reason;
        this.Scanner = scanner;
        this.Category = category;
    }

    /// <summary>Gets why the egress was stopped.</summary>
    public SensitiveContentEgressRefusalReason Reason { get; }

    /// <summary>Gets the scanner that found the material, or <see langword="null" /> where nothing was found.</summary>
    public SensitiveContentScannerKind? Scanner { get; }

    /// <summary>Gets the kind of material found, or <see langword="null" /> where nothing was found.</summary>
    public SensitiveContentCategory? Category { get; }

    /// <summary>Reports a text carrying material of a category this egress point is screened for.</summary>
    /// <param name="scanner">The scanner whose category matched.</param>
    /// <param name="category">The kind of material found.</param>
    /// <returns>The refusal to report.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="category" /> is <see langword="null" />.</exception>
    public static SensitiveContentEgressRefusal ContentFound(
        SensitiveContentScannerKind scanner,
        SensitiveContentCategory category)
    {
        ArgumentNullException.ThrowIfNull(category);

        return new SensitiveContentEgressRefusal(
            SensitiveContentEgressRefusalReason.ContentFound,
            scanner,
            category);
    }

    /// <summary>Reports a text whose remainder was never analyzed, so nothing established what it carries.</summary>
    /// <returns>The refusal to report.</returns>
    public static SensitiveContentEgressRefusal NotFullyScanned() => new(
        SensitiveContentEgressRefusalReason.TextExceededScanCeiling,
        scanner: null,
        category: null);
}
