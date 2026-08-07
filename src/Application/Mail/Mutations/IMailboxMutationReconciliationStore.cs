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
    /// <summary>Reads the relocations that named one folder as their destination and placed an email at one of these UIDs.</summary>
    /// <param name="accountId">The account whose mutations are read.</param>
    /// <param name="destinationPath">The remote folder being synchronized, which is the destination those relocations named.</param>
    /// <param name="uidValidity">The UIDVALIDITY that folder reports now.</param>
    /// <param name="uids">The UIDs one batch of the forward pass discovered.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Every record whose reported placement is one of those occurrences, which may be none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="uids" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A whole batch of discoveries is asked about at once, so a folder that has no relocation pending — which is nearly
    /// every folder on nearly every run — costs one query rather than one per message. The batch's own UIDs bound the
    /// answer, so no limit has to be invented for it and no candidate can be missed by one. Which record a given
    /// occurrence belongs to is then decided by <see cref="MailboxMutationRecord.IsPlacementOf" />, which restates every
    /// condition of the read rather than trusting it.
    /// </remarks>
    Task<IReadOnlyList<MailboxMutationRecord>> ReadRelocationsPlacedAtAsync(
        MailAccountId accountId,
        RemoteFolderPath destinationPath,
        ImapUidValidity uidValidity,
        IReadOnlyCollection<ImapUid> uids,
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
    /// It settles the source half as well, where nothing has settled it already. Carrying the row across is what takes
    /// the email out of the source folder locally, so no later window can select it there and no later run can observe
    /// the disappearance the record is otherwise still waiting for. The stage this is only ever reached from is
    /// <see cref="MailboxMutationStage.Completed" />, which is the server's own statement that the source occurrence is
    /// already gone.
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
