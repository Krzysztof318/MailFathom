// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations.Convergence;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Mail.Mutations;

/// <summary>Keeps the durable record of every change MailFathom asked a mail server to make.</summary>
/// <remarks>
/// <para>
/// The idempotency identity is the email occurrence, the requester, and the mutation together, and it is enforced by a
/// unique constraint rather than by this contract declining to write. Two callers asking for the same change at the
/// same moment both reach the database, and one of them loses there; a check-then-insert would let both through the
/// window between the two statements, which is the window the crash-safety of everything above depends on being closed.
/// </para>
/// <para>
/// Writes take the caller's session because a mutation record is written alongside whatever else the caller is
/// committing. Reads take none, because a read joins no transaction.
/// </para>
/// </remarks>
public interface IMailboxMutationRecordStore
{
    /// <summary>Writes the intent down, or reads back the record that already holds this idempotency identity.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="request">The change that was asked for.</param>
    /// <param name="cancellationToken">Cancels the write or the read that follows a losing insert.</param>
    /// <returns>The record for this request, whether this call created it or another one did.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> or <paramref name="request" /> is <see langword="null" />.</exception>
    /// <remarks>The record starts at <see cref="MailboxMutationStage.Recorded" /> with no attempt counted, so opening one performs nothing by itself.</remarks>
    Task<MailboxMutationRecord> OpenAsync(
        IPersistenceSession session,
        MailboxMutationRequest request,
        CancellationToken cancellationToken);

    /// <summary>Reports whether one local email has ever had a mutation of a given kind asked for by a given kind of requester.</summary>
    /// <param name="storedEmailId">The local email, which is the identity that survives the email being moved.</param>
    /// <param name="mutation">The change asked for.</param>
    /// <param name="origin">The kind of act that asked.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns><see langword="true" /> when at least one such record exists, whatever stage it reached.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="mutation" /> is unspecified.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="origin" /> is not a declared origin.</exception>
    /// <remarks>
    /// <para>
    /// It answers a question the idempotency identity deliberately cannot: whether this email was ever moved by this kind
    /// of requester, rather than whether one particular request has already been made. The identity is keyed to the
    /// occurrence and to what asked, so a message that has since moved is a new occurrence and a requester whose terms
    /// changed is a new requester — both of which ask afresh, which is right for a retry and wrong for deciding whether
    /// somebody has since undone the change.
    /// </para>
    /// <para>
    /// Every stage counts, including an abandoned one. What the caller is establishing is that MailFathom has already
    /// acted on this email once, and a change that was attempted and given up on is still a change the owner may have
    /// seen and reversed.
    /// </para>
    /// </remarks>
    Task<bool> HasRecordAsync(
        StoredEmailId storedEmailId,
        MailboxMutation mutation,
        MailboxMutationOrigin origin,
        CancellationToken cancellationToken);

    /// <summary>Counts one attempt against the record before that attempt is made.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="recordId">The record to count against.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The number this attempt is, counting from one.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no record carries <paramref name="recordId" />.</exception>
    Task<int> CountAttemptAsync(
        IPersistenceSession session,
        MailboxMutationRecordId recordId,
        CancellationToken cancellationToken);

    /// <summary>Moves the record to <see cref="MailboxMutationStage.PlacementIssued" /> and states what the command will leave behind.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="recordId">The record to advance.</param>
    /// <param name="requiresSourceRemoval"><see langword="true" /> when the command leaves the source in place and a separate removal will be owed.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the stage and the answer are written.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no record carries <paramref name="recordId" />, or when the record has already passed this stage.</exception>
    /// <remarks>
    /// It is a transition of its own rather than a flag on <see cref="AdvanceAsync" />, because this is the only stage
    /// the answer is knowable at and the only one it means anything for. Writing both in one transaction is what stops
    /// a crash between them from leaving a placement whose obligation nobody recorded.
    /// </remarks>
    Task RecordPlacementIssuedAsync(
        IPersistenceSession session,
        MailboxMutationRecordId recordId,
        bool requiresSourceRemoval,
        CancellationToken cancellationToken);

    /// <summary>Moves the record to a later stage, recording the placement where the server named one.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="recordId">The record to advance.</param>
    /// <param name="stage">The stage the mutation has now reached.</param>
    /// <param name="placement">Where the server said it put the email, or <see langword="null" /> to leave the recorded placement alone.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the stage is written.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no record carries <paramref name="recordId" />, or when the record has already reached a terminal stage or a later one than <paramref name="stage" />.</exception>
    /// <remarks>A stage only ever moves forward, so a late write from a lost attempt cannot pull a mutation back to a stage a retry has already passed.</remarks>
    Task AdvanceAsync(
        IPersistenceSession session,
        MailboxMutationRecordId recordId,
        MailboxMutationStage stage,
        RemoteEmailPlacement? placement,
        CancellationToken cancellationToken);

    /// <summary>Records the failure the last attempt ended in, without moving the stage.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="recordId">The record the attempt belonged to.</param>
    /// <param name="failure">The code identifying what ended the attempt.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the failure is written.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no record carries <paramref name="recordId" />.</exception>
    /// <remarks>The stage stays where the sequence actually got to, which is what a resumed attempt reads; the failure says why it got no further.</remarks>
    Task RecordFailureAsync(
        IPersistenceSession session,
        MailboxMutationRecordId recordId,
        MailFathomErrorCode failure,
        CancellationToken cancellationToken);

    /// <summary>Reads the mutations of one account that have not completed, with the folder binding each was recorded against.</summary>
    /// <param name="accountId">The account whose mutations are read.</param>
    /// <param name="limit">The greatest number of records to return.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The outstanding records, oldest first, at most <paramref name="limit" /> of them.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="limit" /> is not positive.</exception>
    /// <remarks>
    /// <para>
    /// An abandoned mutation is part of the answer rather than excluded from it. Reaching a terminal failed stage is
    /// what stops a change being retried, and it would be worth nothing if it also stopped the change being seen: the
    /// operator's question is which changes are in flight and which are stuck, and a mutation nothing will attempt again
    /// is the second kind. Only a completed one leaves this answer.
    /// </para>
    /// <para>
    /// Oldest first, because the answer starts with whatever has been outstanding longest. It is bounded like every
    /// other public query, and convergence treats the bound as a page it comes back for rather than as a cut.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<OutstandingMailboxMutation>> ReadOutstandingAsync(
        MailAccountId accountId,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Counts one account's uncompleted mutations by kind and by where in its lifecycle each one stands.</summary>
    /// <param name="accountId">The account whose mutations are counted.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>One entry per kind and lifecycle that has at least one record, in no particular order.</returns>
    /// <remarks>
    /// <para>
    /// It is an aggregate rather than a count of what <see cref="ReadOutstandingAsync" /> returned, because that read is
    /// bounded and a bounded count is wrong exactly when it matters — the moment an account has more stuck changes than
    /// one pass looks at is the moment somebody needs the real number.
    /// </para>
    /// <para>
    /// The database groups and counts, so the answer is a handful of rows however many mutations the account has: at
    /// most one per permitted mutation per lifecycle. Nothing derived from a message takes part in it.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<MailboxMutationLifecycleCount>> ReadLifecycleCountsAsync(
        MailAccountId accountId,
        CancellationToken cancellationToken);
}
