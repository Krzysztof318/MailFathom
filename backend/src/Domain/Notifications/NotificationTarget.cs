// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Calendar;
using MailFathom.Domain.Emails;

namespace MailFathom.Domain.Notifications;

/// <summary>Where opening a notification leads.</summary>
/// <remarks>
/// <para>
/// The four shapes are closed and each is reached through its own factory, so a target can never half exist — a
/// message target with no message, or a screen target that also names one. Which of the four a producer chose is what
/// a reader switches on, and <see cref="NotificationTargetKind" /> is that answer without unwrapping anything.
/// </para>
/// <para>
/// A message target is the one that ties a notification to mail, and it is what makes a notification erasable with the
/// message it describes: the record inherits the message's deletion rather than being swept for separately, so nothing
/// can leave a row pointing at mail that is gone.
/// </para>
/// </remarks>
public sealed record NotificationTarget
{
    private NotificationTarget(
        NotificationTargetKind kind,
        StoredEmailId? message,
        NotificationScreen? screen,
        CalendarEventId? calendarEvent)
    {
        this.Kind = kind;
        this.Message = message;
        this.Screen = screen;
        this.CalendarEvent = calendarEvent;
    }

    /// <summary>Gets which of the four shapes this target is.</summary>
    public NotificationTargetKind Kind { get; }

    /// <summary>Gets the stored message the notification leads to, and <see langword="null" /> for every other shape.</summary>
    public StoredEmailId? Message { get; }

    /// <summary>Gets the screen the notification leads to, and <see langword="null" /> for every other shape.</summary>
    public NotificationScreen? Screen { get; }

    /// <summary>Gets the calendar event the notification leads to, and <see langword="null" /> for every other shape.</summary>
    public CalendarEventId? CalendarEvent { get; }

    /// <summary>Gets the target of a notification that has nothing to open.</summary>
    public static NotificationTarget Nothing { get; } =
        new(NotificationTargetKind.Nothing, message: null, screen: null, calendarEvent: null);

    /// <summary>Creates a target that leads to one stored message.</summary>
    /// <param name="message">The message the notification is about.</param>
    /// <returns>A message target.</returns>
    public static NotificationTarget ToMessage(StoredEmailId message) =>
        new(NotificationTargetKind.Message, message, screen: null, calendarEvent: null);

    /// <summary>Creates a target that leads to a screen rather than to a record.</summary>
    /// <param name="screen">The screen the notification leads to.</param>
    /// <returns>A screen target.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="screen" /> is not a declared screen.</exception>
    public static NotificationTarget ToScreen(NotificationScreen screen)
    {
        if (!Enum.IsDefined(screen))
        {
            throw new ArgumentOutOfRangeException(
                nameof(screen),
                screen,
                "A notification leads to a declared screen or to nothing at all.");
        }

        return new NotificationTarget(NotificationTargetKind.Screen, message: null, screen, calendarEvent: null);
    }

    /// <summary>Creates a target that leads to one event of the person's own calendar.</summary>
    /// <param name="calendarEvent">The event the notification is about.</param>
    /// <returns>A calendar event target.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="calendarEvent" /> names no event.</exception>
    /// <remarks>
    /// One event rather than the calendar screen, because what a reminder is about is a single commitment rather than
    /// a day: a reader following it is taken to the thing they were reminded of, and a client with no screen for it
    /// yet leaves them where they were exactly as it does for a screen it does not answer for.
    /// </remarks>
    public static NotificationTarget ToCalendarEvent(CalendarEventId calendarEvent)
    {
        if (!calendarEvent.IsSpecified)
        {
            throw new ArgumentException(
                "A notification leading to a calendar event names the event it leads to.",
                nameof(calendarEvent));
        }

        return new NotificationTarget(
            NotificationTargetKind.CalendarEvent,
            message: null,
            screen: null,
            calendarEvent);
    }
}
