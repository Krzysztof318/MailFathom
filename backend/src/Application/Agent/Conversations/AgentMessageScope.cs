// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Streaming;
using MailFathom.Domain.Calendar;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Agent.Conversations;

/// <summary>What one question was asked about: a kind from the closed set, and the object it names.</summary>
/// <remarks>
/// <para>
/// It is recorded per message rather than per conversation, because the context chip the client draws is cleared and
/// re-set as a conversation goes on: the second question in one conversation is routinely about something else, and a
/// scope kept on the conversation would say the whole history was asked under whichever object came last.
/// </para>
/// <para>
/// <strong>It names an object and carries none of it.</strong> A thread's subject, an event's title, and a run's
/// answer are all mail-derived and none of them is here — what is stored is the identifier, so what the scope
/// discloses to a reader of the row is that a question was asked about something rather than what that something says.
/// The scope is nonetheless part of the same sensitive record as the message it belongs to: which thread somebody
/// asked about is itself about them.
/// </para>
/// <para>
/// The constructor is public and does the checking, because this is read back out of a stored document as well as
/// composed in process, and a type reachable only through its factories cannot be deserialized at all. The factories
/// beside it are how it is composed here: each takes the identity its kind actually names, so a caller cannot pair a
/// calendar event's identifier with a thread.
/// </para>
/// </remarks>
public sealed record AgentMessageScope
{
    /// <summary>Initializes a scope, which is how one is read back out of a stored document.</summary>
    /// <param name="kind">Which kind of thing the question was asked about.</param>
    /// <param name="subject">The object of that kind, and <see langword="null" /> for a question about everything.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="kind" /> is not a declared member.</exception>
    /// <exception cref="ArgumentException">Thrown when a kind that names an object carries none, when a question about everything names one, or when the object is the empty UUID.</exception>
    public AgentMessageScope(AgentScopeKind kind, Guid? subject)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "A scope names a declared kind of thing.");
        }

        if (kind is AgentScopeKind.Mailbox && subject is not null)
        {
            throw new ArgumentException(
                "A question about the whole mailbox names no object, because there is no narrower thing it was asked about.",
                nameof(subject));
        }

        if (kind is not AgentScopeKind.Mailbox && subject is null)
        {
            throw new ArgumentException(
                $"A question scoped to a {kind} names the one it was asked about.",
                nameof(subject));
        }

        if (subject == Guid.Empty)
        {
            throw new ArgumentException("A scope cannot name the empty identifier.", nameof(subject));
        }

        this.Kind = kind;
        this.Subject = subject;
    }

    /// <summary>Gets which kind of thing the question was asked about.</summary>
    public AgentScopeKind Kind { get; }

    /// <summary>Gets the object the question was asked about, and <see langword="null" /> where it was asked about everything.</summary>
    public Guid? Subject { get; }

    /// <summary>The scope of a question asked with nothing narrower named.</summary>
    /// <returns>The mailbox-wide scope.</returns>
    public static AgentMessageScope Mailbox() => new(AgentScopeKind.Mailbox, subject: null);

    /// <summary>The scope of a question asked about one conversation of mail.</summary>
    /// <param name="thread">The thread the question is about.</param>
    /// <returns>The scope naming that thread.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="thread" /> is the struct default.</exception>
    public static AgentMessageScope Thread(EmailThreadId thread) =>
        new(AgentScopeKind.Thread, thread.Value);

    /// <summary>The scope of a question asked about one entry in the person's calendar.</summary>
    /// <param name="calendarEvent">The event the question is about.</param>
    /// <returns>The scope naming that event.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="calendarEvent" /> is the struct default.</exception>
    public static AgentMessageScope CalendarEvent(CalendarEventId calendarEvent) =>
        new(AgentScopeKind.CalendarEvent, calendarEvent.Value);

    /// <summary>The scope of a question carrying on from a Discover run's answer.</summary>
    /// <param name="run">The run the question carries on from.</param>
    /// <returns>The scope naming that run.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="run" /> is the struct default.</exception>
    public static AgentMessageScope DiscoveryRun(DiscoveryRunId run) =>
        new(AgentScopeKind.DiscoveryRun, run.Value);
}
