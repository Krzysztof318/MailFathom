// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Tasks;

namespace MailFathom.Application.Tasks;

/// <summary>Keeps what a person owes, native to this deployment.</summary>
/// <remarks>
/// Every reader and every producer goes through this port rather than the table, which is what keeps a proposal one
/// row: a stage that composed its own insert would write a second task when the person accepted the first, and two
/// producers deciding that separately is how a list starts showing the same commitment twice.
/// </remarks>
public interface IPersonalTaskStore
{
    /// <summary>Keeps one task.</summary>
    /// <param name="task">The task to keep.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>An operation that completes once the row is committed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="task" /> is <see langword="null" />.</exception>
    Task AddAsync(PersonalTask task, CancellationToken cancellationToken);

    /// <summary>Reads one bounded page of a person's tasks, soonest due first.</summary>
    /// <param name="user">The person whose list is read.</param>
    /// <param name="origin">The origin to read, or <see langword="null" /> for the whole list.</param>
    /// <param name="after">The position a continued walk reads beyond, or <see langword="null" /> for the first page.</param>
    /// <param name="limit">The greatest number of tasks the answer may hold.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The tasks, soonest due first, with the undated ones last, holding at most <paramref name="limit" />.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="origin" /> is not a declared origin, or when <paramref name="limit" /> is not positive.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="after" /> is the struct default, which names no position.</exception>
    /// <remarks>
    /// Naming an origin is what reads the proposals apart from the commitments, so a screen can offer what mail
    /// suggested without it standing on the list as something the person agreed to. Completed tasks are read with the
    /// outstanding ones, because whether a list shows what is done is the screen's decision and not the store's.
    /// The boundary is a position rather than an offset, so a task dated, completed, or accepted while somebody is
    /// paging neither shifts the window nor causes a row to be repeated or skipped. Whether the cursor was issued to
    /// this user is the caller's question rather than the store's: this reads the user it is given and nothing else.
    /// </remarks>
    Task<IReadOnlyList<PersonalTask>> ReadAsync(
        MailUserId user,
        PersonalTaskOrigin? origin,
        PersonalTaskCursor? after,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Turns one of a person's proposals into a task they owe.</summary>
    /// <param name="user">The person whose task is accepted, which is what scopes the write.</param>
    /// <param name="task">The task to accept.</param>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns>What became of the request.</returns>
    /// <remarks>
    /// It moves the row's origin rather than writing a second task, so a commitment keeps the identity it was derived
    /// under and whatever already points at it — a reminder, a citation — still points at it afterwards.
    /// </remarks>
    Task<PersonalTaskChangeOutcome> AcceptAsync(
        MailUserId user,
        PersonalTaskId task,
        CancellationToken cancellationToken);

    /// <summary>Puts one of a person's tasks into a stated completion state.</summary>
    /// <param name="user">The person whose task is changed, which is what scopes the write.</param>
    /// <param name="task">The task to change.</param>
    /// <param name="isCompleted">The completion state it is to stand in.</param>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns>What became of the request.</returns>
    /// <remarks>
    /// The user is part of the addressing rather than a filter applied after a lookup, so a task another person holds
    /// is not found rather than found and refused — which is what keeps the answer identical for one that does not
    /// exist at all.
    /// </remarks>
    Task<PersonalTaskChangeOutcome> SetCompletionAsync(
        MailUserId user,
        PersonalTaskId task,
        bool isCompleted,
        CancellationToken cancellationToken);

    /// <summary>Erases one of a person's tasks.</summary>
    /// <param name="user">The person whose task is erased, which is what scopes the write.</param>
    /// <param name="task">The task to erase.</param>
    /// <param name="cancellationToken">Cancels the erasure.</param>
    /// <returns><see langword="true" /> when a task was erased, and <see langword="false" /> when this user held none.</returns>
    /// <remarks>
    /// The row goes rather than being marked gone: a task nobody owes any more is not a record anything is owed about,
    /// and keeping one would leave a list whose every reader had to remember to exclude it. Erasing something already
    /// erased is the act the caller wanted rather than an error, so the answer says what happened and never what was
    /// asked for.
    /// </remarks>
    Task<bool> EraseAsync(MailUserId user, PersonalTaskId task, CancellationToken cancellationToken);
}
