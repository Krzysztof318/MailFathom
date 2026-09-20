// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Access;
using MailFathom.Application.Paging;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Reminders;
using MailFathom.Domain.Tasks;

namespace MailFathom.Application.Tasks;

/// <summary>Reads and writes the signed-in person's own task list.</summary>
/// <remarks>
/// <para>
/// Whose tasks these are comes from the principal rather than from the request, exactly as it does for the user record,
/// the client preferences, and the notification centre: there is no argument here for another user's identifier, so a
/// reading of somebody else's list is something a caller cannot express rather than something a surface has to refuse.
/// A task named by identifier is addressed with the user beside it, so one another person holds answers as one that
/// does not exist.
/// </para>
/// <para>
/// The two origins are read apart rather than filtered, because they are two things to somebody looking at a screen:
/// what they have committed to, and what mail suggested and nobody has agreed to yet. A cursor is issued against the
/// origin it was read under and refused in the other, which is what stops a walk of the proposals continuing into the
/// commitments halfway down a screen.
/// </para>
/// <para>
/// Every act is admitted under <see cref="MailFathomPermission.MailRead" />, the writes included, and none of them adds
/// a name to the published set. It is the notification centre's reasoning rather than the mutation routes': a task is
/// this deployment's own record of what a person owes, nothing here reaches a mail server, nothing moves in a mailbox,
/// and a person whose mail accounts an administrator maintains has to be able to keep their own list.
/// </para>
/// </remarks>
public sealed class OwnTasks
{
    /// <summary>The page size a request that names none is served.</summary>
    /// <remarks>Enough to fill the list a client draws without scrolling, so a first paint is one request.</remarks>
    public const int DefaultPageSize = 50;

    /// <summary>The greatest page size one request is served, whatever it asked for.</summary>
    /// <remarks>
    /// A task is a line, a day, and two flags, so a page of this many is a few tens of kilobytes. It is a bound rather
    /// than a refusal, for the reason the notification centre clamps where the mail list refuses: this serves a panel
    /// asking for as much as it can draw, where the useful answer to a number no screen wants is the most this
    /// deployment serves rather than an error the screen has to render instead of a list.
    /// </remarks>
    public const int MaximumPageSize = 200;

    private readonly AccessAuthorization authorization;
    private readonly IPersonalTaskStore store;
    private readonly TimeProvider clock;

    /// <summary>Initializes the use case.</summary>
    /// <param name="authorization">Reports the grant the caller holds and the user it acts for.</param>
    /// <param name="store">Holds what a person owes.</param>
    /// <param name="clock">Stamps the identity a new task is addressed under.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public OwnTasks(AccessAuthorization authorization, IPersonalTaskStore store, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);

