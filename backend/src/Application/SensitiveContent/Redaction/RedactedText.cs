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
        int omittedCharacterCount,
        IReadOnlyList<RedactedPlacement> placements)
    {
        this.Text = text;
        this.Findings = findings;
        this.OmittedCharacterCount = omittedCharacterCount;
        this.Placements = placements;
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

    /// <summary>Gets where each placeholder stands, in the order a reader of <see cref="Text" /> meets them.</summary>
    public IReadOnlyList<RedactedPlacement> Placements { get; }

    /// <summary>Gets whether anything was redacted at all.</summary>
    public bool IsRedacted => this.Findings.Count > 0;

    /// <summary>Gets how many characters of the original text this redaction actually looked at.</summary>
    /// <remarks>
    /// Recovered from <see cref="Text" /> and <see cref="Placements" /> rather than stored beside them, so the three
    /// cannot disagree: every character of the analyzed text either survived into the result or was replaced by a
    /// placeholder whose length is recorded.
    /// </remarks>
    private int AnalyzedCharacterCount =>
        this.Text.Length - this.Placements.Sum(placement => placement.PlaceholderLength - placement.Length);

    /// <summary>Finds where an offset into the text that was analyzed ended up in <see cref="Text" />.</summary>
    /// <param name="analyzedOffset">The offset in the text handed to the redaction.</param>
    /// <returns>The offset in <see cref="Text" />, or <see langword="null" /> where the analyzed ceiling dropped it.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="analyzedOffset" /> is negative.</exception>
    /// <remarks>
    /// <para>
    /// This is what lets an offset recorded against a text survive its redaction. A page boundary inside an attachment
    /// is such an offset, and without this every boundary of a document carrying one detected address would be dropped
    /// — a two-hundred-page contract losing every citation because a signature block held an email address.
    /// </para>
    /// <para>
    /// An offset that fell <em>inside</em> a replaced region answers with the start of the placeholder that replaced
    /// it, which is the nearest position that still exists. An offset past what the ceiling admitted answers with
    /// nothing at all, because the text it pointed into is not in the result: a coordinate that says nothing is better
    /// than one that sends a reader to the wrong page.
    /// </para>
    /// </remarks>
    public int? MapOffset(int analyzedOffset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(analyzedOffset);

        if (analyzedOffset > this.AnalyzedCharacterCount)
        {
            return null;
        }

        var shift = 0;

        foreach (var placement in this.Placements)
        {
            if (analyzedOffset < placement.Start)
            {
                break;
            }

            if (analyzedOffset < placement.Start + placement.Length)
            {
                return placement.Start + shift;
            }

            shift += placement.PlaceholderLength - placement.Length;
        }

        return analyzedOffset + shift;
    }

    /// <summary>Records a redaction.</summary>
    /// <param name="text">The text with every detected region replaced.</param>
    /// <param name="findings">Every finding the scanners reported.</param>
    /// <param name="omittedCharacterCount">How many characters lay beyond what one scan analyzes.</param>
    /// <param name="placements">Where each placeholder stands, in order, or nothing where none was applied.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text" /> or <paramref name="findings" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="omittedCharacterCount" /> is negative.</exception>
    /// <remarks>
    /// <paramref name="placements" /> defaults to none, which is the identity mapping and is what a caller composing a
    /// result with no findings means. Nothing derives it from <paramref name="findings" />: those are reported before
    /// overlapping regions were merged, so one placeholder need not correspond to one finding, and reconstructing the
    /// merge here would be a second copy of the rule the redactor applies.
    /// </remarks>
    public static RedactedText Create(
        string text,
        IReadOnlyList<SensitiveContentFinding> findings,
        int omittedCharacterCount,
        IReadOnlyList<RedactedPlacement>? placements = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentOutOfRangeException.ThrowIfNegative(omittedCharacterCount);

        return new RedactedText(text, [.. findings], omittedCharacterCount, [.. placements ?? []]);
    }
}
