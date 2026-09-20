// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;

namespace MailFathom.Domain.Calendar;

/// <summary>Carries the <c>UID</c> a <c>VEVENT</c> named itself by, for an event that came out of a file.</summary>
/// <remarks>
/// <para>
/// RFC 5545 gives every entry in an iCalendar file a persistent, globally unique identifier, and this is that value
/// kept as the file wrote it. It exists for one question, asked by whoever reads such a file: has this entry already
/// been imported here. Nothing else reads it, an event a person typed or a proposal read out of mail carries none, and
/// it never identifies an event within MailFathom — <see cref="CalendarEventId" /> does.
/// </para>
/// <para>
/// It is compared as written rather than case-insensitively or after any other normalization. The specification makes
/// it an opaque string whose only property is equality, so two spellings of one identifier are two identifiers to
/// everybody producing them, and folding them together here would recognize as a repeat an entry the file says is a
/// different one.
/// </para>
/// </remarks>
public readonly record struct ImportedCalendarEventUid
{
    /// <summary>The greatest length an imported identifier may carry.</summary>
    /// <remarks>
    /// RFC 5545 states no ceiling, so this is one MailFathom sets: well past the domain-qualified UUID every producer
    /// of such a file actually writes, and short enough that the column a repeat is recognized through stays an index
    /// rather than a place a file can put a kilobyte of text per entry.
    /// </remarks>
    public const int MaximumLength = 512;

    private ImportedCalendarEventUid(string value) => this.Value = value;

    /// <summary>Gets the identifier as the file wrote it, trimmed of surrounding whitespace.</summary>
    public string Value { get; }

    /// <summary>Gets whether this value carries an identifier, which the default of the struct does not.</summary>
    public bool IsSpecified => this.Value is not null;

    /// <summary>Reads an identifier out of a file, refusing one that is blank, too long, or carrying a character that renders as nothing.</summary>
    /// <param name="value">The identifier as the file supplied it.</param>
    /// <param name="uid">The identifier, when it is one.</param>
    /// <returns><see langword="true" /> when <paramref name="value" /> is an identifier.</returns>
    /// <remarks>
    /// The file is untrusted input, and this value is answered back to whoever imported it in the report naming what
    /// was skipped — so a control character or a bidirectional override is refused here for the same reason a title's
    /// is, and the entry carrying one is reported as an entry that could not be read.
    /// </remarks>
    public static bool TryCreate(string? value, out ImportedCalendarEventUid uid)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed)
            || trimmed.Length > MaximumLength
            || trimmed.Any(static character =>
                char.IsControl(character)
                || char.GetUnicodeCategory(character) == UnicodeCategory.Format))
        {
            uid = default;

            return false;
        }

        uid = new ImportedCalendarEventUid(trimmed);

        return true;
    }

    /// <summary>Reads an identifier that is expected to be one.</summary>
    /// <param name="value">The identifier as supplied.</param>
    /// <returns>A validated identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is not an identifier.</exception>
    public static ImportedCalendarEventUid Create(string? value) =>
        TryCreate(value, out var uid)
            ? uid
            : throw new ArgumentException(
                $"An imported event identifier is non-blank, at most {MaximumLength} characters, and carries no control or format character.",
                nameof(value));
}
