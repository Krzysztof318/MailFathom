// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Synchronization.Drain;

/// <summary>Reads what a held account still has on its source, and records what the drain did about it.</summary>
/// <remarks>
/// The occurrence columns on the stored row are the drain's own durable record, which is why nothing here writes an
/// intent before a command: both drain commands are idempotent against the UIDs they name, so a process that died
/// between issuing one and seeing it answered leaves the occurrence standing and the next pass issues it again.
/// Clearing the occurrence is therefore the last step rather than the first, and synchronization meeting that UID in
/// between recognizes the row it already has rather than storing the message twice.
/// </remarks>
public interface IMailboxDrainStore
{
    /// <summary>Reads the oldest messages of a held account that its source still holds.</summary>
    /// <param name="account">The account.</param>
    /// <param name="maximumCandidates">The most messages one pass takes in hand.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The candidates, oldest first, bounded by what was asked for.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maximumCandidates" /> is not positive.</exception>
    /// <remarks>
    /// Oldest first so a mailbox of years empties from the end nobody is reading, and bounded so one pass costs a
    /// bounded number of reads whatever the mailbox holds. Every candidate carries an occurrence: a row whose occurrence
    /// was cleared has already been drained and is not asked about again.
    /// </remarks>
    Task<IReadOnlyList<MailboxDrainCandidate>> ReadCandidatesAsync(
        MailAccountId account,
        int maximumCandidates,
        CancellationToken cancellationToken);

    /// <summary>Reads the same messages again, as they stand now.</summary>
    /// <param name="account">The account.</param>
    /// <param name="emails">The messages to re-read.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The candidates that are still there and still carry an occurrence, which may be fewer than were asked about.</returns>
    /// <remarks>
    /// The gate is re-evaluated immediately before a batch is issued rather than only when a message is selected for
    /// one, so a row erased, moved, or drained by another replica between the two is judged on what is true when the
    /// command goes out. A message this does not return is one the batch drops.
    /// </remarks>
    Task<IReadOnlyList<MailboxDrainCandidate>> ReadCandidatesAgainAsync(
        MailAccountId account,
        IReadOnlyCollection<StoredEmailId> emails,
        CancellationToken cancellationToken);

    /// <summary>Records that a message's stored payload was read back and matched what the row records.</summary>
    /// <param name="session">The transaction the record commits in.</param>
    /// <param name="email">The message.</param>
    /// <param name="verifiedAt">When the read-back was established.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <remarks>
    /// Written once per message, so a batch abandoned before its commands went out does not pay for the read again on
    /// the next pass. It is a fact about the payload rather than a permission: the rest of the gate is re-read before
    /// every batch.
    /// </remarks>
    Task RecordContentVerifiedAsync(
        IPersistenceSession session,
        StoredEmailId email,
        DateTimeOffset verifiedAt,
        CancellationToken cancellationToken);

    /// <summary>Clears the occurrence of every message whose expunge the source has answered.</summary>
    /// <param name="session">The transaction the clearing commits in.</param>
    /// <param name="account">The account the messages belong to.</param>
    /// <param name="drained">The messages the source no longer holds.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>How many rows were cleared.</returns>
    /// <remarks>Clearing is also the data-minimization answer: a remote path and a UID that name nothing any more are not kept.</remarks>
    Task<int> ClearOccurrencesAsync(
        IPersistenceSession session,
        MailAccountId account,
        IReadOnlyCollection<StoredEmailId> drained,
        CancellationToken cancellationToken);

    /// <summary>Reads the messages erased before the drain reached them, whose source copy still has to go.</summary>
    /// <param name="account">The account.</param>
    /// <param name="maximumRemovals">The most records one pass takes in hand.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The records, oldest first, bounded by what was asked for.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maximumRemovals" /> is not positive.</exception>
    Task<IReadOnlyList<MailboxSourceRemoval>> ReadSourceRemovalsAsync(
        MailAccountId account,
        int maximumRemovals,
        CancellationToken cancellationToken);

    /// <summary>Deletes the source removal records whose expunge the source has answered.</summary>
    /// <param name="session">The transaction the deletion commits in.</param>
    /// <param name="removals">The records to delete.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    Task DeleteSourceRemovalsAsync(
        IPersistenceSession session,
        IReadOnlyCollection<MailboxSourceRemovalId> removals,
        CancellationToken cancellationToken);

    /// <summary>Counts what one held account's source still holds, and what the gate is keeping there.</summary>
    /// <param name="account">The account.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The standing figures an operator reads to know how far a switch has got.</returns>
    Task<MailboxDrainStanding> ReadStandingAsync(MailAccountId account, CancellationToken cancellationToken);
}

/// <summary>What one held account's source still holds, counted rather than listed.</summary>
/// <param name="AwaitingDrain">Messages whose occurrence still stands, whether or not the gate would pass them.</param>
/// <param name="HeldBackAboveSizeLimit">Messages the source keeps because their content is above the size MailFathom stores.</param>
/// <param name="HeldBackAwaitingHeadroom">Messages the source keeps until the storage ceiling has headroom for them.</param>
/// <param name="AwaitingSourceRemoval">Messages erased locally whose source copy has still to be removed.</param>
/// <remarks>
/// Counts and never a listing, because what a client sees about a held message is nothing about the message: its
/// presence on the source is not a property a person acts on, and the figures carry no subject, address, or content.
/// </remarks>
public sealed record MailboxDrainStanding(
    int AwaitingDrain,
    int HeldBackAboveSizeLimit,
    int HeldBackAwaitingHeadroom,
    int AwaitingSourceRemoval);
