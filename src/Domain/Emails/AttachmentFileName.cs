// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text;

namespace MailFathom.Domain.Emails;

/// <summary>Holds an attachment file name that is safe to store, display, and log a decision about.</summary>
/// <remarks>
/// <para>
/// A file name is attacker-controlled content, not a label. After the transport encodings are decoded it can carry path
/// separators and traversal segments, control characters and line breaks, unbounded length, and bidirectional overrides
/// that make a name render as something other than what it is. Normalization happens once, here, so no consumer has to
/// remember which of those a name it was handed still contains.
/// </para>
/// <para>
/// <see cref="WasNormalized" /> exists so a caller can tell a plain name from a repaired one. A name that survives
/// unchanged is the ordinary case and must stay distinguishable from one this type had to rewrite.
/// </para>
/// </remarks>
public readonly record struct AttachmentFileName
{
    /// <summary>The greatest number of characters a normalized file name keeps.</summary>
    /// <remarks>
    /// The bound is about what a name may cost to store, index, and render rather than about any filesystem: nothing
    /// here writes a file. It is generous enough that no name a person chose is truncated.
    /// </remarks>
    public const int MaxLength = 200;

    private AttachmentFileName(string value, bool wasNormalized)
    {
        this.Value = value;
        this.WasNormalized = wasNormalized;
    }

    /// <summary>Gets the normalized name, which is never blank and never carries path structure.</summary>
    public string Value { get; }

    /// <summary>Gets whether normalization changed what the message wrote.</summary>
    public bool WasNormalized { get; }

    /// <summary>Normalizes a decoded file name into one that is safe to keep.</summary>
    /// <param name="decodedFileName">The file name after its RFC 2047 or RFC 2231 transport encoding was decoded.</param>
    /// <param name="fileName">The normalized name, when anything usable remained.</param>
    /// <returns><see langword="true" /> when a usable name remained; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// A part left with nothing usable is unnamed rather than given a synthetic name, because a generated
    /// <c>attachment-1.bin</c> would be indistinguishable from a name the sender actually wrote.
    /// </remarks>
    public static bool TryNormalize(string? decodedFileName, out AttachmentFileName fileName)
    {
        fileName = default;

        if (string.IsNullOrEmpty(decodedFileName))
        {
            return false;
        }

        var withoutHiddenCharacters = RemoveControlAndFormattingCharacters(decodedFileName);
        var withoutPathStructure = TakeLastPathSegment(withoutHiddenCharacters);
        var trimmed = withoutPathStructure.Trim().TrimEnd('.', ' ');

        if (trimmed.Length == 0)
        {
            return false;
        }

        // A single text element longer than the bound leaves nothing to keep, which is the unnamed case rather than a
        // name cut through the middle of one grapheme.
        var bounded = MailTextBounds.TruncateAtTextElementBoundary(trimmed, MaxLength);
        if (bounded.Length == 0)
        {
            return false;
        }

        fileName = new AttachmentFileName(bounded, !string.Equals(bounded, decodedFileName, StringComparison.Ordinal));

        return true;
    }

    /// <inheritdoc />
    public override string ToString() => this.Value;

    /// <summary>Drops what would let a name misrepresent itself or break the line it is written on.</summary>
    /// <remarks>
    /// <para>
    /// Formatting characters are removed alongside control characters because the bidirectional overrides are in that
    /// category: a name written as <c>invoice</c>, U+202E, <c>gnp.exe</c> renders as <c>invoiceexe.png</c> while
    /// remaining an executable name.
    /// </para>
    /// <para>
    /// The name is examined as Unicode scalars rather than as UTF-16 code units, because the formatting characters
    /// outside the Basic Multilingual Plane — the language tag characters at U+E0001 and the musical formatting
    /// controls among them — are surrogate pairs. Each half of such a pair categorizes as <c>Surrogate</c> and not as
    /// <c>Format</c>, so a per-character test would keep exactly the invisible character it exists to drop. Enumerating
    /// scalars additionally replaces an unpaired surrogate with U+FFFD, which leaves a value every consumer can write.
    /// </para>
    /// </remarks>
    private static string RemoveControlAndFormattingCharacters(string fileName) =>
        string.Concat(fileName.EnumerateRunes().Where(scalar =>
            !Rune.IsControl(scalar) && Rune.GetUnicodeCategory(scalar) != UnicodeCategory.Format));

    /// <summary>Reduces a name that carries path structure to the name at its end.</summary>
    /// <remarks>
    /// Both separators and the Windows drive separator are cut, whichever platform the sender used, so a result can
    /// never be interpreted as a location. Traversal segments disappear with the separators that gave them meaning.
    /// </remarks>
    private static string TakeLastPathSegment(string fileName)
    {
        var lastSeparatorIndex = fileName.LastIndexOfAny(['/', '\\', ':']);

        return lastSeparatorIndex < 0 ? fileName : fileName[(lastSeparatorIndex + 1)..];
    }
}
