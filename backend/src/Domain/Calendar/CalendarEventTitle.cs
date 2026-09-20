// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Calendar;

/// <summary>Names what an event is, in the words of whoever put it on the calendar.</summary>
/// <remarks>
/// <para>
/// Three writers reach this value and none of them is trusted: a person typing into the client, a model reading a date
/// out of a message, and an <c>.ics</c> file somebody was handed. A title is then drawn in a list beside the other
/// events and read back in an answer, so a control character would end the line it is on and a bidirectional override
/// would render the rest of a row as text the record does not contain. Both are refused rather than stripped, because a
/// person who typed one is told the title was not accepted instead of being shown a different title than they wrote.
/// </para>
/// <para>
/// This is personal data the moment it says who a meeting is with. Nothing logs it, records it as a metric dimension,
/// or writes it into a failure message; <see cref="CalendarEventId" /> is what a failure names.
/// </para>
/// </remarks>
public readonly record struct CalendarEventTitle
{
    /// <summary>The greatest length a title may carry.</summary>
    /// <remarks>
    /// Generous against a line somebody reads at a glance in a day's column, and short enough that the field cannot
    /// become a way to keep the agenda of a meeting in the column that names it.
    /// </remarks>
    public const int MaximumLength = 200;

    private CalendarEventTitle(string value) => this.Value = value;

    /// <summary>Gets the title as it was written, trimmed.</summary>
    public string Value { get; }

    /// <summary>Gets whether this value carries a title, which the default of the struct does not.</summary>
    public bool IsSpecified => this.Value is not null;

    /// <summary>Reads a title, refusing one that is blank, too long, or carrying a character that renders as nothing.</summary>
    /// <remarks><see cref="CalendarText" /> holds which characters those are, and why the text is walked as Unicode scalars rather than as UTF-16 code units.</remarks>
    /// <param name="value">The title as supplied.</param>
    /// <param name="title">The title, when it is one.</param>
    /// <returns><see langword="true" /> when <paramref name="value" /> is a title.</returns>
    public static bool TryCreate(string? value, out CalendarEventTitle title)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaximumLength || !CalendarText.IsReadable(trimmed))
        {
            title = default;

            return false;
        }

        title = new CalendarEventTitle(trimmed);

        return true;
    }

    /// <summary>Reads a title that is expected to be one.</summary>
    /// <param name="value">The title as supplied.</param>
    /// <returns>A validated title.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is not a title.</exception>
    public static CalendarEventTitle Create(string? value) =>
        TryCreate(value, out var title)
            ? title
            : throw new ArgumentException(
                $"An event title is non-blank, at most {MaximumLength} characters, and carries no control or format character.",
                nameof(value));
}
