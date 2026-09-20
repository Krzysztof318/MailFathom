// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;

namespace MailFathom.Domain.Tasks;

/// <summary>States one thing a person owes, entered by them or read out of their mail.</summary>
/// <remarks>
/// <para>
/// It is native to this deployment and stored beside everything else: no external task-management protocol is spoken,
/// chosen, or synchronized against, so what a person sees here is what this database holds. It is per user, on the
/// <c>(user, identifier)</c> axis every account reference already uses, and it is a personal list rather than a shared
/// board — nothing here assigns work to anybody but the person whose list it is.
/// </para>
/// <para>
/// The type is named for the list it belongs to rather than simply <c>Task</c>, because a domain type by that name
/// would collide at every point of use with the one every asynchronous method in this solution returns, and the
/// conventions refuse a name a reader has to recover from a namespace.
/// </para>
/// <para>
/// A task derived from mail is derived personal data with the same classification as the message behind it: the title
/// may be a sentence read out of a body. What it does not hold is a second copy of the mailbox — no body, no
/// attachment, no address — so the message is cited rather than repeated, and a task outlives that citation because
/// what a person owes does not stop being owed when the mail naming it is erased.
/// </para>
/// </remarks>
public sealed record PersonalTask
{
    /// <summary>The longest title stored, which is a line on a list rather than a note.</summary>
    public const int MaximumTitleLength = 200;

    private PersonalTask(
        PersonalTaskId id,
        MailUserId user,
        string title,
        DateOnly? dueOn,
        PersonalTaskOrigin origin,
        StoredEmailId? sourceMessage,
        bool isCompleted)
    {
        this.Id = id;
        this.User = user;
        this.Title = title;
        this.DueOn = dueOn;
        this.Origin = origin;
        this.SourceMessage = sourceMessage;
        this.IsCompleted = isCompleted;
    }

    /// <summary>Gets what addresses this task.</summary>
    public PersonalTaskId Id { get; }

    /// <summary>Gets the person whose list it is on.</summary>
    public MailUserId User { get; }

    /// <summary>Gets the line the list is drawn with.</summary>
    public string Title { get; }

    /// <summary>Gets the day it is due on, and <see langword="null" /> where nobody has said when.</summary>
    /// <remarks>
    /// A day rather than an instant, because what a person owes is owed on a date and never at a minute; a reminder
    /// against that date is what carries a time, and it is a separate record.
    /// </remarks>
    public DateOnly? DueOn { get; }

    /// <summary>Gets where the task came from, and so whether the person has committed to it.</summary>
    public PersonalTaskOrigin Origin { get; }

    /// <summary>Gets the message the task was read out of, and <see langword="null" /> where it names none.</summary>
    /// <remarks>
    /// Optional whichever origin the task carries: mail is the usual source of a proposal and never the only one, and
    /// a person entering a task by hand may still cite the thread they promised it in.
    /// </remarks>
    public StoredEmailId? SourceMessage { get; }

    /// <summary>Gets whether the person has done it.</summary>
    public bool IsCompleted { get; }

    /// <summary>Composes a task nobody has completed yet.</summary>
    /// <param name="id">What addresses the task.</param>
    /// <param name="user">The person whose list it is on.</param>
    /// <param name="title">The line the list is drawn with.</param>
    /// <param name="dueOn">The day it is due on, or <see langword="null" /> where nobody has said when.</param>
    /// <param name="origin">Where the task came from.</param>
    /// <param name="sourceMessage">The message it was read out of, or <see langword="null" /> where it names none.</param>
    /// <returns>A task that stands outstanding.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="title" /> is blank, when <paramref name="user" /> names nobody, or when <paramref name="id" /> is the struct default.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="origin" /> is not a declared origin, or when <paramref name="title" /> exceeds <see cref="MaximumTitleLength" />.</exception>
    /// <remarks>
    /// The user has to be a named one, because a row written under the unspecified identity would belong to nobody:
    /// unreachable by any read and uncollected by any erasure.
    /// </remarks>
    public static PersonalTask Compose(
        PersonalTaskId id,
        MailUserId user,
        string title,
        DateOnly? dueOn,
        PersonalTaskOrigin origin,
        StoredEmailId? sourceMessage)
    {
        Validate(id, user, origin);

        return new PersonalTask(
            id,
            user,
            Bounded(title, nameof(title)),
            dueOn,
            origin,
            sourceMessage,
            isCompleted: false);
    }

