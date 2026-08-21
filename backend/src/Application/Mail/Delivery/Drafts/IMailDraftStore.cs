// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Mail.Delivery.Drafts;

/// <summary>Keeps the durable account of every draft this deployment holds and every copy of one it appended.</summary>
/// <remarks>
/// <para>
/// A draft's state is written down before the mail server is touched and advanced as each command is answered, which is
/// the same discipline the outgoing record and the mutation record follow and for the same reason: a replacement is two
/// commands with a crash window between them, and the record is what a resumed attempt reads instead of inspecting a
/// folder afterwards.
/// </para>
/// <para>
/// The copies are a list because a revision that has not finished leaves two of them standing. What decides which is
/// which is the revision number rather than an ordering, so a resumed attempt reaches the same answer whichever order
/// the rows come back in.
/// </para>
/// <para>
/// Nothing here carries mail content. A folder, an alias, a UID, an identity MailFathom minted, and the addresses a
/// promotion would need are what a row holds, and the message stays in the content store.
/// </para>
/// </remarks>
public interface IMailDraftStore
{
    /// <summary>Writes down a new draft, at the revision its first stored message is.</summary>
    /// <param name="session">The session the write joins, which is the one the message is stored in.</param>
    /// <param name="accountId">The account the draft belongs to.</param>
    /// <param name="author">The authored act that wrote it.</param>
    /// <param name="recipients">The people it is addressed to, which may be nobody, each with where its address came from.</param>
    /// <param name="mimeByteLength">How many bytes the stored message is.</param>
    /// <param name="composedAt">When it was written down.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The draft as the write left it, at <see cref="MailDraftStage.Composed" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" />, <paramref name="author" />, or <paramref name="recipients" /> is <see langword="null" />.</exception>
    Task<MailDraftRecord> OpenAsync(
        IPersistenceSession session,
        MailAccountId accountId,
        OutgoingEmailRequester author,
        IReadOnlyList<MailDraftRecipient> recipients,
        long mimeByteLength,
        DateTimeOffset composedAt,
        CancellationToken cancellationToken);

    /// <summary>Advances a draft to a new revision, before any command replacing its copy goes out.</summary>
    /// <param name="session">The session the write joins, which is the one the new message is stored in.</param>
    /// <param name="draftId">The draft being revised.</param>
    /// <param name="recipients">The people the new revision is addressed to, each with where its address came from.</param>
    /// <param name="mimeByteLength">How many bytes the new stored message is.</param>
    /// <param name="revisedAt">When the revision was written down.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The draft as the write left it, with the copy it replaces now superseded.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> or <paramref name="recipients" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no draft is held under <paramref name="draftId" />, or when it has already been discarded.</exception>
    /// <remarks>
    /// It writes nothing about the mail server and issues no command. What it does is make the intent durable — this is
    /// now revision <em>n</em>, and the copy carrying revision <em>n-1</em> is one to take back out — so a process that
    /// dies before either command still leaves the previous copy standing and nameable, which is one draft rather than
    /// none.
    /// </remarks>
    Task<MailDraftRecord> ReviseAsync(
        IPersistenceSession session,
        MailDraftId draftId,
        IReadOnlyList<MailDraftRecipient> recipients,
        long mimeByteLength,
        DateTimeOffset revisedAt,
        CancellationToken cancellationToken);

    /// <summary>Reads one draft with its copies, or answers that nothing is held under that identifier.</summary>
    /// <param name="draftId">The draft to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The draft, or <see langword="null" /> when none is held.</returns>
    Task<MailDraftRecord?> FindAsync(MailDraftId draftId, CancellationToken cancellationToken);

    /// <summary>Reads the draft a promotion wrote one outgoing record for.</summary>
    /// <param name="outgoingEmailId">The record the promotion produced.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The draft, or <see langword="null" /> when that send came from no draft.</returns>
    /// <remarks>
    /// It is what turns a delivered send back into the draft it came from, so the copy in the drafts folder goes when
    /// the message actually left rather than when somebody asked for it to.
    /// </remarks>
    Task<MailDraftRecord?> FindPromotedToAsync(
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken);

    /// <summary>Reads the drafts of one account that still owe the mail server something.</summary>
    /// <param name="accountId">The account whose drafts are read.</param>
    /// <param name="maxCount">The greatest number of drafts to answer with.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The drafts with outstanding work, oldest revision first, bounded by <paramref name="maxCount" />.</returns>
    /// <remarks>
    /// It is the resume path. An account whose drafts are all settled answers nothing, so a pass over it costs one
    /// bounded query rather than a session.
    /// </remarks>
    Task<IReadOnlyList<MailDraftRecord>> ReadOutstandingAsync(
        MailAccountId accountId,
        int maxCount,
        CancellationToken cancellationToken);

