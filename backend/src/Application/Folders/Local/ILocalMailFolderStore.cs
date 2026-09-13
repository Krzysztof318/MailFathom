// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Folders.Local;

/// <summary>Persists the folders MailFathom keeps for an account whose mailbox it holds, and which messages are in them.</summary>
/// <remarks>
/// Every read is narrowed to one user's one account, so an account identifier the caller does not hold reads as no
/// account at all rather than as somebody else's.
/// </remarks>
public interface ILocalMailFolderStore
{
    /// <summary>Reads the account's phase and folders inside the transaction a write will commit.</summary>
    /// <param name="session">The transaction the edit commits in.</param>
    /// <param name="account">The account.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The holding, or <see langword="null" /> where the user holds no such account.</returns>
    /// <remarks>
    /// A later <see cref="SaveAsync" /> in the same session is conditional on the hierarchy not having changed since
    /// this read, so two edits reaching one account at once conflict and the loser decides again from what the winner
    /// wrote rather than both committing against the same picture.
    /// </remarks>
    Task<LocalMailFolderHolding?> ReadAsync(
        IPersistenceSession session,
        MailAccountIdentity account,
        CancellationToken cancellationToken);

    /// <summary>Reads the account's phase and folders, joining no transaction.</summary>
    /// <param name="account">The account.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The holding, or <see langword="null" /> where the user holds no such account.</returns>
    Task<LocalMailFolderHolding?> ReadAsync(MailAccountIdentity account, CancellationToken cancellationToken);

    /// <summary>Writes what an edit decided.</summary>
    /// <param name="session">The transaction the read joined.</param>
    /// <param name="account">The account.</param>
    /// <param name="saved">The folders to insert or replace.</param>
    /// <param name="erased">The folders to erase, which leave every listing when this commits.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown at commit when another edit changed the hierarchy since the read.</exception>
    Task SaveAsync(
        IPersistenceSession session,
        MailAccountIdentity account,
        IReadOnlyCollection<LocalMailFolder> saved,
        IReadOnlyCollection<LocalMailFolderId> erased,
        CancellationToken cancellationToken);

    /// <summary>Places a stored message in a local folder, unless it is already in one.</summary>
    /// <param name="session">The transaction that stores the message.</param>
    /// <param name="account">The account the message belongs to.</param>
    /// <param name="email">The stored message.</param>
    /// <param name="folder">The folder it arrives in.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <remarks>A message already in a folder stays there, so a message synchronization meets again is not moved back to where it first arrived.</remarks>
    Task PlaceAsync(
        IPersistenceSession session,
        MailAccountIdentity account,
        StoredEmailId email,
        LocalMailFolderId folder,
        CancellationToken cancellationToken);

    /// <summary>Erases, through the cascade a stored message's erasure runs, up to a bound of the messages in the account's erased folders.</summary>
    /// <param name="session">The transaction the pass commits in.</param>
    /// <param name="account">The account.</param>
    /// <param name="maxEmails">The most messages one pass erases.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>What the pass erased, and whether another is owed.</returns>
    Task<LocalMailFolderMailErasure> EraseMailOfErasedFoldersAsync(
        IPersistenceSession session,
        MailAccountIdentity account,
        int maxEmails,
        CancellationToken cancellationToken);

    /// <summary>Erases one stored message of the account through the cascade a stored message's erasure runs.</summary>
    /// <param name="session">The transaction the erasure commits in.</param>
    /// <param name="account">The account the message belongs to.</param>
    /// <param name="email">The message.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The alias of the folder binding the message was stored under, or <see langword="null" /> where the account holds no such message.</returns>
    /// <remarks>A message already gone is not an error, because the act that erases it may be replaying one that already did.</remarks>
    Task<MailFolderAlias?> EraseEmailAsync(
        IPersistenceSession session,
        MailAccountIdentity account,
        StoredEmailId email,
        CancellationToken cancellationToken);
}
