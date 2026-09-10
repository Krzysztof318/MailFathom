// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations.Convergence;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
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
    /// <param name="heldUntil">The instant before which no convergence pass may take the record in hand, or <see langword="null" /> where it may be taken at once.</param>
    /// <param name="cancellationToken">Cancels the write or the read that follows a losing insert.</param>
    /// <returns>The record for this request, whether this call created it or another one did.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> or <paramref name="request" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>The record starts at <see cref="MailboxMutationStage.Recorded" /> with no attempt counted, so opening one performs nothing by itself.</para>
    /// <para>
    /// A hold is what makes a change withdrawable for a stated stretch after it was asked for, and it belongs to the
    /// record rather than to whoever asked: a client that closes, loses its network, or is put to sleep costs the
    /// mailbox nothing, because the window elapses and the record is taken in hand exactly as it would have been. It
    /// is honoured by <see cref="ReadOutstandingAsync" /> and lifted by <see cref="ReleaseAsync" />, and a record that
    /// already exists under this identity keeps the hold it was opened with rather than taking this call's.
    /// </para>
    /// </remarks>
    Task<MailboxMutationRecord> OpenAsync(
        IPersistenceSession session,
        MailboxMutationRequest request,
        DateTimeOffset? heldUntil,
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
    /// acted on this email once, and a change that was attempted and given up on is still a change the user may have
    /// seen and reversed.
    /// </para>
    /// </remarks>
    Task<bool> HasRecordAsync(
        StoredEmailId storedEmailId,
        MailboxMutation mutation,
        MailboxMutationOrigin origin,
        CancellationToken cancellationToken);

    /// <summary>Reads the records one user's own change carries, by the identities that change was answered with.</summary>
    /// <param name="user">The user the records must belong to.</param>
    /// <param name="recordIds">The records to read, in any order and with repetitions.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The records this user holds under those identities, ordered by when each was recorded, and empty where they hold none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="recordIds" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// The user is a parameter rather than something the caller checks afterwards, because it is what makes an
    /// identifier somebody else's record unreadable rather than merely unreported: a record that does not belong to the
    /// asking user is absent from the answer, so no timing or shape separates one that exists from one that never did.
    /// A record identity is generated rather than guessable, and this is what keeps that from being the only thing
    /// standing between two people's mail.
    /// </para>
    /// <para>
    /// Every stage is answered, completed and terminal ones included, because the caller is asking where its own change
    /// got to and the answer it is waiting for is the one that says the change is done.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<MailboxMutationRecord>> ReadAsync(
        MailUserId user,
        IReadOnlyList<MailboxMutationRecordId> recordIds,
        CancellationToken cancellationToken);

    /// <summary>Withdraws the user's changes among those named, wherever nothing has been asked of the mail server for one yet.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="user">The user the records must belong to.</param>
    /// <param name="recordIds">The records to withdraw.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Each named record as it now stands, unchanged where the change had already been attempted, and absent where this user holds no such record.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> or <paramref name="recordIds" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// A record past <see cref="MailboxMutationRecord.IsWithdrawable" /> is returned rather than refused, because the
    /// caller's question is what became of the change and "it had already gone out" is the answer to it. That also makes
    /// the call safe to repeat: withdrawing an already withdrawn record reports it withdrawn and writes nothing.
    /// </para>
    /// <para>
    /// The call takes the whole set rather than one record, because a caller withdrawing what it submitted as a batch
    /// withdraws it as one, and the commit this joins retries as a whole — so a call per record would pay the round trip
    /// again on every attempt.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<MailboxMutationRecord>> WithdrawAsync(
        IPersistenceSession session,
        MailUserId user,
        IReadOnlyList<MailboxMutationRecordId> recordIds,
        CancellationToken cancellationToken);

    /// <summary>Lifts the hold on the user's changes among those named, so the next convergence pass may take each in hand.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="recordIds">The records to release.</param>
    /// <param name="user">The user the records must belong to.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Each named record as it now stands, unchanged where nothing was holding it, and absent where this user holds no such record.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> or <paramref name="recordIds" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// It is the opposite half of <see cref="WithdrawAsync" /> and is shaped like it for the same reasons: a record
    /// nothing was holding is reported where it stands rather than refused, which is what makes the call safe to
    /// repeat, and the whole set is taken in one commit because a caller that submitted a batch releases it as one.
    /// Releasing changes no stage, so a record already withdrawn stays withdrawn and one already under way is
    /// untouched — what it removes is a wait, never a decision.
    /// </remarks>
    Task<IReadOnlyList<MailboxMutationRecord>> ReleaseAsync(
        IPersistenceSession session,
        MailUserId user,
        IReadOnlyList<MailboxMutationRecordId> recordIds,
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
    /// <param name="account">The account whose mutations are read.</param>
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
    /// <para>
    /// A record still inside the hold <see cref="OpenAsync" /> opened it under is absent from this answer, and it is
    /// absent rather than skipped afterwards so that a page is spent on work a pass can actually do. It counts as
    /// pending in <see cref="ReadLifecycleCountsAsync" /> throughout, which is what it is: a change asked for that
    /// nothing has started.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<OutstandingMailboxMutation>> ReadOutstandingAsync(
        MailAccountIdentity account,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Counts one account's uncompleted mutations by kind and by where in its lifecycle each one stands.</summary>
    /// <param name="account">The account whose mutations are counted.</param>
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
        MailAccountIdentity account,
        CancellationToken cancellationToken);
}
