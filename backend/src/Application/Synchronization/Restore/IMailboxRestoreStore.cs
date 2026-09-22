// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Synchronization.Restore;

/// <summary>Reads what a restoring account still owes its source, and records what the restore did about it.</summary>
/// <remarks>
/// <para>
/// The append record is the durable half of this port and the reason it exists at all. An <c>APPEND</c> is the one
/// command the mode issues that is not idempotent, so the record is written before it goes out and deleted once the
/// server has named where the copy went — which is the opposite order from the drain, whose occurrence is cleared last
/// precisely because both of its commands may be issued again.
/// </para>
/// <para>
/// Every write takes the caller's session, because each is committed beside the change it settles: the occurrence
/// written onto a message and the record deleted from under it are one fact about that message. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0034-holding-a-mailbox-mailfathom-alone-keeps.md">ADR 0034</see>.
/// </para>
/// </remarks>
public interface IMailboxRestoreStore
{
    /// <summary>Reads the oldest messages of a restoring account that its source no longer holds.</summary>
    /// <param name="account">The account.</param>
    /// <param name="maximumCandidates">The most messages one pass takes in hand.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The candidates, oldest first, bounded by what was asked for.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maximumCandidates" /> is not positive.</exception>
    /// <remarks>
    /// Only a message carrying no occurrence is a candidate, which is what makes the restore append exactly what the
    /// drain took off. A message an append record already stands for is not one either: its copy may be in the folder
    /// already, and appending again is the one mistake no later correction undoes.
    /// </remarks>
    Task<IReadOnlyList<MailboxRestoreCandidate>> ReadCandidatesAsync(
        MailAccountId account,
        int maximumCandidates,
        CancellationToken cancellationToken);

