// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Tasks;
using MailFathom.Domain.Access;
using MailFathom.Domain.Tasks;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Holds what a person owes in memory, ordered and scoped the way the persisted store is.</summary>
/// <remarks>
/// The order and the keyset boundary are reproduced rather than approximated, because they are what the use case above
/// reads a page against: a double that ordered by insertion would pass every paging test while the real store served a
/// different list. What it does not reproduce is the transaction, which is the persisted store's own subject.
/// </remarks>
internal sealed class InMemoryPersonalTaskStore : IPersonalTaskStore
{
    private readonly List<PersonalTask> tasks = [];

    /// <summary>Gets every task the store holds, for a test asserting what a write left behind.</summary>
    public IReadOnlyList<PersonalTask> Held => this.tasks;

    /// <summary>Gets the limit the last read was asked for, which is what a clamp is observable through.</summary>
    public int LastLimit { get; private set; }

    public Task AddAsync(PersonalTask task, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);

        this.tasks.Add(task);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PersonalTask>> ReadAsync(
        MailUserId user,
        PersonalTaskOrigin? origin,
        PersonalTaskCursor? after,
        int limit,
        CancellationToken cancellationToken)
    {
        this.LastLimit = limit;

        var ordered = this.tasks
            .Where(task => task.User == user)
            .Where(task => origin is not { } named || task.Origin == named)
            .OrderBy(task => task.DueOn is null)
            .ThenBy(task => task.DueOn)
            .ThenBy(task => task.Id.Value);

        var page = after is { } boundary
            ? ordered.Where(task => IsBeyond(task, boundary))
            : ordered;

        return Task.FromResult<IReadOnlyList<PersonalTask>>([.. page.Take(limit)]);
    }

    public Task<PersonalTask?> FindAsync(MailUserId user, PersonalTaskId task, CancellationToken cancellationToken) =>
        Task.FromResult(this.tasks.FirstOrDefault(held => held.User == user && held.Id == task));

    public Task<PersonalTaskChangeOutcome> ReviseAsync(PersonalTask revision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(revision);

        return Task.FromResult(this.Replace(
            revision.User,
            revision.Id,
            held => held.Revise(revision.Title, revision.DueOn, Announcing(revision))));
    }

    public Task<PersonalTaskChangeOutcome> AcceptAsync(
        MailUserId user,
        PersonalTaskId task,
        CancellationToken cancellationToken) =>
        Task.FromResult(this.Replace(
            user,
            task,
            held => PersonalTask.Restore(
                held.Id,
                held.User,
                held.Title,
                held.DueOn,
                Announcing(held),
                PersonalTaskOrigin.Asserted,
                held.SourceMessage,
                held.IsCompleted)));

    public Task<PersonalTaskChangeOutcome> SetCompletionAsync(
        MailUserId user,
        PersonalTaskId task,
        bool isCompleted,
        CancellationToken cancellationToken) =>
        Task.FromResult(this.Replace(
            user,
            task,
            held => PersonalTask.Restore(
                held.Id,
                held.User,
                held.Title,
                held.DueOn,
                Announcing(held),
                held.Origin,
                held.SourceMessage,
                isCompleted)));

    /// <summary>Restates what announces a held task, which a state change keeps rather than rewrites.</summary>
    private static TaskAnnouncement Announcing(PersonalTask task) => task.DueDayOffset is { } offset
        ? new TaskAnnouncement(offset, task.Reminders)
        : TaskAnnouncement.Silent;

    public Task<bool> EraseAsync(MailUserId user, PersonalTaskId task, CancellationToken cancellationToken) =>
        Task.FromResult(this.tasks.RemoveAll(held => held.User == user && held.Id == task) > 0);

    /// <summary>Reports whether a task stands after a boundary in the order this store serves.</summary>
    private static bool IsBeyond(PersonalTask task, PersonalTaskCursor boundary) => boundary.DueOn is { } dueOn
        ? task.DueOn is null || task.DueOn > dueOn || (task.DueOn == dueOn && task.Id.Value > boundary.Task.Value)
        : task.DueOn is null && task.Id.Value > boundary.Task.Value;

    /// <summary>Applies a change to one of a person's tasks, reporting what became of the request.</summary>
    private PersonalTaskChangeOutcome Replace(
        MailUserId user,
        PersonalTaskId task,
        Func<PersonalTask, PersonalTask> change)
    {
        var position = this.tasks.FindIndex(held => held.User == user && held.Id == task);

        if (position < 0)
        {
            return PersonalTaskChangeOutcome.NotFound;
        }

        this.tasks[position] = change(this.tasks[position]);

        return PersonalTaskChangeOutcome.Applied;
    }
}
