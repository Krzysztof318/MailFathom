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
/// <remarks>
/// Folders are read back while the account holds its mailbox and while it is being restored, which is what the real store
/// loads, so a test cannot see a hierarchy the use case could not. An erased folder's source alias is kept and reported, as the real store keeps the
/// erased row, because it is what sends a later arrival from that source to the inbox. Writes naming another account
/// change nothing, as the real store's writes are scoped to the account they name.
/// </remarks>
internal sealed class InMemoryLocalMailFolderStore : ILocalMailFolderStore
{
    private readonly Dictionary<LocalMailFolderId, LocalMailFolder> folders = [];
    private readonly HashSet<LocalMailFolderId> erased = [];
    private readonly HashSet<MailFolderAlias> erasedSourceAliases = [];
    private readonly Dictionary<StoredEmailId, LocalMailFolderId> placements = [];
    private readonly List<StoredEmailId> erasedEmails = [];

    internal InMemoryLocalMailFolderStore(MailAccountId account, MailAccountCustodyPhase phase)
    {
        this.Account = account;
        this.Phase = phase;
    }

    internal MailAccountId Account { get; }

    /// <summary>Gets or sets the account's custody phase, which a test moves to arrange an account drained after something was appended.</summary>
    internal MailAccountCustodyPhase Phase { get; set; }

    internal IReadOnlyCollection<LocalMailFolder> Folders => this.folders.Values;

    internal IReadOnlyCollection<LocalMailFolderId> Erased => this.erased;

    internal IReadOnlyDictionary<StoredEmailId, LocalMailFolderId> Placements => this.placements;

    internal IReadOnlyList<StoredEmailId> ErasedEmails => this.erasedEmails;

    internal int SaveCount { get; private set; }

    public Task<LocalMailFolderHolding?> ReadAsync(
        IPersistenceSession session,
        MailAccountId account,
        CancellationToken cancellationToken) =>
        this.ReadAsync(account, cancellationToken);

    public Task<LocalMailFolderHolding?> ReadAsync(MailAccountId account, CancellationToken cancellationToken) =>
        Task.FromResult(account == this.Account
            ? this.Phase is MailAccountCustodyPhase.Held or MailAccountCustodyPhase.Restoring
                ? new LocalMailFolderHolding(this.Phase, [.. this.folders.Values], [.. this.erasedSourceAliases])
                : new LocalMailFolderHolding(this.Phase, [], [])
            : null);

    public Task SaveAsync(
        IPersistenceSession session,
        MailAccountId account,
        IReadOnlyCollection<LocalMailFolder> saved,
        IReadOnlyCollection<LocalMailFolderId> erased,
        CancellationToken cancellationToken)
    {
        if (account != this.Account)
        {
            return Task.CompletedTask;
        }

        this.SaveCount++;

        foreach (var folder in saved)
        {
            this.folders[folder.Id] = folder;
        }

        foreach (var folder in erased)
        {
            if (this.folders.Remove(folder, out var removed) && removed.SourceFolderAlias is { } alias)
            {
                this.erasedSourceAliases.Add(alias);
            }

            this.erased.Add(folder);
        }

        return Task.CompletedTask;
    }

    public Task PlaceAsync(
        IPersistenceSession session,
        MailAccountId account,
        StoredEmailId email,
        LocalMailFolderId folder,
        CancellationToken cancellationToken)
    {
        if (account == this.Account)
        {
            this.placements.TryAdd(email, folder);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Erases a placed message, answering with the placed folder's source alias, or its name where it has none, because
    /// this double keeps no stored rows to read a binding off; a test reads which message went rather than the alias.
    /// </summary>
    public Task<MailFolderAlias?> EraseEmailAsync(
        IPersistenceSession session,
        MailAccountId account,
        StoredEmailId email,
        CancellationToken cancellationToken)
    {
        if (account != this.Account || !this.placements.Remove(email, out var placedIn))
        {
            return Task.FromResult<MailFolderAlias?>(null);
        }

        this.erasedEmails.Add(email);

        var folder = this.folders[placedIn];

        return Task.FromResult<MailFolderAlias?>(folder.SourceFolderAlias ?? MailFolderAlias.Create(folder.Name.Value));
    }

    public Task<LocalMailFolderMailErasure> EraseMailOfErasedFoldersAsync(
        IPersistenceSession session,
        MailAccountId account,
        int maxEmails,
        CancellationToken cancellationToken) =>
        Task.FromResult(new LocalMailFolderMailErasure(ErasedEmailCount: 0, EmailsRemain: false));
}