    /// <summary>Reads the messages of a restoring account whose local state has still to be written onto the occurrence they keep.</summary>
    /// <param name="account">The account.</param>
    /// <param name="maximumCandidates">The most messages one pass takes in hand.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The messages, oldest first, bounded by what was asked for.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maximumCandidates" /> is not positive.</exception>
    /// <remarks>
    /// <para>
    /// These are the messages the drain never reached, so the source still holds them where it always did — with
    /// whatever flags it last had, since the source's own values stopped being observed at the switch on.
    /// </para>
    /// <para>
    /// A message the restore appended can re-enter this walk, and deliberately so. The walk's position is one cursor
    /// per account rather than a stamp per message, so a confirmation cannot mark its own message written without
    /// moving the cursor past every message between — which would silently skip their state. What the cursor costs
    /// instead is bounded and harmless: a message whose identity sorts after the cursor has records opened for the
    /// state its own <c>APPEND</c> already carried, and the converger writes what the source is already showing.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<MailboxRestoredStateCandidate>> ReadStateCandidatesAsync(
        MailAccountId account,
        int maximumCandidates,
        CancellationToken cancellationToken);

    /// <summary>Records that one message's local state has been written down as mutations for the converger to carry.</summary>
    /// <param name="session">The transaction the stamp commits in, which is the one the records were written in.</param>
    /// <param name="account">The account the message is stored for.</param>
    /// <param name="email">The message the walk has reached.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes once the stamp is staged in the session.</returns>
    /// <remarks>
    /// The stamp and the records are one transaction, because the stamp is the whole of what stops the next pass
    /// writing a second set of them. A crash before the commit leaves the message unstamped and the records unwritten,
    /// which is the state the pass is already able to start from. It carries no instant: what it records is how far
    /// the walk has got, which is the message's own identity and nothing about when.
    /// </remarks>
    Task RecordStateWrittenAsync(
        IPersistenceSession session,
        MailAccountId account,
        StoredEmailId email,
        CancellationToken cancellationToken);

    /// <summary>Writes down that an append is about to be issued for one message.</summary>
    /// <param name="session">The transaction the record commits in.</param>
    /// <param name="account">The account whose mailbox the copy goes back into.</param>
    /// <param name="record">The record to write.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns><see langword="true" /> when this caller is the one that took the message in hand.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="record" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>It commits before the command goes out, which is the whole of what stops a second copy.</para>
    /// <para>
    /// It resolves from a fresh read rather than inserting unconditionally, because the caller commits it under the
    /// optimistic retry policy and that policy's contract is a write that means the same thing on a replay. A record
    /// already standing for the message answers <see langword="false" />: either a commit whose answer was lost in
    /// fact landed, or a pass on another replica reached the message first, and in both readings an <c>APPEND</c>
    /// from this caller would be the second one.
    /// </para>
    /// </remarks>
    Task<bool> WriteAppendAsync(
        IPersistenceSession session,
        MailAccountId account,
        MailboxRestoreAppend record,
        CancellationToken cancellationToken);

    /// <summary>Writes down the placement the source named, on the record the append was issued under.</summary>
    /// <param name="session">The transaction the placement commits in.</param>
    /// <param name="record">The record the append was issued under.</param>
    /// <param name="uidValidity">The UIDVALIDITY the source named.</param>
    /// <param name="uid">The UID the source named.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes once the placement is staged in the session.</returns>
    /// <remarks>
    /// An update by primary key and nothing else, which is what makes it the narrowest write between a fully answered
    /// <c>APPEND</c> and the occurrence that answer justifies. Carrying the occurrence onto the message can lose a
    /// race against synchronization and needs the message's own row; this cannot lose anything, so a record that
    /// carries a placement is one the next pass finishes on its own rather than one an operator has to establish.
    /// </remarks>
    Task RecordPlacementAsync(
        IPersistenceSession session,
        MailboxRestoreAppendId record,
        ImapUidValidity uidValidity,
        ImapUid uid,
        CancellationToken cancellationToken);

    /// <summary>Writes down that the restore can never put one message back, so it stops being counted as outstanding.</summary>
    /// <param name="session">The transaction the record commits in.</param>
    /// <param name="account">The account the message is stored for.</param>
    /// <param name="record">The record to write, which is settled from the moment it exists.</param>
    /// <param name="settledAt">When the restore established that the message cannot go back.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns><see langword="true" /> when this caller is the one that wrote it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="record" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A message whose stored payload cannot be served has no bytes to append and no later pass can produce any, so
    /// leaving it a candidate would count it as outstanding on every pass and hold the account in
    /// <c>Restoring</c> for ever, with no record for an operator to settle. Recorded as settled it stops being
    /// counted, the phase can end, and the source simply never gets that message back — which is what the pass
    /// reports as <see cref="MailboxRestoreFailure.ContentUnreadable" /> and what an operator reads in the log.
    /// </remarks>
    Task<bool> RecordUnrestorableAsync(
        IPersistenceSession session,
        MailAccountId account,
        MailboxRestoreAppend record,
        DateTimeOffset settledAt,
        CancellationToken cancellationToken);

    /// <summary>Writes the occurrence the source named onto the message and deletes the record that stood for the append.</summary>
    /// <param name="session">The transaction both writes commit in.</param>
    /// <param name="record">The record the append was issued under.</param>
    /// <param name="occurrence">Where the copy now is: the folder it was appended into, and the identity the source named in it.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns><see langword="true" /> when the message took the occurrence, and <see langword="false" /> when something else already held it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="record" /> or <paramref name="occurrence" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// One transaction, because the record exists to say the outcome is unknown and the occurrence is what makes it
    /// known. A commit that wrote one without the other would either leave a settled append standing for an operator to
    /// chase or leave the message appended a second time by the next pass.
    /// </para>
    /// <para>
    /// The folder is part of what is written rather than only the identity inside it, because a message somebody moved
    /// while the account was held goes back into the folder it is in now: its row bound the folder the drain took it
    /// off, and an occurrence naming that folder with this folder's UID would name somebody else's mail. A message
    /// something else already holds that occurrence — synchronization having met the copy as an arrival before this
    /// committed — keeps its record rather than losing it, because it is then a message that may be on the source
    /// twice and that is an operator's to establish.
    /// </para>
    /// <para>
    /// Such a record also loses the placement recorded on it, in the same transaction. That is what moves it from the
    /// work a pass finishes on its own to the work an operator is offered: nothing else would ever carry it, and a
    /// record nothing finishes and nobody is offered holds the account in its phase for ever.
    /// </para>
    /// </remarks>
    Task<bool> ConfirmAppendAsync(
        IPersistenceSession session,
        MailboxRestoreAppend record,
        EmailOccurrenceId occurrence,
        CancellationToken cancellationToken);

    /// <summary>Reads the appends of one account the source has answered in full and nothing has yet carried.</summary>
    /// <param name="account">The account.</param>
    /// <param name="maximumRecords">The most records to read, which is a ceiling rather than a page.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The placements, oldest first, bounded by what was asked for.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maximumRecords" /> is not positive.</exception>
    /// <remarks>
    /// Ordinarily empty, because the pass that recorded a placement carries it in the same run. What it returns is
    /// what a pass that ended between the two left behind, and finishing that is the difference between a message
    /// whose occurrence was known all along and one an operator has to go and look for.
    /// </remarks>
    Task<IReadOnlyList<MailboxRestoreConfirmation>> ReadConfirmableAppendsAsync(
        MailAccountId account,
        int maximumRecords,
        CancellationToken cancellationToken);

    /// <summary>Reads the appends of one account whose answer never came back.</summary>
    /// <param name="account">The account.</param>
    /// <param name="maximumRecords">The most records to read, which is a ceiling rather than a page.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The records, oldest first, bounded by what was asked for.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maximumRecords" /> is not positive.</exception>
    Task<IReadOnlyList<MailboxRestoreAppend>> ReadUnansweredAppendsAsync(
        MailAccountId account,
        int maximumRecords,
        CancellationToken cancellationToken);

    /// <summary>Records an operator's verdict on one unanswered append.</summary>
    /// <param name="session">The transaction the verdict commits in.</param>
    /// <param name="account">The account the record belongs to.</param>
    /// <param name="record">The record being settled.</param>
    /// <param name="sourceHoldsTheCopy">Whether the operator found the copy in the folder.</param>
    /// <param name="settledAt">When the verdict was given.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns><see langword="true" /> when the record was still standing and the verdict was written.</returns>
    /// <remarks>
    /// A copy the operator found keeps its record, stamped rather than deleted: the message has no occurrence to write,
    /// and the record is the only thing that stops the next pass putting a second copy beside the first. A copy that is
    /// not there deletes the record, and the next pass appends the message as it would any other.
    /// </remarks>
    Task<bool> SettleAppendAsync(
        IPersistenceSession session,
        MailAccountId account,
        MailboxRestoreAppendId record,
        bool sourceHoldsTheCopy,
        DateTimeOffset settledAt,
        CancellationToken cancellationToken);

    /// <summary>Counts the account's local folders that hold mail and correspond to no folder mapping.</summary>
    /// <param name="account">The account.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>How many such folders there are.</returns>
    /// <remarks>
    /// A local folder corresponds to a mapping by the source folder whose arrivals it receives. One a person created
    /// while the account was held receives none until an operator writes a mapping for it, and there is no folder on
    /// the source its mail could go back into — so the restore pauses rather than deriving a source path from a local
    /// name, which is the one thing ADR 0034 never permits.
    /// </remarks>
    Task<int> CountUnmappedFoldersHoldingMailAsync(MailAccountId account, CancellationToken cancellationToken);

    /// <summary>Counts what one restoring account still owes its source.</summary>
    /// <param name="account">The account.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The standing figures an operator reads to know how far a switch off has got.</returns>
    Task<MailboxRestoreStanding> ReadStandingAsync(MailAccountId account, CancellationToken cancellationToken);
}

