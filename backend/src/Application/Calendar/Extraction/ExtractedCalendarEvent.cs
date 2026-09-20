// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Calendar;

namespace MailFathom.Application.Calendar.Extraction;

/// <summary>One event a reading of text found in it, before anything decided whose calendar it belongs to.</summary>
/// <param name="Title">What the text called it, already held to everything a title is held to.</param>
/// <param name="Start">When it begins.</param>
/// <param name="End">When it ends, or <see langword="null" /> where the text said how long it lasts.</param>
/// <remarks>
/// <para>
/// The same shape whichever text produced it, which is the whole reason one extraction serves both halves: a date a
/// message named and a sentence somebody typed differ in where they came from rather than in what was found. What
/// separates them is what the caller does next — a message's becomes a proposal on somebody's calendar, and a
/// sentence's is handed back to the person who typed it and stored only if they save it.
/// </para>
/// <para>
/// It carries no origin, no owner, and no citation, because none of those is in the text: the reading knows what it
/// read and nothing about the calendar it may reach. An end before or at the start never gets this far — the reading
/// drops such an event, which is the same rule <see cref="CalendarEvent" /> enforces when one is composed.
/// </para>
/// </remarks>
public sealed record ExtractedCalendarEvent(CalendarEventTitle Title, DateTimeOffset Start, DateTimeOffset? End);
