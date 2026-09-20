// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Calendar;

/// <summary>Identifies one event this deployment holds, for as long as it is held.</summary>
/// <remarks>
/// The identity is MailFathom's own rather than anything a file or a message supplied, which is what lets an event keep
/// it while being accepted and amended. An imported entry's own <c>UID</c> is carried beside it as an
/// <see cref="ImportedCalendarEventUid" /> and is never this value: the file that named it is one source among several,
/// and nothing outside this deployment decides what an event here is.
/// </remarks>
public readonly record struct CalendarEventId
{
    private CalendarEventId(Guid value) => this.Value = value;

    /// <summary>Gets the non-empty UUID value.</summary>
    public Guid Value { get; }

    /// <summary>Gets whether this value names an event, which the default of the struct does not.</summary>
    public bool IsSpecified => this.Value != Guid.Empty;

    /// <summary>Creates a calendar event identifier from a non-empty UUID.</summary>
    /// <param name="value">The UUID to wrap.</param>
    /// <returns>A validated calendar event identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is empty.</exception>
    public static CalendarEventId Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A calendar event identifier cannot be empty.", nameof(value));
        }

        return new CalendarEventId(value);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value.ToString();
}
