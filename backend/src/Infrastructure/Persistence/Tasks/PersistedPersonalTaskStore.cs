// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Persistence;
using MailFathom.Application.Tasks;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Domain.Tasks;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Tasks;

/// <summary>Keeps what a person owes, in PostgreSQL.</summary>
/// <remarks>
/// <para>
/// Each write opens a session of its own rather than joining a caller's, because no caller has one: a task is written
/// by a person acting on a screen or by an enrichment run reporting on mail it has already stored, and composing the
/// task into that run's transaction would make a suggestion able to fail mail that was already committed.
/// </para>
/// <para>
/// The three state changes run under the ordinary commit policy, so the read that finds the row and the write that
/// moves it are one transaction and a writer that got there first is a retry rather than a failure. None of them can
/// collide on a constraint: a task has no natural key, so two replicas moving one row converge on the same state
/// rather than racing for it. The erasure uses neither, being a set-based delete that composes with nothing a caller
/// is holding.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this store.")]
[RequiresIntegrationCoverage]
internal sealed class PersistedPersonalTaskStore(
    MailFathomDbContext readContext,
    OptimisticConcurrencyRetryPolicy commitPolicy)
    : IPersonalTaskStore
{
    /// <inheritdoc />
    public Task AddAsync(PersonalTask task, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);

        return commitPolicy.CommitAsync(
            async (session, token) =>
            {
                var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, token);

                writeContext.PersonalTasks.Add(PersonalTaskMapping.ToEntity(task));
            },
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A read joins no transaction and takes no session, so it runs on the scoped context. The order is the order the
    /// index declares — one person's tasks, soonest due first, with the undated ones last and the identifier breaking
    /// a tie — which is also the order the list is drawn in.
    /// </remarks>
    public async Task<IReadOnlyList<PersonalTask>> ReadAsync(
        MailUserId user,
        PersonalTaskOrigin? origin,
        PersonalTaskCursor? after,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        if (origin is { } requested && !Enum.IsDefined(requested))
        {
            throw new ArgumentOutOfRangeException(nameof(origin), requested, "A task carries a declared origin.");
        }

        if (after is { Task.IsSpecified: false })
        {
            throw new ArgumentException(
                "A cursor continues a walk after a specified task, so the struct default names no position.",
                nameof(after));
        }

        var userValue = user.Value;
        var rows = readContext.PersonalTasks
            .AsNoTracking()
            .Where(task => task.UserId == userValue);

        if (origin is { } named)
        {
            rows = rows.Where(task => task.Origin == named);
        }

        if (after is { } boundary)
        {
            rows = ReadBeyond(rows, boundary);
        }

        var page = await rows
            .OrderBy(task => task.DueOn)
            .ThenBy(task => task.Id)
            .Take(limit)
            .ToArrayAsync(cancellationToken);

        return [.. page.Select(PersonalTaskMapping.ToPersonalTask)];
    }

    /// <inheritdoc />
    /// <remarks>
    /// A read joins no transaction and takes no session, so it runs on the scoped context, and the user is inside the
    /// predicate rather than checked after it.
    /// </remarks>
    public async Task<PersonalTask?> FindAsync(
        MailUserId user,
        PersonalTaskId task,
        CancellationToken cancellationToken)
    {
        var userValue = user.Value;
        var identifier = task.Value;

        var stored = await readContext.PersonalTasks
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.UserId == userValue && candidate.Id == identifier,
                cancellationToken);

        return stored is null ? null : PersonalTaskMapping.ToPersonalTask(stored);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Two columns rather than one, which is the only way this differs from the two state changes below it: the
    /// addressing, the session, and the commit policy are the same, and the revision supplies the row it is written
    /// against rather than being looked up from a second identity.
    /// </remarks>
    public Task<PersonalTaskChangeOutcome> ReviseAsync(PersonalTask revision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(revision);

        return commitPolicy.CommitAsync(
            (session, token) => StageAsync(
                session,
                revision.User,
                revision.Id,
                stored =>
                {
                    stored.Title = revision.Title;
                    stored.DueOn = revision.DueOn;
                },
                token),
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Accepting is one column moving on the row that already exists, which is what keeps a proposal and the
    /// commitment it became one task. A task already asserted is left alone and reported as applied, so accepting
    /// twice writes once.
    /// </remarks>
    public Task<PersonalTaskChangeOutcome> AcceptAsync(
        MailUserId user,
        PersonalTaskId task,
        CancellationToken cancellationToken) =>
        commitPolicy.CommitAsync(
            (session, token) => StageAsync(
                session,
                user,
                task,
                stored => stored.Origin = PersonalTaskOrigin.Asserted,
                token),
            cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// The user is part of the lookup rather than a check after it, which is what makes another person's task answer
    /// as one that does not exist.
    /// </remarks>
    public Task<PersonalTaskChangeOutcome> SetCompletionAsync(
        MailUserId user,
        PersonalTaskId task,
        bool isCompleted,
        CancellationToken cancellationToken) =>
        commitPolicy.CommitAsync(
            (session, token) => StageAsync(
                session,
                user,
                task,
                stored => stored.IsCompleted = isCompleted,
                token),
            cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// One set-based delete rather than a lookup and a delete: the user is inside the predicate, so a task another
    /// person holds is never matched and never has to be told apart from one that does not exist.
    /// </remarks>
    public async Task<bool> EraseAsync(
        MailUserId user,
        PersonalTaskId task,
        CancellationToken cancellationToken)
    {
        var userValue = user.Value;
        var identifier = task.Value;

        var erased = await readContext.PersonalTasks
            .Where(candidate => candidate.UserId == userValue && candidate.Id == identifier)
            .ExecuteDeleteAsync(cancellationToken);

        return erased > 0;
    }

    /// <summary>Narrows a person's tasks to the ones the order puts after a boundary.</summary>
    /// <remarks>
    /// The order is the day ascending with PostgreSQL's own <c>NULLS LAST</c> and the identifier breaking a tie, so
    /// continuing past a boundary is two cases rather than one. From a dated boundary the rest is every later day, the
    /// same day beyond that identifier, and the whole undated block that follows every date; a comparison against
    /// <c>NULL</c> yields <c>NULL</c> rather than true, which is why the undated block is named rather than left to
    /// the inequality. From an undated boundary the rest is the remainder of that block alone, because nothing in this
    /// order comes after it.
    /// </remarks>
    private static IQueryable<PersonalTaskEntity> ReadBeyond(
        IQueryable<PersonalTaskEntity> rows,
        PersonalTaskCursor boundary)
    {
        var boundaryId = boundary.Task.Value;

        if (boundary.DueOn is not { } boundaryDueOn)
        {
            return rows.Where(task => task.DueOn == null && task.Id > boundaryId);
        }

        return rows.Where(task => task.DueOn == null
            || task.DueOn > boundaryDueOn
            || (task.DueOn == boundaryDueOn && task.Id > boundaryId));
    }

    /// <summary>Finds one of a person's tasks inside the session and applies a change to it.</summary>
    /// <remarks>
    /// Every state change here has the same two halves — address the row through the user as well as the identifier,
    /// then move one column — so the addressing is written once and each caller supplies only the column it moves.
    /// Assigning a value the row already carries leaves the change tracker with nothing to write, which is what makes
    /// a repeated request a read rather than a second write.
    /// </remarks>
    private static async Task<PersonalTaskChangeOutcome> StageAsync(
        IPersistenceSession session,
        MailUserId user,
        PersonalTaskId task,
        Action<PersonalTaskEntity> change,
        CancellationToken cancellationToken)
    {
        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var userValue = user.Value;
        var identifier = task.Value;

        var stored = await writeContext.PersonalTasks.FirstOrDefaultAsync(
            candidate => candidate.UserId == userValue && candidate.Id == identifier,
            cancellationToken);

        if (stored is null)
        {
            return PersonalTaskChangeOutcome.NotFound;
        }

        change(stored);

        return PersonalTaskChangeOutcome.Applied;
    }
}