        this.authorization = authorization;
        this.store = store;
        this.clock = clock;
    }

    /// <summary>Reads one page of the signed-in person's tasks of one origin, soonest due first.</summary>
    /// <param name="origin">The half of the list to read: what the person owes, or what mail proposed.</param>
    /// <param name="pageSize">How many tasks the page may hold, or <see langword="null" /> for <see cref="DefaultPageSize" />; a larger number is served <see cref="MaximumPageSize" />.</param>
    /// <param name="cursor">The cursor a previous page returned, or <see langword="null" /> for the first page.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The page, or <see langword="null" /> when the cursor is not one this deployment issued for this reading.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="origin" /> is not a declared origin.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.MailRead" />.</exception>
    /// <remarks>
    /// Completed tasks are served with the outstanding ones and carry the flag that tells them apart, because which of
    /// them a screen draws is the screen's decision: a list that hid them would leave a person no way to see what they
    /// did today, and one filtered here would need a second route to get them back.
    /// </remarks>
    public async Task<PersonalTaskPage?> ReadPageAsync(
        PersonalTaskOrigin origin,
        int? pageSize,
        string? cursor,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin), origin, "A task list is read under a declared origin.");
        }

        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        var user = this.authorization.RequireUser();
        var fingerprint = FingerprintOf(user, origin);
        PersonalTaskCursor? boundary = null;

        if (!string.IsNullOrWhiteSpace(cursor) && !TryReadBoundary(cursor, fingerprint, out boundary))
        {
            return null;
        }

        var limit = Bounded(pageSize);
        var tasks = await this.store.ReadAsync(user, origin, boundary, limit, cancellationToken);

        // The page is short only where the list held nothing more, so the boundary is issued exactly when a full page
        // came back — which is what lets a caller stop on the absent cursor rather than on a length comparison.
        var next = tasks.Count == limit && tasks[^1] is { } last
            ? Encode(last, fingerprint)
            : null;

        return new PersonalTaskPage(tasks, next);
    }

    /// <summary>Reads one of the signed-in person's tasks.</summary>
    /// <param name="task">The task to read.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The task, or <see langword="null" /> when this person holds none under that identity.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.MailRead" />.</exception>
    public Task<PersonalTask?> FindAsync(PersonalTaskId task, CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        return this.store.FindAsync(this.authorization.RequireUser(), task, cancellationToken);
    }

    /// <summary>Records a task the signed-in person has just committed to.</summary>
    /// <param name="title">The line the list is drawn with.</param>
    /// <param name="dueOn">The day it is due on, or <see langword="null" /> where they said nothing about when.</param>
    /// <param name="announcement">What announces it and the offset their due day runs in, or <see cref="TaskAnnouncement.Silent" /> to announce nothing.</param>
    /// <param name="sourceMessage">The message they were looking at, or <see langword="null" /> where the task cites none.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The task as it was written.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="announcement" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="title" /> is blank, when one lead is stated twice, or when leads are stated against no due day.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="title" /> exceeds <see cref="PersonalTask.MaximumTitleLength" />, or when more than <see cref="Reminder.MaximumCount" /> leads are stated.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.MailRead" />.</exception>
    /// <remarks>
    /// What somebody types into their own client is something they owe, so a task written here is asserted and this is
    /// not the seam a proposal arrives through: what mail suggested is written by the extraction that read it, under
    /// the origin that says nobody has agreed to it yet.
    /// <para>
    /// The citation is taken as the caller states it and is not resolved against the mailbox. It is a value the task
    /// keeps rather than a reference the row depends on, and following one is the citation route's act under the same
    /// grant — so an identity naming nothing resolves to nothing there rather than being refused here, and nothing about
    /// whose mail exists is reported by writing a task.
    /// </para>
    /// </remarks>
    public async Task<PersonalTask> RecordAsync(
        string title,
        DateOnly? dueOn,
        TaskAnnouncement announcement,
        StoredEmailId? sourceMessage,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        var task = PersonalTask.Compose(
            PersonalTaskId.Create(Guid.CreateVersion7(this.clock.GetUtcNow())),
            this.authorization.RequireUser(),
            title,
            dueOn,
            announcement,
            PersonalTaskOrigin.Asserted,
            sourceMessage);

        await this.store.AddAsync(task, cancellationToken);

        return task;
    }

    /// <summary>Writes what the signed-in person edited about one of their tasks.</summary>
    /// <param name="task">The task to revise.</param>
    /// <param name="title">The line the list is to be drawn with from now on.</param>
    /// <param name="dueOn">The day it is due on, or <see langword="null" /> where they took the date off it.</param>
    /// <param name="announcement">What announces it afterwards and the offset their due day runs in, or <see cref="TaskAnnouncement.Silent" /> to announce nothing.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The task as it now stands, or <see langword="null" /> when this person holds none under that identity.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="announcement" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="title" /> is blank, when one lead is stated twice, or when leads are stated against no due day.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="title" /> exceeds <see cref="PersonalTask.MaximumTitleLength" />, or when more than <see cref="Reminder.MaximumCount" /> leads are stated.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.MailRead" />.</exception>
    /// <remarks>
    /// The task is read before it is written because an edit is stated against the record it edits: the answer is what
    /// the person now holds rather than what they sent, and a task erased between the two is reported as one they do
    /// not hold rather than written back into existence.
    /// <para>
    /// What announces the task is part of the edit rather than beside it, which is what makes turning the last
    /// reminder off a task stated with none rather than a field left out — and what makes taking the date off one
    /// take its reminders with it, since a lead measured back from no due day is refused.
    /// </para>
    /// </remarks>
    public async Task<PersonalTask?> ReviseAsync(
        PersonalTaskId task,
        string title,
        DateOnly? dueOn,
        TaskAnnouncement announcement,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        var held = await this.store.FindAsync(this.authorization.RequireUser(), task, cancellationToken);

        if (held is null)
        {
            return null;
        }

        var revision = held.Revise(title, dueOn, announcement);

        return await this.store.ReviseAsync(revision, cancellationToken) is PersonalTaskChangeOutcome.Applied
            ? revision
            : null;
    }

    /// <summary>Turns one of the signed-in person's proposals into a task they owe.</summary>
    /// <param name="task">The task to accept.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The task as it now stands, or <see langword="null" /> when this person holds none under that identity.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.MailRead" />.</exception>
    /// <remarks>
    /// A task already asserted is the state the caller asked for rather than an error, so accepting twice is answered
    /// as done. Dismissing a proposal is the erasure instead: what a person declines is not a task they owe, and
    /// keeping a declined row would leave a list whose every reader had to remember to exclude it.
    /// </remarks>
    public Task<PersonalTask?> AcceptAsync(PersonalTaskId task, CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        var user = this.authorization.RequireUser();

        return this.ReadBackAsync(user, task, this.store.AcceptAsync(user, task, cancellationToken), cancellationToken);
    }

    /// <summary>Puts one of the signed-in person's tasks into a stated completion state.</summary>
    /// <param name="task">The task to change.</param>
    /// <param name="isCompleted">The completion state it is to stand in.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The task as it now stands, or <see langword="null" /> when this person holds none under that identity.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.MailRead" />.</exception>
    public Task<PersonalTask?> SetCompletionAsync(
        PersonalTaskId task,
        bool isCompleted,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        var user = this.authorization.RequireUser();

        return this.ReadBackAsync(
            user,
            task,
            this.store.SetCompletionAsync(user, task, isCompleted, cancellationToken),
            cancellationToken);
    }

    /// <summary>Erases one of the signed-in person's tasks.</summary>
    /// <param name="task">The task to erase.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns><see langword="true" /> when a task was erased, and <see langword="false" /> when this person held none.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.MailRead" />.</exception>
    /// <remarks>
    /// One act rather than two, whichever origin the task carries: deleting something a person owes and dismissing
    /// something mail suggested leave the same list behind, and a second name for the same erasure would be a route
    /// whose only difference was the word a screen puts on the button.
    /// </remarks>
    public Task<bool> EraseAsync(PersonalTaskId task, CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        return this.store.EraseAsync(this.authorization.RequireUser(), task, cancellationToken);
    }

    /// <summary>Reduces the reading's own narrowing to the fingerprint its cursors carry.</summary>
    /// <remarks>
    /// The user and the origin, because those are the two things this reading narrows by. The user comes off the
    /// credential, so a cursor presented by somebody it was not issued to is refused; the origin is the route, so one
    /// issued for the proposals names no boundary in the commitments.
    /// </remarks>
    private static string FingerprintOf(MailUserId user, PersonalTaskOrigin origin) => PageFilterFingerprint.Of(
        user.Value.ToString("N", CultureInfo.InvariantCulture),
        origin.ToString());

    /// <summary>Writes the boundary one page ended on as the opaque string a caller presents to continue the walk.</summary>
    /// <remarks>
    /// A due day is written as midnight UTC, which is what lets the day ordering share the encoding every other keyset
    /// cursor here uses; an undated task is written as the absent position that encoding already carries, which is the
    /// block the order puts last.
    /// </remarks>
    private static string Encode(PersonalTask last, string fingerprint) => KeysetCursorPayload
        .At(PositionOf(last.DueOn), last.Id.Value, fingerprint)
        .Encode();

    /// <summary>Reads the boundary a caller presented, refusing one this reading did not issue.</summary>
    private static bool TryReadBoundary(string cursor, string fingerprint, out PersonalTaskCursor? boundary)
    {
        boundary = null;

        if (!KeysetCursorPayload.TryDecode(cursor, out var payload)
            || !string.Equals(payload.FilterFingerprint, fingerprint, StringComparison.Ordinal))
        {
            return false;
        }

        boundary = PersonalTaskCursor.After(
            payload.Position is { } position ? DateOnly.FromDateTime(position.UtcDateTime) : null,
            PersonalTaskId.Create(payload.Identity));

        return true;
    }

    /// <summary>Writes a due day as the instant the shared encoding carries, and an undated task as no position at all.</summary>
    private static DateTimeOffset? PositionOf(DateOnly? dueOn) =>
        dueOn is { } day ? new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null;

    /// <summary>Reads back what a state change left, so a caller redraws the row from what this deployment holds.</summary>
    /// <remarks>
    /// The read is what makes one answer serve both acts: accepting and completing each move one column, and a client
    /// redrawing a row needs the whole task rather than the column it asked about. A task erased between the write and
    /// the read is reported as one the person does not hold, which is the state they are in by then.
    /// </remarks>
    private async Task<PersonalTask?> ReadBackAsync(
        MailUserId user,
        PersonalTaskId task,
        Task<PersonalTaskChangeOutcome> change,
        CancellationToken cancellationToken) =>
        await change is PersonalTaskChangeOutcome.Applied
            ? await this.store.FindAsync(user, task, cancellationToken)
            : null;

    /// <summary>Reduces what a caller asked for to a page size this deployment serves.</summary>
    private static int Bounded(int? pageSize) => pageSize switch
    {
        null or < 1 => DefaultPageSize,
        > MaximumPageSize => MaximumPageSize,
        var asked => asked.Value,
    };
}
