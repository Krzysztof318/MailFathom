// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Mail.Mutations;

/// <summary>Reads the mutation records synchronization has to recognize its own work in, and writes down that it did.</summary>
/// <remarks>
/// <para>
/// It is a port of its own rather than more members on <see cref="IMailboxMutationRecordStore" /> because the two answer
/// to different callers. That one belongs to the act of performing a mutation and is written through the session the
/// performer already holds; this one belongs to a synchronization run, which reads records it did not create and asks
/// about them by where the email is rather than by which record it is.
/// </para>
/// <para>
/// Both reads are bounded, and neither returns anything derived from a message. A record names folders, identifiers, and
/// a requester, which is what lets the join be made without reading mail.
/// </para>
/// </remarks>
public interface IMailboxMutationReconciliationStore
{
    /// <summary>Reads the mutations that named one folder as their destination and placed an email at one of these UIDs.</summary>
    /// <param name="accountId">The account whose mutations are read.</param>
    /// <param name="destinationPath">The remote folder being synchronized, which is the destination those mutations named.</param>
    /// <param name="uidValidity">The UIDVALIDITY that folder reports now.</param>
    /// <param name="uids">The UIDs one batch of the forward pass discovered.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Every record whose reported placement is one of those occurrences, which may be none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="uids" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// A whole batch of discoveries is asked about at once, so a folder that has no placement pending — which is nearly
    /// every folder on nearly every run — costs one query rather than one per message. The batch's own UIDs bound the
    /// answer, so no limit has to be invented for it and no candidate can be missed by one. Which record a given
    /// occurrence belongs to is then decided by <see cref="MailboxMutationRecord.AccountsForPlacementAt" />, which
    /// restates every condition of the read rather than trusting it.
    /// </para>
    /// <para>
    /// A relocation and a copy are both read, because both put a message where the forward pass will meet it and the
    /// discovery is MailFathom's own act either way. What the two then do differs — a relocation carries the local email
    /// onto the new occurrence and a copy does not — and that is the caller's decision rather than this read's.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<MailboxMutationRecord>> ReadPlacementsAtAsync(
        MailAccountId accountId,
        RemoteFolderPath destinationPath,
        ImapUidValidity uidValidity,
        IReadOnlyCollection<ImapUid> uids,
        CancellationToken cancellationToken);

    /// <summary>The greatest number of flag and keyword stores the attribution of one window reads.</summary>
    /// <param name="changedOccurrenceCount">How many occurrences the window found standing somewhere new.</param>
    /// <returns>One record per value each of those occurrences could carry.</returns>
    /// <remarks>
    /// It is a ceiling rather than a page, and it is derived from the window rather than fixed, because the window is
    /// what decides how many records can account for something: a run reconciles up to as many occurrences as the
    /// account's configured window holds, and each of them can carry a store of every value a <c>FLAGS</c> response
    /// reports. A constant would have had to be right for both a window of ten and a window of ten thousand, and being
    /// wrong at the top end drops records that explain a change — which is the one failure this read exists to prevent,
    /// since a value nothing explains is credited to the mailbox owner and reacted to as their act.
    /// <para>
    /// What it still bounds is duplication under one value: a caller-authored requester is per invocation, so an agent
    /// that stars and unstars one message leaves a record per call, and only the newest of those can account for the
    /// current reading. Reaching the number therefore means one window's occurrences carry more unspent stores than any
    /// attribution has a use for, and the adapter says so in a log line rather than truncating in silence.
    /// </para>
    /// </remarks>
    static int MaximumFlagChangeRecordsFor(int changedOccurrenceCount) =>
        changedOccurrenceCount * MailboxMutation.FlagWriting.Count;

    /// <summary>Reads the flag and keyword stores issued against any of the occurrences a reconciliation window found moved.</summary>
    /// <param name="accountId">The account whose mutations are read.</param>
    /// <param name="folderResolutionId">The alias binding the occurrences were stored under.</param>
    /// <param name="uidValidity">The UIDVALIDITY the window was opened for.</param>
    /// <param name="uids">The UIDs whose <c>\Seen</c> flag, <c>\Flagged</c> flag, or keywords the window found standing somewhere new.</param>
    /// <param name="issuedAfter">The earliest previous observation among those occurrences, before which no record can account for anything.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Every store of one of those values issued against one of those occurrences, which may be none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="uids" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// This is the read the whole provenance question turns on. A flag change reaches synchronization as a changed
    /// modification sequence, which is exactly what a person marking mail read or starring it in their own client
    /// produces, so nothing in the server's answer distinguishes the two and only the record does. A rule conditioned on
    /// unread mail that marks mail read would otherwise re-evaluate every message it had just acted on.
    /// </para>
    /// <para>
    /// All five stores are read together rather than one query per value, because one <c>FLAGS</c> response carries
    /// every value at once: an occurrence whose star and whose label both moved is one question, and splitting it would
    /// cost a query per value on every window that found anything. Which record answers for which value is settled by
    /// the caller against the record's own comparisons.
    /// </para>
    /// <para>
    /// Only the occurrences where something actually moved are asked about, so a window that found the mailbox unchanged
    /// — which is most windows — asks nothing. The idempotency identity no longer bounds what one occurrence can carry:
    /// the requester of a caller-authored change is the invocation, so an agent that stars and unstars one message
    /// leaves a record per call and nothing ever deletes one. What bounds the answer instead is stated rather than
    /// assumed — the UIDs and <paramref name="issuedAfter" />, which drops only records that could account for nothing,
    /// since all three comparisons require a record whose stage moved after the occurrence was last read. What survives
    /// both is capped at <see cref="MaximumFlagChangeRecordsFor" /> of the occurrences asked about.
    /// </para>
    /// <para>
    /// Every such record is returned, spent or not, because whether one still accounts for anything is settled against
    /// the occurrence's own last observation rather than against a mark on the row. That comparison belongs to
    /// <see cref="MailboxMutationRecord.AccountsForSeenStateOf" />,
    /// <see cref="MailboxMutationRecord.AccountsForFlaggedStateOf" />, and
    /// <see cref="MailboxMutationRecord.AccountsForKeywordsOf" />, which the caller applies to what this returns.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<MailboxMutationRecord>> ReadFlagChangesOnAsync(
        MailAccountId accountId,
        MailFolderResolutionId folderResolutionId,
        ImapUidValidity uidValidity,
        IReadOnlyCollection<ImapUid> uids,
        DateTimeOffset issuedAfter,
        CancellationToken cancellationToken);

    /// <summary>Reads the mutations issued against any of the source occurrences a reconciliation window found gone.</summary>
    /// <param name="accountId">The account whose mutations are read.</param>
    /// <param name="folderResolutionId">The alias binding the occurrences were stored under.</param>
    /// <param name="uidValidity">The UIDVALIDITY the window was opened for.</param>
    /// <param name="uids">The UIDs the folder no longer holds.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Every record issued against one of those occurrences, which may be none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="uids" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// The whole window is asked about at once, so attributing a window's disappearances costs one query rather than one
    /// per email. A record that already carries its observation is part of the answer: it stays the durable reason that
    /// occurrence is gone, and a window that asks about the same disappearance again must get the same answer rather
    /// than fall through to the path that handles somebody else's deletion.
    /// </para>
    /// <para>
    /// The answer is bounded by <paramref name="uids" /> and by the schema rather than by a limit of its own, and that is
    /// deliberate. One occurrence can carry several records — the idempotency identity is the occurrence, the requester,
    /// and the mutation together — so the rows are the window's occurrences times the requesters that asked something of
    /// each, which is a handful. A count limit on top would be the wrong shape: it would truncate whichever occurrences
    /// sorted last, and an occurrence whose record was cut would be attributed to nobody and reach the remote-deletion
    /// path, which is the one outcome this read exists to prevent.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<MailboxMutationRecord>> ReadMutationsRemovingAsync(
        MailAccountId accountId,
        MailFolderResolutionId folderResolutionId,
        ImapUidValidity uidValidity,
        IReadOnlyCollection<ImapUid> uids,
        CancellationToken cancellationToken);

    /// <summary>Writes down that the occurrence a relocation created has been recognized and the local row carried onto it.</summary>
    /// <param name="session">The session the write joins, which is the one the row is carried across in.</param>
    /// <param name="recordId">The record whose placement was recognized.</param>
    /// <param name="observedAt">When the run recognized it.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the observation is written.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no record carries <paramref name="recordId" />.</exception>
    /// <remarks>
    /// <para>
    /// It settles the source half as well, for a mutation that has one. Carrying the row across is what takes the email
    /// out of the source folder locally, so no later window can select it there and no later run can observe the
    /// disappearance the record is otherwise still waiting for. The stage this is only ever reached from is
    /// <see cref="MailboxMutationStage.Completed" />, which is the server's own statement that the source occurrence is
    /// already gone. A copy leaves its source where it was and settles nothing about it.
    /// </para>
    /// <para>
    /// Writing it is also what expires the suppression. A record whose placement has been observed answers for no later
    /// discovery, which is what keeps a folder recreated under a reused UID from being attributed to a mutation made
    /// against the previous one.
    /// </para>
    /// </remarks>
    Task RecordPlacementObservedAsync(
        IPersistenceSession session,
        MailboxMutationRecordId recordId,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken);

    /// <summary>Writes down that the source occurrence a mutation removed has been seen to leave its folder.</summary>
    /// <param name="session">The session the write joins, which is the one the window's outcome is applied in.</param>
    /// <param name="recordId">The record the disappearance was attributed to.</param>
    /// <param name="observedAt">When the window read the folder.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the observation is written.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no record carries <paramref name="recordId" />.</exception>
    /// <remarks>The first observation is kept, because what it says is when the disappearance was first accounted for.</remarks>
    Task RecordSourceRemovalObservedAsync(
        IPersistenceSession session,
        MailboxMutationRecordId recordId,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken);
}