    /// <summary>Writes down that an append of the current revision is about to be issued, before the command goes out.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="draftId">The draft being appended.</param>
    /// <param name="destination">The folder binding the copy is appended to.</param>
    /// <param name="appendedAt">When the append was issued.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the row is durable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> or <paramref name="destination" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no draft is held under <paramref name="draftId" />, or when its current revision was already appended.</exception>
    Task RecordAppendIssuedAsync(
        IPersistenceSession session,
        MailDraftId draftId,
        MailFolderResolution destination,
        DateTimeOffset appendedAt,
        CancellationToken cancellationToken);

    /// <summary>Writes down what the server said about the copy it accepted.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="draftId">The draft that was appended.</param>
    /// <param name="copy">What the server named, which may name no placement at all.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the copy is confirmed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> or <paramref name="copy" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no copy of the current revision is awaiting confirmation.</exception>
    Task RecordAppendConfirmedAsync(
        IPersistenceSession session,
        MailDraftId draftId,
        AppendedMailCopy copy,
        CancellationToken cancellationToken);

    /// <summary>Writes down what became of one copy once nothing more will be asked of it.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="draftId">The draft the copy belongs to.</param>
    /// <param name="revision">Which revision's copy it is.</param>
    /// <param name="stage">Whether the copy was taken back out or left as the owner's.</param>
    /// <param name="settledAt">When it stopped standing.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the copy is settled.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="stage" /> is not an ending a copy may reach.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no copy of that revision is held.</exception>
    /// <remarks>
    /// The two endings are separate answers rather than one. <see cref="MailDraftCopyStage.Withdrawn" /> says the folder
    /// no longer holds the copy, and <see cref="MailDraftCopyStage.Abandoned" /> says nothing will touch it again —
    /// writing the first where the second is true would be MailFathom claiming a message it cannot reach is gone.
    /// </remarks>
    Task RecordCopySettledAsync(
        IPersistenceSession session,
        MailDraftId draftId,
        int revision,
        MailDraftCopyStage stage,
        DateTimeOffset settledAt,
        CancellationToken cancellationToken);

    /// <summary>Writes down that this draft has been given up, before its copies are taken out of the folder.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="draftId">The draft being given up.</param>
    /// <param name="discardedAt">When it was given up.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the row says so.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no draft is held under <paramref name="draftId" />.</exception>
    /// <remarks>
    /// The row outlives the decision on purpose. Removing it first would lose the only thing naming the copies, and a
    /// copy nothing can name is a message left in the owner's drafts folder for good.
    /// </remarks>
    Task RecordDiscardedAsync(
        IPersistenceSession session,
        MailDraftId draftId,
        DateTimeOffset discardedAt,
        CancellationToken cancellationToken);

    /// <summary>Writes down the outgoing record a promotion produced for this draft.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="draftId">The draft that was promoted.</param>
    /// <param name="outgoingEmailId">The record the send was written down as.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the draft names the send.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no draft is held under <paramref name="draftId" />.</exception>
    Task RecordPromotedAsync(
        IPersistenceSession session,
        MailDraftId draftId,
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken);

    /// <summary>Removes one draft and everything held under it.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="draftId">The draft to remove.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when nothing is held under that identifier.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The stored message and the copy rows go with it, which is what makes erasing a draft one act rather than three.
    /// A draft that is not held is not an error: the removal is the last step of a discard that may already have run.
    /// </remarks>
    Task RemoveAsync(IPersistenceSession session, MailDraftId draftId, CancellationToken cancellationToken);

    /// <summary>Records that the tracked copy stopped being one MailFathom may replace or remove.</summary>
    /// <param name="draftId">The draft whose copy went out of reach.</param>
    /// <param name="reason">Which fact took it out of reach.</param>
    /// <param name="observedAt">When the attempt found it out.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the divergence is on the record.</returns>
    /// <remarks>
    /// It takes no session and moves no stage, for the reason a filing failure does not: the draft itself is unharmed
    /// and its author's next edit still has to be stored, so this is written beside the draft rather than over it.
    /// </remarks>
    Task RecordDivergenceAsync(
        MailDraftId draftId,
        MailDraftDivergenceReason reason,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken);

    /// <summary>Records why the last attempt against the mail server did not leave this draft where it belongs.</summary>
    /// <param name="draftId">The draft whose copy could not be settled.</param>
    /// <param name="failure">The code identifying what ended the attempt.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the code is on the record.</returns>
    Task RecordFailureAsync(
        MailDraftId draftId,
        MailFathomErrorCode failure,
        CancellationToken cancellationToken);
}
