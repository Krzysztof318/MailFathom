// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text;

namespace MailFathom.Domain.Contacts;

/// <summary>Decides which characters the two free-text values of a contact may not carry.</summary>
/// <remarks>
/// <para>
/// A name and a note are published into listings of the other contacts, into an answer an agent reads, and into an
/// export. A character that renders as nothing, ends the line it is on, or reverses the direction of what follows it can
/// therefore make one record say something about the ones around it, which is why the rule is a refusal rather than a
/// silent strip: an owner typed the value and is told it was not accepted.
/// </para>
/// <para>
/// The text is examined as Unicode scalars rather than as UTF-16 code units, because the formatting characters outside
/// the Basic Multilingual Plane — the language tag characters at U+E0001 among them — are surrogate pairs whose halves
/// categorize as <c>Surrogate</c> and not as <c>Format</c>. A per-character test would keep exactly the invisible
/// character this exists to refuse.
/// </para>
/// </remarks>
internal static class ContactText
{
    private const int ZeroWidthNonJoiner = 0x200C;

    private const int ZeroWidthJoiner = 0x200D;

    /// <summary>Answers whether a scalar would render as something other than the text it is part of.</summary>
    /// <param name="scalar">The scalar to judge.</param>
    /// <returns><see langword="true" /> when the scalar carries no glyph of its own.</returns>
    /// <remarks>
    /// The line and paragraph separators are here beside the control characters because they end a line exactly as a
    /// newline does while categorizing as neither; the formatting characters are here because the bidirectional overrides
    /// and isolates are among them, and one of those makes a name render as text it does not contain.
    /// </remarks>
    internal static bool IsUnprintable(Rune scalar) =>
        !IsWrittenInsideAWord(scalar)
        && (Rune.IsControl(scalar)
            || Rune.GetUnicodeCategory(scalar) is UnicodeCategory.Format
                or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator);

    /// <summary>Answers whether a scalar is one of the control characters that carry layout rather than terminal behavior.</summary>
    /// <param name="scalar">The scalar to judge.</param>
    /// <returns><see langword="true" /> when the scalar is a line break or a tab.</returns>
    internal static bool IsLayout(Rune scalar) => scalar.Value is '\r' or '\n' or '\t';

    /// <summary>Admits the two formatting characters that belong inside ordinary words.</summary>
    /// <remarks>
    /// The zero-width joiner and non-joiner decide how neighbouring letters are shaped, which Persian, Arabic, and the
    /// Indic scripts rely on inside names people actually write. Refusing every formatting character would refuse those
    /// names, so the two that join letters are admitted while the ones that reorder, isolate, or tag a run are not.
    /// </remarks>
    private static bool IsWrittenInsideAWord(Rune scalar) =>
        scalar.Value is ZeroWidthNonJoiner or ZeroWidthJoiner;
}
