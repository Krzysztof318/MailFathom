// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Threads;

/// <summary>Places one stored email in the conversation its message identifiers name.</summary>
/// <remarks>
/// <para>
/// Membership is decided from <c>Message-ID</c>, <c>In-Reply-To</c> and <c>References</c> and from nothing else. A
/// subject is not evidence: <c>Invoice</c> arrives from four unrelated senders in a month, and a fallback that closed a
/// conversation on a normalized subject would merge exchanges that never touched. Over-linking is the failure a reader
/// cannot detect, because nothing in what they are shown says two halves of it were never one; under-linking is visible
/// and correct, so a client that drops <c>References</c> leaves a conversation split rather than guessed back together.
/// </para>
/// <para>
/// The whole placement happens inside the caller's transaction. Two callers reach it — the arrival pipeline, in the
/// transaction that commits the message, and re-derivation, which re-reads a stored message's own MIME — and both need
/// the placement committed with the columns it was decided from, so no message is ever readable while belonging to
/// nothing.
/// </para>
/// <para>
/// It is idempotent by construction. Every step reads the state it would write and converges on it: a message assembled
/// twice reaches the same conversation, an identifier already bound is not bound again, and a reply relation already
/// recorded is left alone. That is what makes re-deriving a scope whose conversations are assembled change nothing.
/// </para>
/// <para>
/// A race between two arrivals binding one identifier for the first time is resolved by the database rather than here.
/// Both read nothing and both bind, and the loser violates the store's uniqueness — which the retry resolves by
/// re-reading what the winner assembled and joining it.
/// </para>
/// </remarks>
public sealed class EmailThreadAssembly
{
    /// <summary>How far a cycle check walks a reply chain before it treats the chain as unusable.</summary>
    /// <remarks>
    /// The walk terminates on its own for any chain this assembly built, because a relation that would close a cycle is
    /// never written. The ceiling is against a chain no longer entirely of its making, where walking forever would hang
    /// the transaction that stores a message rather than refuse one edge of it.
    /// </remarks>
    private const int MaximumReplyChainWalk = 1_000;

    private readonly IEmailThreadStore store;

    /// <summary>Initializes the assembly.</summary>
    /// <param name="store">The durable conversation state, written in the caller's transaction.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="store" /> is <see langword="null" />.</exception>
    public EmailThreadAssembly(IEmailThreadStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        this.store = store;
    }

    /// <summary>Places the email, merging any conversations its identifiers prove were always one.</summary>
    /// <param name="session">The transaction the placement is part of.</param>
    /// <param name="account">The account whose mail the email is, named by its owner and its identifier.</param>
    /// <param name="email">The email to place, with the identifiers its headers carried.</param>
    /// <param name="currentThreadId">The conversation the email already belongs to, or <see langword="null" /> when none.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The conversation the email now belongs to.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="email" /> is <see langword="null" />.</exception>
    public async Task<EmailThreadId> AssembleAsync(
        IPersistenceSession session,
        MailAccountIdentity account,
        ThreadedEmail email,
        EmailThreadId? currentThreadId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);

        var identifiers = IdentifiersOf(email);
        var bindings = identifiers.Count == 0
            ? []
            : await this.store.FindBindingsAsync(session, account, identifiers, cancellationToken);

        var threadId = await this.ThreadOfAsync(session, account, bindings, currentThreadId, cancellationToken);

        await this.BindMissingAsync(session, account, identifiers, bindings, threadId, cancellationToken);

        var answered = await this.AnsweredMessageAsync(session, threadId, email, cancellationToken);

        await this.store.PlaceAsync(session, email.StoredEmailId, threadId, answered, cancellationToken);
        await this.PlaceAnswersAsync(session, threadId, email, cancellationToken);

