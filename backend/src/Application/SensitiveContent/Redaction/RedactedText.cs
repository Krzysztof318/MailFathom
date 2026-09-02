// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent.Detection;

namespace MailFathom.Application.SensitiveContent.Redaction;

/// <summary>Text a consumer may hand on, and what had to be removed from it first.</summary>
/// <remarks>
/// <para>
/// <see cref="Text" /> is the only part that leaves the process. The findings beside it describe what was removed —
/// their categories, their positions in the text that was analyzed, and what detected them — and never what was
/// removed, so a consumer can count, attribute, and audit a redaction without recreating the leak it prevented.
/// </para>
/// <para>
/// Findings are reported as the scanners produced them, which is before overlapping regions were merged. One placeholder
/// therefore need not correspond to exactly one finding: two detectors recognizing the same credential produce two
/// findings and one placeholder, which is the honest reading of both numbers.
/// </para>
/// </remarks>
public sealed record RedactedText
{
    private RedactedText(
        string text,
        IReadOnlyList<SensitiveContentFinding> findings,
        int omittedCharacterCount)
    {
        this.Text = text;
        this.Findings = findings;
        this.OmittedCharacterCount = omittedCharacterCount;
    }

    /// <summary>Gets the text with every detected region replaced by its placeholder.</summary>
    public string Text { get; }

    /// <summary>Gets every finding the scanners reported, ordered by position and, at one position, widest first.</summary>
    public IReadOnlyList<SensitiveContentFinding> Findings { get; }

    /// <summary>Gets how many characters were dropped because they lay beyond what one scan analyzes.</summary>
    /// <remarks>
    /// Dropped rather than passed through: text nothing analyzed is exactly the text that must not leave, so the
    /// ceiling truncates the result instead of admitting an unscanned remainder. A non-zero count is worth reporting to
    /// an operator, because it means the ceiling is doing something on ordinary mail rather than on a pathological
    /// message.
    /// </remarks>
    public int OmittedCharacterCount { get; }

    /// <summary>Gets whether anything was redacted at all.</summary>
    public bool IsRedacted => this.Findings.Count > 0;

    /// <summary>Records a redaction.</summary>
    /// <param name="text">The text with every detected region replaced.</param>
    /// <param name="findings">Every finding the scanners reported.</param>
    /// <param name="omittedCharacterCount">How many characters lay beyond what one scan analyzes.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="omittedCharacterCount" /> is negative.</exception>
    public static RedactedText Create(
        string text,
        IReadOnlyList<SensitiveContentFinding> findings,
        int omittedCharacterCount)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentOutOfRangeException.ThrowIfNegative(omittedCharacterCount);

        return new RedactedText(text, [.. findings], omittedCharacterCount);
    }
}