    /// <summary>Restores a task this deployment already kept, with the origin and completion it was stored under.</summary>
    /// <param name="id">What addresses the task.</param>
    /// <param name="user">The person whose list it is on.</param>
    /// <param name="title">The line the list is drawn with.</param>
    /// <param name="dueOn">The day it is due on, or <see langword="null" /> where nobody has said when.</param>
    /// <param name="origin">Where the task came from.</param>
    /// <param name="sourceMessage">The message it was read out of, or <see langword="null" /> where it names none.</param>
    /// <param name="isCompleted">Whether the person has done it.</param>
    /// <returns>The task as it stands.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="title" /> is blank, when <paramref name="user" /> names nobody, or when <paramref name="id" /> is the struct default.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="origin" /> is not a declared origin, or when <paramref name="title" /> exceeds <see cref="MaximumTitleLength" />.</exception>
    /// <remarks>
    /// It validates exactly what <see cref="Compose" /> validates rather than trusting the store, because a row read
    /// back is input from outside this process however it got there. The two things it takes that composing does not
    /// are the store's to say and never a producer's: whether the task has been completed, and whether a proposal has
    /// since been accepted.
    /// </remarks>
    public static PersonalTask Restore(
        PersonalTaskId id,
        MailUserId user,
        string title,
        DateOnly? dueOn,
        PersonalTaskOrigin origin,
        StoredEmailId? sourceMessage,
        bool isCompleted)
    {
        Validate(id, user, origin);

        return new PersonalTask(
            id,
            user,
            Bounded(title, nameof(title)),
            dueOn,
            origin,
            sourceMessage,
            isCompleted);
    }

    /// <summary>States the task as the person has just edited it.</summary>
    /// <param name="title">The line the list is to be drawn with from now on.</param>
    /// <param name="dueOn">The day it is due on, or <see langword="null" /> where the person took the date off it.</param>
    /// <returns>The task as it now stands.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="title" /> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="title" /> exceeds <see cref="MaximumTitleLength" />.</exception>
    /// <remarks>
    /// Only the two values an edit is about, because the rest of the record is not the editor's to state: the identity
    /// and the person are what address the task, the origin moves by accepting a proposal rather than by typing, the
    /// completion moves by doing the thing, and the citation records where the task came from rather than what it is
    /// now about. An edit that could rewrite any of those would let a person turn a proposal into a commitment, or
    /// point one at a message it was never read out of, through the route that renames it.
    /// </remarks>
    public PersonalTask Revise(string title, DateOnly? dueOn) => new(
        this.Id,
        this.User,
        Bounded(title, nameof(title)),
        dueOn,
        this.Origin,
        this.SourceMessage,
        this.IsCompleted);

    /// <summary>Refuses the identities and the origin that no task can be built from, whether composed or restored.</summary>
    private static void Validate(PersonalTaskId id, MailUserId user, PersonalTaskOrigin origin)
    {
        if (!id.IsSpecified)
        {
            throw new ArgumentException("A task is addressed by a specified identifier.", nameof(id));
        }

        if (!user.IsSpecified)
        {
            throw new ArgumentException(
                "A task is owed by a named user, so it is never held under the unspecified one.",
                nameof(user));
        }

        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin), origin, "A task carries a declared origin.");
        }
    }

    private static string Bounded(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        var trimmed = value.Trim();

        ArgumentOutOfRangeException.ThrowIfGreaterThan(trimmed.Length, MaximumTitleLength, parameterName);

        return trimmed;
    }
}
