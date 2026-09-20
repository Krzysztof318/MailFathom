// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Reminders;

namespace MailFathom.Domain.Tasks;

/// <summary>States what announces a task: the leads it is announced at, and the offset its due day runs in.</summary>
/// <remarks>
/// <para>
/// The two travel together because neither means anything without the other. A lead is measured back from nine in
/// the morning on the due day, and a day names no instant until somebody says which offset it is read in — so a
/// writer stating leads and no offset has stated a reminder nothing can place, and one stating an offset and no
/// leads has answered a question nobody asked.
/// </para>
/// <para>
/// The offset is the person's rather than the deployment's. Nothing here keeps a timezone for anybody, so the client
/// states the offset its own day runs in on every write, exactly as it states the window a day is arranged over — and
/// a person who has moved since they set a reminder moves its instant the next time they edit the task, which is the
/// answer they would expect from a list they read where they are.
/// </para>
/// </remarks>
/// <param name="DueDayOffset">The UTC offset the person's due day runs in.</param>
/// <param name="Reminders">The leads the task is announced at, in any order and without repetition.</param>
public sealed record TaskAnnouncement(TimeSpan DueDayOffset, IReadOnlyCollection<Reminder> Reminders)
{
    /// <summary>The announcement of a task nothing is raised about, which is what every task carries until somebody sets a reminder.</summary>
    public static TaskAnnouncement Silent { get; } = new(TimeSpan.Zero, []);
}
