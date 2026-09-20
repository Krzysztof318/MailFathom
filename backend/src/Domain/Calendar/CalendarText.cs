// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers;
using System.Globalization;
using System.Text;

namespace MailFathom.Domain.Calendar;

/// <summary>Decides which characters the free text an event carries may not hold.</summary>
/// <remarks>
/// <para>
/// A title is drawn in a list beside the other events and read back in an answer, and an imported identifier is
/// answered back in the report naming what an import skipped. A character that renders as nothing, ends the line it is
/// on, or reverses the direction of what follows it can therefore make one record say something about the ones around
/// it, which is why the rule is a refusal rather than a silent strip: whoever supplied the value is told it was not
/// accepted.
/// </para>
/// <para>
/// The text is examined as Unicode scalars rather than as UTF-16 code units, because the formatting characters outside
/// the Basic Multilingual Plane are surrogate pairs whose halves categorize as <c>Surrogate</c> and not as
/// <c>Format</c>. A per-character test would keep exactly the invisible character this exists to refuse, and would
/// admit the line and paragraph separators, which end a line while categorizing as neither control nor format.
/// </para>
/// <para>
/// It is the contact book's rule applied to this calendar's own values rather than a second reading of it: the book
/// admits the layout characters a note may carry, and nothing an event holds is written across lines.
/// </para>
/// </remarks>
internal static class CalendarText
{
    private const int ZeroWidthNonJoiner = 0x200C;

    private const int ZeroWidthJoiner = 0x200D;

    /// <summary>Answers whether text is written entirely in characters that render as part of it.</summary>
    /// <param name="value">The text to judge.</param>
    /// <returns><see langword="true" /> when every code unit takes part in a scalar and every scalar carries a glyph.</returns>
    internal static bool IsReadable(string value) =>
        IsWellFormed(value) && !value.EnumerateRunes().Any(IsUnprintable);

    /// <summary>Answers whether a scalar would render as something other than the text it is part of.</summary>
    /// <remarks>
    /// The line and paragraph separators are here beside the control characters because they end a line exactly as a
    /// newline does while categorizing as neither; the formatting characters are here because the bidirectional
    /// overrides and isolates are among them, and one of those makes a title render as text it does not contain.
    /// </remarks>
    private static bool IsUnprintable(Rune scalar) =>
        !IsWrittenInsideAWord(scalar)
        && (Rune.IsControl(scalar)
            || Rune.GetUnicodeCategory(scalar) is UnicodeCategory.Format
                or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator);

    /// <summary>Answers whether text is well-formed UTF-16, which is what makes the scalar walk above mean anything.</summary>
    /// <remarks>
    /// An unpaired surrogate is not a character and has no category, so enumerating scalars substitutes U+FFFD for it —
    /// which is a printable symbol and passes every rule above while the ill-formed code unit stays in the stored value.
    /// It is refused here instead, because the first thing to reject it otherwise would be the UTF-8 encoder inside
    /// Npgsql or the JSON writer, and a value somebody supplied would come back as an encoding failure rather than as a
    /// value that was not accepted.
    /// </remarks>
    private static bool IsWellFormed(string value)
    {
        var remaining = value.AsSpan();

        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out _, out var consumed) is not OperationStatus.Done)
            {
                return false;
            }

            remaining = remaining[consumed..];
        }

        return true;
    }

    /// <summary>Admits the two formatting characters that belong inside ordinary words.</summary>
    /// <remarks>
    /// The zero-width joiner and non-joiner decide how neighbouring letters are shaped, which Persian, Arabic, and the
    /// Indic scripts rely on inside the words people actually write. Refusing every formatting character would refuse
    /// a title written in one of those scripts, so the two that join letters are admitted while the ones that reorder,
    /// isolate, or tag a run are not.
    /// </remarks>
    private static bool IsWrittenInsideAWord(Rune scalar) =>
        scalar.Value is ZeroWidthNonJoiner or ZeroWidthJoiner;
}
