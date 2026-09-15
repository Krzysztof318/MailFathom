// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Exports;

namespace MailFathom.Application.Mail.Export;

/// <summary>Keeps the record of every export a deployment has been asked for, which is what outlives the job that writes one.</summary>
/// <remarks>
/// <para>
/// The record rather than the work. A job is claimed, leased, retried, and forgotten; an archive is downloadable for
/// as long as its retention allows and has to be findable by an operator who holds nothing but its identity, so where
/// the export stands is written here and nowhere else.
/// </para>
/// <para>
/// Every write states the state it expects to find, so two replicas cannot both decide what became of one export: a
/// cancellation that meets a completion leaves the completion, and a completion that meets a cancellation writes
/// nothing and lets the writing job delete what it produced.
/// </para>
/// </remarks>
public interface IMailboxExportStore
{
    /// <summary>Writes down an export nothing has started yet.</summary>
    /// <param name="queuedExport">The queued export.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the record is durable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="queuedExport" /> is <see langword="null" />.</exception>
    Task RecordAsync(MailboxExport queuedExport, CancellationToken cancellationToken);

    /// <summary>Reads one export of one account.</summary>
    /// <param name="account">The account the export belongs to.</param>
    /// <param name="exportId">The export's identity.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The export, or <see langword="null" /> where that account holds none under that identity.</returns>
    /// <remarks>The account is part of the question rather than a filter applied afterwards, so an identity minted for another mailbox answers as absent rather than as found.</remarks>
    Task<MailboxExport?> FindAsync(MailAccountId account, MailboxExportId exportId, CancellationToken cancellationToken);

    /// <summary>Reads the export this account already has being written, where it has one.</summary>
    /// <param name="account">The account asked about.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The queued or running export, or <see langword="null" /> when the account has none.</returns>
    Task<MailboxExport?> FindInFlightAsync(MailAccountId account, CancellationToken cancellationToken);

    /// <summary>Reads the account's exports, newest first.</summary>
    /// <param name="account">The account asked about.</param>
    /// <param name="limit">How many rows to read at most.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The exports, newest first.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="limit" /> is not positive.</exception>
    Task<IReadOnlyList<MailboxExport>> ListAsync(MailAccountId account, int limit, CancellationToken cancellationToken);

    /// <summary>Names the completed exports whose retention period has run out.</summary>
    /// <param name="asOf">The instant expiry is judged against.</param>
    /// <param name="limit">How many to name at most, which bounds one pass.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The exports whose archives are due to be deleted.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="limit" /> is not positive.</exception>
    /// <remarks>Read from the recorded expiry rather than from a timer held in one process, which is what makes expiry survive a restart and a replica ending.</remarks>
    Task<IReadOnlyList<MailboxExport>> FindDueForExpiryAsync(
        DateTimeOffset asOf,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Writes the export back, but only while it is still in the state the caller decided from.</summary>
    /// <param name="updatedExport">The export as it should now stand.</param>
    /// <param name="expectedState">The state the caller read before deciding.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when the write applied; <see langword="false" /> when the export had already moved on.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="updatedExport" /> is <see langword="null" />.</exception>
    Task<bool> SaveAsync(
        MailboxExport updatedExport,
        MailboxExportState expectedState,
        CancellationToken cancellationToken);

    /// <summary>Records how far the archive has got, without deciding anything about the export's state.</summary>
    /// <param name="exportId">The export being written.</param>
    /// <param name="messageCount">How many messages have reached the archive.</param>
    /// <param name="byteCount">How many bytes of stored mail they carried.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the counts are durable.</returns>
    /// <remarks>
    /// A member of its own because it is the one write a cancellation must not lose to. It applies only while the export
    /// is running, so a progress write racing an operator's cancellation leaves the cancellation standing.
    /// </remarks>
    Task SaveProgressAsync(
        MailboxExportId exportId,
        long messageCount,
        long byteCount,
        CancellationToken cancellationToken);
}
