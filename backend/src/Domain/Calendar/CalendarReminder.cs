// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;

namespace MailFathom.Domain.Calendar;

/// <summary>States how long before an event a person is to be reminded of it.</summary>
/// <remarks>
/// <para>
/// A lead rather than an instant, which is what makes a reminder survive the event moving: what a person chose is
/// <em>a quarter of an hour beforehand</em>, and the instant that falls at is derived from the event every time it is
/// asked for. <see cref="CalendarEvent.RemindsAt" /> is where that derivation lives, because the anchor an all-day
/// event is measured from is the event's own affair rather than the reminder's.
/// </para>
/// <para>
/// Zero is a lead like any other and means the moment the event begins. It is not the absence of a reminder: an event
/// nobody wants to be told about carries no reminder at all.
/// </para>
/// <para>
/// The lead is whole minutes because that is the coarsest unit the client offers and the finest anything here acts on:
/// a run looking for what has come due cannot be more precise than the interval it runs on, and a reminder stated in
/// seconds would promise a precision no producer keeps.
/// </para>
/// </remarks>
public readonly record struct CalendarReminder
{
    /// <summary>The longest lead a reminder may state, which is four weeks.</summary>
    /// <remarks>
    /// Wide enough for every lead the client offers and for the far end of what somebody would set by hand on a
    /// quarterly commitment, and bounded at all because the lead decides how far ahead of now a run has to look: an
    /// unbounded one would make every pass read the whole calendar rather than the days around it.
    /// </remarks>
    public const int MaximumMinutesBefore = 28 * 24 * 60;

    private CalendarReminder(int minutesBefore) => this.MinutesBefore = minutesBefore;

    /// <summary>Gets how many minutes before the event this reminder falls, which is zero at the event itself.</summary>
    public int MinutesBefore { get; }

    /// <summary>Gets how long before the event this reminder falls.</summary>
    public TimeSpan Lead => TimeSpan.FromMinutes(this.MinutesBefore);

    /// <summary>Creates a reminder from the lead a person stated.</summary>
    /// <param name="minutesBefore">How many minutes before the event to remind them, which may be zero.</param>
    /// <returns>A validated reminder.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="minutesBefore" /> is negative or longer than <see cref="MaximumMinutesBefore" />.</exception>
    public static CalendarReminder Create(int minutesBefore)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minutesBefore);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minutesBefore, MaximumMinutesBefore);

        return new CalendarReminder(minutesBefore);
    }

    /// <summary>Reports whether a stated lead is one a reminder can be created from.</summary>
    /// <param name="minutesBefore">The lead as supplied.</param>
    /// <returns><see langword="true" /> when <see cref="Create" /> would accept it.</returns>
    /// <remarks>
    /// A surface reading what somebody typed asks this rather than catching the refusal, so a lead nobody can set is
    /// reported to them as a lead rather than as a fault in the deployment.
    /// </remarks>
    public static bool IsStatable(int minutesBefore) => minutesBefore is >= 0 and <= MaximumMinutesBefore;

    /// <inheritdoc />
    public override string ToString() => this.MinutesBefore.ToString(CultureInfo.InvariantCulture);
}
