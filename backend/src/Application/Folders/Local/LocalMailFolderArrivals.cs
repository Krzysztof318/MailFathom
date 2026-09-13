// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Signals;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Folders.Local;

/// <summary>Places a message synchronization just stored into the local folder its source folder corresponds to, on a held account.</summary>
/// <remarks>
/// On any other account it reads the account's phase and writes nothing, so a mirrored account's synchronization is
/// what it was. The protected folders are supplied in the same transaction when an arrival finds them missing, for the
/// reason an edit supplies them: the inbox and the trash are where the rules send mail that has nowhere else to go.
/// </remarks>
public sealed class LocalMailFolderArrivals
{
    private readonly ILocalMailFolderStore store;
    private readonly ClientSignals signals;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a new instance of the <see cref="LocalMailFolderArrivals" /> class.</summary>
    /// <param name="store">Persists the folders and the placement.</param>
    /// <param name="signals">Tells the account's clients that a placement created folders.</param>
    /// <param name="timeProvider">Supplies the instant a new folder's identity is minted at.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public LocalMailFolderArrivals(ILocalMailFolderStore store, ClientSignals signals, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.store = store;
        this.signals = signals;
        this.timeProvider = timeProvider;
    }

    /// <summary>Reports whether MailFathom holds the account's mailbox alone, which is when its arrivals land in local folders.</summary>
    /// <param name="account">The account.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns><see langword="true" /> when the account is held.</returns>
    public async Task<bool> HoldsAsync(MailAccountIdentity account, CancellationToken cancellationToken) =>
        await this.store.ReadAsync(account, cancellationToken) is { Phase: MailAccountCustodyPhase.Held };

    /// <summary>Places one stored message, inside the transaction that stored it.</summary>
    /// <param name="session">The transaction that stored the message.</param>
    /// <param name="account">The account the message arrived for.</param>
    /// <param name="email">The stored message.</param>
    /// <param name="source">The source folder it arrived from.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>Whether the placement created folders, which the caller announces through <see cref="AnnounceFoldersChanged" /> once the transaction commits.</returns>
    public async Task<bool> PlaceAsync(
        IPersistenceSession session,
        MailAccountIdentity account,
        StoredEmailId email,
        LocalMailFolderArrivalSource source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        // ponytail: the hierarchy is read once per stored message, a bounded indexed read of at most MaximumFolders live
        // rows beside the several writes that transaction already makes. Caching it across a folder run needs the store to
        // hand back the revision its own save bumped, or every later arrival in the run conflicts once; do that if a
        // held account's backfill measures this read as a real share of its cost.
        var holding = await this.store.ReadAsync(session, account, cancellationToken);

        if (holding is not { Phase: MailAccountCustodyPhase.Held })
        {
            return false;
        }

        var found = holding.ToTree();
        var missing = found.MissingProtectedFolders(this.MintId);
        var arrival = found.With(missing).PlaceArrival(source.Alias, source.Role, source.Name, this.MintId);
        // Saved even where nothing is new, because a save is what makes the commit conditional on the hierarchy it read:
        // a folder erased by an edit committing meanwhile would otherwise receive a message after its erasure pass ended.
        await this.store.SaveAsync(session, account, [.. missing, .. arrival.Saved], [], cancellationToken);
        await this.store.PlaceAsync(session, account, email, arrival.Folder, cancellationToken);

        return missing.Count > 0 || arrival.Saved.Count > 0;
    }

    /// <summary>Tells the account's clients that its folder set moved, after the transaction that created the folders committed.</summary>
    /// <param name="account">The account whose folders were created.</param>
    public void AnnounceFoldersChanged(MailAccountIdentity account) => this.signals.Publish(ClientSignal.FoldersChanged(account));

    private LocalMailFolderId MintId() => LocalMailFolderId.Create(Guid.CreateVersion7(this.timeProvider.GetUtcNow()));
}

/// <summary>The source folder a message arrived from, as the arrival rules read it.</summary>
/// <param name="Alias">The folder's alias.</param>
/// <param name="Role">The role the folder plays for its account, or <see langword="null" /> where it plays none.</param>
/// <param name="Name">The folder's own name on the source, which a local folder created for it takes where it can.</param>
public sealed record LocalMailFolderArrivalSource(MailFolderAlias Alias, MailFolderSpecialUse? Role, string? Name);
