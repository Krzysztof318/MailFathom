// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;

namespace MailFathom.Application.SensitiveContent.Detection;

/// <summary>Where in the analyzed text a finding sits, as a character offset and a length.</summary>
/// <remarks>
/// <para>
/// A finding points at its text rather than carrying it. That is the whole reason this type exists: a record that held
/// the matched value would recreate the leak in the object written to prevent it, and every consumer that logs, stores,
/// or audits a finding would carry the credential along with it.
/// </para>
/// <para>
/// Offsets are into the text handed to the scanner, in UTF-16 code units, which is what indexing a
/// <see cref="string" /> uses. Being a struct, <see langword="default" /> is reachable and describes no region;
/// <see cref="IsSpecified" /> reports that, and <see cref="Create" /> is the only route to a usable value.
/// </para>
/// </remarks>
public readonly record struct SensitiveContentSpan
{
    private SensitiveContentSpan(int start, int length)
    {
        this.Start = start;
        this.Length = length;
    }

    /// <summary>Gets the offset of the first character the finding covers.</summary>
    public int Start { get; }

    /// <summary>Gets the number of characters the finding covers, which is always at least one.</summary>
    public int Length { get; }

    /// <summary>Gets the offset just past the last character the finding covers.</summary>
    public int End => this.Start + this.Length;

    /// <summary>Gets whether this value describes a region rather than the unusable struct default.</summary>
    public bool IsSpecified => this.Length > 0;

    /// <summary>Creates a span over a region of the analyzed text.</summary>
    /// <param name="start">The offset of the first character the finding covers.</param>
    /// <param name="length">The number of characters the finding covers.</param>
    /// <returns>The validated span.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the offset is negative or the length is not positive.</exception>
    /// <remarks>
    /// A zero-length span is refused rather than accepted as an empty match, because redaction would replace nothing
    /// with a placeholder and the text a reader sees would gain a marker naming a value that was never there.
    /// </remarks>
    public static SensitiveContentSpan Create(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);

        return new SensitiveContentSpan(start, length);
    }

    /// <summary>Reports whether this span shares at least one character with another.</summary>
    /// <param name="other">The span to compare against.</param>
    /// <returns><see langword="true" /> when the two overlap; otherwise <see langword="false" />.</returns>
    /// <remarks>Two spans that merely touch do not overlap, so adjacent findings stay two placeholders rather than one.</remarks>
    public bool Overlaps(SensitiveContentSpan other) => this.Start < other.End && other.Start < this.End;

    /// <summary>Produces the smallest span covering both this one and another.</summary>
    /// <param name="other">The span to cover as well.</param>
    /// <returns>The covering span.</returns>
    /// <remarks>
    /// Used where two findings overlap. Covering both is what keeps the tail of the longer one from surviving into the
    /// redacted text, which dropping the second finding would leave behind.
    /// </remarks>
    public SensitiveContentSpan CoverWith(SensitiveContentSpan other)
    {
        var start = Math.Min(this.Start, other.Start);

        return new SensitiveContentSpan(start, Math.Max(this.End, other.End) - start);
    }

    /// <inheritdoc />
    public override string ToString() => this.IsSpecified
        ? string.Format(CultureInfo.InvariantCulture, "[{0}, {1})", this.Start, this.End)
        : "(unspecified)";
}
