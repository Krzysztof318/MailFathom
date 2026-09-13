// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders.Local;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Keeps one account's local folders and placements in memory, answering as the store does for any other account.</summary>
/// <remarks>Folders are read back only while the account is held, which is what the real store loads, so a test cannot see a hierarchy the use case could not.</remarks>
internal sealed class InMemoryLocalMailFolderStore : ILocalMailFolderStore
{
    private readonly Dictionary<LocalMailFolderId, LocalMailFolder> folders = [];
    private readonly HashSet<LocalMailFolderId> erased = [];
    private readonly Dictionary<StoredEmailId, LocalMailFolderId> placements = [];

    internal InMemoryLocalMailFolderStore(MailAccountIdentity account, MailAccountCustodyPhase phase)
    {
        this.Account = account;
        this.Phase = phase;
    }

    internal MailAccountIdentity Account { get; }

    internal MailAccountCustodyPhase Phase { get; }

    internal IReadOnlyCollection<LocalMailFolder> Folders => this.folders.Values;

    internal IReadOnlyCollection<LocalMailFolderId> Erased => this.erased;

    internal IReadOnlyDictionary<StoredEmailId, LocalMailFolderId> Placements => this.placements;

    internal int SaveCount { get; private set; }

    public Task<LocalMailFolderHolding?> ReadAsync(
        IPersistenceSession session,
        MailAccountIdentity account,
        CancellationToken cancellationToken) =>
        this.ReadAsync(account, cancellationToken);

    public Task<LocalMailFolderHolding?> ReadAsync(MailAccountIdentity account, CancellationToken cancellationToken) =>
        Task.FromResult(account == this.Account
            ? new LocalMailFolderHolding(this.Phase, this.Phase == MailAccountCustodyPhase.Held ? [.. this.folders.Values] : [], [])
            : null);

    public Task SaveAsync(
        IPersistenceSession session,
        MailAccountIdentity account,
        IReadOnlyCollection<LocalMailFolder> saved,
        IReadOnlyCollection<LocalMailFolderId> erased,
        CancellationToken cancellationToken)
    {
        this.SaveCount++;

        foreach (var folder in saved)
        {
            this.folders[folder.Id] = folder;
        }

        foreach (var folder in erased)
        {
            this.folders.Remove(folder);
            this.erased.Add(folder);
        }

        return Task.CompletedTask;
    }

    public Task PlaceAsync(
        IPersistenceSession session,
        MailAccountIdentity account,
        StoredEmailId email,
        LocalMailFolderId folder,
        CancellationToken cancellationToken)
    {
        this.placements.TryAdd(email, folder);

        return Task.CompletedTask;
    }

    public Task<LocalMailFolderMailErasure> EraseMailOfErasedFoldersAsync(
        IPersistenceSession session,
        MailAccountIdentity account,
        int maxEmails,
        CancellationToken cancellationToken) =>
        Task.FromResult(new LocalMailFolderMailErasure(ErasedEmailCount: 0, EmailsRemain: false));
}