/// <summary>What a restoring account still owes its source, counted rather than listed.</summary>
/// <param name="AwaitingAppend">Messages the source no longer holds that have still to be put back.</param>
/// <param name="AwaitingStateWrite">Messages whose local state has still to be written onto the occurrence they keep.</param>
/// <param name="UnansweredAppends">Appends whose answer never came back, each of which holds the account in its phase.</param>
/// <param name="AwaitingConfirmation">Appends the source answered in full whose occurrence has still to be written onto the message.</param>
/// <remarks>
/// <para>
/// Counts and never a listing, for the reason the drain's own standing carries none: what a client sees about a held
/// message is nothing about the message, and these figures carry no subject, address, or content.
/// </para>
/// <para>
/// The last two are disjoint by construction and are told apart because an operator can act on only one of them. An
/// unanswered append is a folder somebody has to look in; one awaiting confirmation is work the next pass does, and
/// offering it as a verdict would either put a second copy in a folder or strand a placement nobody ever carries.
/// </para>
/// </remarks>
public sealed record MailboxRestoreStanding(
    int AwaitingAppend,
    int AwaitingStateWrite,
    int UnansweredAppends,
    int AwaitingConfirmation)
{
    /// <summary>The standing of an account that is not restoring anything.</summary>
    public static MailboxRestoreStanding Nothing { get; } = new(0, 0, 0, 0);

    /// <summary>Gets whether the account still owes its source something the restore itself has to do.</summary>
    public bool IsOutstanding =>
        this.AwaitingAppend > 0
        || this.AwaitingStateWrite > 0
        || this.UnansweredAppends > 0
        || this.AwaitingConfirmation > 0;
}
