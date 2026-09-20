// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Tasks;

namespace MailFathom.Application.Tasks;

/// <summary>One day, what the person owes on it, and what they are already committed to during it.</summary>
/// <remarks>
/// <para>
/// The whole of what a layout is decided from. The day is a window rather than a date because whose day it is decides
/// when it begins: this deployment keeps no timezone for a person, so the client states the two instants its own
/// calendar drew, exactly as it states them to read the calendar itself.
/// </para>
/// <para>
/// It carries the tasks and the commitments as values rather than as the records they were read from, because a layout
/// is decided from four things — what is owed, when it is due, when the day is busy, and how long the day is — and
/// nothing about a task's origin, its completion, or the message it cites changes the arrangement. What is not here
/// cannot reach a provider.
/// </para>
/// </remarks>
/// <param name="DayStart">The instant the person's day opens.</param>
/// <param name="DayEnd">The instant it closes.</param>
/// <param name="Tasks">What the person owes on that day, soonest due first.</param>
/// <param name="Commitments">What they are already committed to during it, earliest first.</param>
public sealed record DayLayoutQuestion(
    DateTimeOffset DayStart,
    DateTimeOffset DayEnd,
    IReadOnlyList<DayLayoutTask> Tasks,
    IReadOnlyList<DayLayoutCommitment> Commitments);

/// <summary>One task a day is to be laid out around, reduced to what deciding the arrangement needs.</summary>
/// <param name="Id">What addresses the task, which the arrangement names it by and no provider is shown.</param>
/// <param name="Title">The line the task is drawn with, which is derived personal data.</param>
/// <param name="DueOn">The day it is due on, or <see langword="null" /> where nobody has said when.</param>
public sealed record DayLayoutTask(PersonalTaskId Id, string Title, DateOnly? DueOn);

/// <summary>One thing the person is already committed to during the day being laid out.</summary>
/// <remarks>
/// A calendar event read through the calendar's own reading rather than a second query of its own, which is what keeps
/// one day's answer the same whichever screen asked for it. It carries no identity, because nothing in an arrangement
/// names an event: the commitments are the shape of the day rather than rows the answer moves.
/// </remarks>
/// <param name="Title">What the commitment is called, which is derived personal data.</param>
/// <param name="Start">When it begins.</param>
/// <param name="End">When it ends, or <see langword="null" /> where the event states no end.</param>
public sealed record DayLayoutCommitment(string Title, DateTimeOffset Start, DateTimeOffset? End);