        return threadId;
    }

    /// <summary>Reduces the email's three headers to the distinct identifiers it is bound under.</summary>
    /// <remarks>
    /// The message's own identifier is bound with the ones it refers to, which is what lets a reply stored later find it.
    /// The three arrive already normalized and bounded, so nothing is re-checked here.
    /// </remarks>
    private static IReadOnlyList<string> IdentifiersOf(ThreadedEmail email) =>
    [
        .. new[] { email.InternetMessageId, email.AnsweredInternetMessageId }
            .Concat(email.ReferencedInternetMessageIds)
            .Where(identifier => !string.IsNullOrEmpty(identifier))
            .Select(identifier => identifier!)
            .Distinct(StringComparer.Ordinal),
    ];

    /// <summary>Finds the conversation the bindings name, starting one or merging several as they require.</summary>
    /// <remarks>
    /// A message whose identifiers name no conversation this account holds starts one, including a message that carried
    /// no usable identifier at all — nothing can ever join such a message, so its conversation is one of one. It keeps
    /// the conversation it already has rather than starting a second, which is what makes re-deriving a message with no
    /// identifiers change nothing.
    /// </remarks>
    private async Task<EmailThreadId> ThreadOfAsync(
        IPersistenceSession session,
        MailAccountIdentity account,
        IReadOnlyList<EmailThreadBinding> bindings,
        EmailThreadId? currentThreadId,
        CancellationToken cancellationToken)
    {
        var named = bindings
            .DistinctBy(binding => binding.ThreadId)
            .OrderBy(binding => binding.ThreadAssembledAt)
            .ThenBy(binding => binding.ThreadId.Value)
            .ToArray();

        if (named.Length == 0)
        {
            return currentThreadId ?? await this.store.StartThreadAsync(session, account, cancellationToken);
        }

        var surviving = named[0].ThreadId;

        // The earlier conversation survives, so the identifier a tool published first stays the one this exchange is
        // known by. Every merged conversation keeps a record naming the survivor, which is what makes an identifier
        // published before the merge resolve afterwards.
        if (named.Length > 1)
        {
            await this.store.MergeThreadsAsync(
                session,
                [.. named.Skip(1).Select(binding => binding.ThreadId)],
                surviving,
                cancellationToken);
        }

        return surviving;
    }

    /// <summary>Binds every identifier the email carries that this account did not bind already.</summary>
    private async Task BindMissingAsync(
        IPersistenceSession session,
        MailAccountIdentity account,
        IReadOnlyList<string> identifiers,
        IReadOnlyList<EmailThreadBinding> bindings,
        EmailThreadId threadId,
        CancellationToken cancellationToken)
    {
        var bound = bindings.Select(binding => binding.Identifier).ToHashSet(StringComparer.Ordinal);
        var missing = identifiers.Where(identifier => !bound.Contains(identifier)).ToArray();

        if (missing.Length > 0)
        {
            await this.store.BindIdentifiersAsync(session, account, missing, threadId, cancellationToken);
        }
    }

    /// <summary>Resolves the identifier this message answers to a message the conversation holds.</summary>
    /// <remarks>
    /// <para>
    /// The search is inside the conversation rather than across the account, which is exact rather than an optimization:
    /// the identifier this message answers was bound to this conversation a moment ago, so any stored message carrying
    /// it is in this conversation by construction.
    /// </para>
    /// <para>
    /// One message identifier can name several stored rows, because the same message mirrored in two folders is two
    /// occurrences and two rows. The lowest identity wins, so two reads of one mailbox agree on which of them an answer
    /// hangs from.
    /// </para>
    /// </remarks>
    private async Task<StoredEmailId?> AnsweredMessageAsync(
        IPersistenceSession session,
        EmailThreadId threadId,
        ThreadedEmail email,
        CancellationToken cancellationToken)
    {
        if (email.AnsweredStoredEmailId is { } alreadyAnswered)
        {
            return alreadyAnswered;
        }

        if (email.AnsweredInternetMessageId is not { } answeredIdentifier)
        {
            return null;
        }

        var candidates = await this.store.FindByMessageIdentifierAsync(
            session,
            threadId,
            answeredIdentifier,
            cancellationToken);

        var answered = candidates
            .Where(candidate => candidate.StoredEmailId != email.StoredEmailId)
            .OrderBy(candidate => candidate.StoredEmailId.Value)
            .Select(candidate => (StoredEmailId?)candidate.StoredEmailId)
            .FirstOrDefault();

        return answered is { } parent
               && !await this.ClosesCycleAsync(session, email.StoredEmailId, parent, cancellationToken)
            ? parent
            : null;
    }

    /// <summary>Hangs the already-stored messages that answer this one from it, wherever nothing has yet.</summary>
    /// <remarks>
    /// A conversation does not arrive in order — a mailbox is walked newest first — so the message stored second is the
    /// one that closes an edge, in whichever direction is still open. Only a message hanging from nothing is claimed,
    /// so a second copy of a message arriving from a mirrored folder never re-parents a reply already placed.
    /// </remarks>
    private async Task PlaceAnswersAsync(
        IPersistenceSession session,
        EmailThreadId threadId,
        ThreadedEmail email,
        CancellationToken cancellationToken)
    {
        if (email.InternetMessageId is not { } identifier)
        {
            return;
        }

        var answers = await this.store.FindUnplacedAnswersAsync(session, threadId, identifier, cancellationToken);

        foreach (var answer in answers.Where(answer => answer.StoredEmailId != email.StoredEmailId))
        {
            if (!await this.ClosesCycleAsync(
                    session,
                    answer.StoredEmailId,
                    email.StoredEmailId,
                    cancellationToken))
            {
                await this.store.LinkAnswerAsync(
                    session,
                    answer.StoredEmailId,
                    email.StoredEmailId,
                    cancellationToken);
            }
        }
    }

    /// <summary>Answers whether hanging one message from another would close a loop in the reply relation.</summary>
    /// <remarks>
    /// A cycle is refused rather than written, because a conversation carrying one has no order: the walk that produces
    /// the published sequence would never reach an end. Two messages each naming the other as the one they answer is
    /// what a mail client with a broken threading implementation writes, and it arrives here as ordinary headers.
    /// </remarks>
    private async Task<bool> ClosesCycleAsync(
        IPersistenceSession session,
        StoredEmailId answerStoredEmailId,
        StoredEmailId answeredStoredEmailId,
        CancellationToken cancellationToken)
    {
        var ancestor = (StoredEmailId?)answeredStoredEmailId;

        for (var step = 0; step < MaximumReplyChainWalk && ancestor is { } current; step++)
        {
            if (current == answerStoredEmailId)
            {
                return true;
            }

            var walked = await this.store.FindThreadedEmailAsync(session, current, cancellationToken);
            ancestor = walked?.AnsweredStoredEmailId;
        }

        return ancestor is not null;
    }
}
