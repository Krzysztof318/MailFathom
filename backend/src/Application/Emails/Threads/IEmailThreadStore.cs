// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Threads;

/// <summary>The durable state one conversation is assembled out of, written in the caller's transaction.</summary>
/// <remarks>
/// <para>
/// Every method takes the session, because placing a message in a conversation belongs to the transaction that commits
/// the message. A placement committed separately would leave a window in which a message is readable and belongs to
/// nothing, and a crash inside that window would leave it there for good.
/// </para>
/// <para>
/// Every read is scoped to one conversation or to one account, deliberately. Assembly asks narrow questions — which
/// conversations these identifiers name, which message in this conversation carries that identifier — so a store that
/// handed back a whole conversation would make the cost of storing one message grow with the length of the exchange it
/// joins.
/// </para>
/// </remarks>
public interface IEmailThreadStore
{
    /// <summary>Reads which of the given identifiers this account already binds to a conversation.</summary>
    /// <param name="session">The transaction the reading is part of, so pending writes of the same session are visible.</param>
    /// <param name="account">The account whose conversations are searched.</param>
    /// <param name="identifiers">The message identifiers to resolve.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>One entry per identifier that is bound, and nothing for the ones that are not.</returns>
    Task<IReadOnlyList<EmailThreadBinding>> FindBindingsAsync(
        IPersistenceSession session,
        MailAccountIdentity account,
        IReadOnlyList<string> identifiers,
        CancellationToken cancellationToken);

    /// <summary>Starts a conversation for the account and reports its identity.</summary>
    /// <param name="session">The transaction the conversation is created in.</param>
    /// <param name="account">The account that owns it.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The new conversation's identity.</returns>
    Task<EmailThreadId> StartThreadAsync(
        IPersistenceSession session,
        MailAccountIdentity account,
        CancellationToken cancellationToken);

    /// <summary>Binds identifiers this account does not bind yet to one conversation.</summary>
    /// <param name="session">The transaction the bindings are written in.</param>
    /// <param name="account">The account the identifiers were seen in.</param>
    /// <param name="identifiers">The identifiers to bind, which the caller has established are unbound.</param>
    /// <param name="threadId">The conversation to bind them to.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    Task BindIdentifiersAsync(
        IPersistenceSession session,
        MailAccountIdentity account,
        IReadOnlyList<string> identifiers,
        EmailThreadId threadId,
        CancellationToken cancellationToken);

    /// <summary>Folds conversations into one, moving their mail and their identifiers onto the survivor.</summary>
    /// <param name="session">The transaction the merge is part of.</param>
    /// <param name="mergedThreadIds">The conversations that stop being their own.</param>
    /// <param name="survivingThreadId">The conversation that keeps its identity.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <remarks>
    /// A merged conversation keeps a record naming its survivor, so an identifier a tool published before the merge
    /// still resolves to the conversation it named rather than answering not-found.
    /// </remarks>
    Task MergeThreadsAsync(
        IPersistenceSession session,
        IReadOnlyList<EmailThreadId> mergedThreadIds,
        EmailThreadId survivingThreadId,
        CancellationToken cancellationToken);

    /// <summary>Reads the messages of one conversation that carry a given message identifier.</summary>
    /// <param name="session">The transaction the reading is part of.</param>
    /// <param name="threadId">The conversation to search.</param>
    /// <param name="internetMessageId">The identifier to match on.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The matching messages, which is more than one when the same message is mirrored in two folders.</returns>
    Task<IReadOnlyList<ThreadedEmail>> FindByMessageIdentifierAsync(
        IPersistenceSession session,
        EmailThreadId threadId,
        string internetMessageId,
        CancellationToken cancellationToken);

    /// <summary>Reads the messages of one conversation that answer an identifier and hang from nothing yet.</summary>
    /// <param name="session">The transaction the reading is part of.</param>
    /// <param name="threadId">The conversation to search.</param>
    /// <param name="answeredIdentifier">The identifier those messages name as the one they answer.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The messages waiting for the message that identifier names.</returns>
    Task<IReadOnlyList<ThreadedEmail>> FindUnplacedAnswersAsync(
        IPersistenceSession session,
        EmailThreadId threadId,
        string answeredIdentifier,
        CancellationToken cancellationToken);

    /// <summary>Reads one stored email as assembly sees it, or nothing when it is not stored.</summary>
    /// <param name="session">The transaction the reading is part of.</param>
    /// <param name="storedEmailId">The email to read.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The email, or <see langword="null" /> when nothing holds it.</returns>
    /// <remarks>What reads it is the walk that refuses to close a cycle in the reply relation.</remarks>
    Task<ThreadedEmail?> FindThreadedEmailAsync(
        IPersistenceSession session,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken);

    /// <summary>Records which conversation one email belongs to and which of its messages it answers.</summary>
    /// <param name="session">The transaction the placement is part of.</param>
    /// <param name="storedEmailId">The email being placed.</param>
    /// <param name="threadId">The conversation it belongs to.</param>
    /// <param name="answeredStoredEmailId">The message it answers, or <see langword="null" /> when it answers none held here.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    Task PlaceAsync(
        IPersistenceSession session,
        StoredEmailId storedEmailId,
        EmailThreadId threadId,
        StoredEmailId? answeredStoredEmailId,
        CancellationToken cancellationToken);

    /// <summary>Hangs one already-stored message from the message it answers.</summary>
    /// <param name="session">The transaction the relation is written in.</param>
    /// <param name="answerStoredEmailId">The message that answers.</param>
    /// <param name="answeredStoredEmailId">The message it answers.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    Task LinkAnswerAsync(
        IPersistenceSession session,
        StoredEmailId answerStoredEmailId,
        StoredEmailId answeredStoredEmailId,
        CancellationToken cancellationToken);
}
