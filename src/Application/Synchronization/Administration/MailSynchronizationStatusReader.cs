// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Folders;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Synchronization.Administration;

/// <summary>Answers, in one read, what this deployment's mail synchronization is doing.</summary>
/// <remarks>
/// <para>
/// Composed here rather than by whatever surface asks, because the composition is the answer. Configuration says which
/// accounts and folders exist, the ledger says what the running process is doing with them, and the durable checkpoints
/// say how far each folder has actually come — and the reading an operator needs is the one none of the three gives
/// alone: a folder whose last run keeps ending while its progress does not move is stuck, and a folder whose progress
/// has not moved because there is nothing left to fetch is not.
/// </para>
/// <para>
/// The folder list comes from configuration rather than from the store, so a folder no run has ever reached appears as
/// exactly that instead of being absent. That is the case an operator is most likely to be asking about: an alias that
/// names no advertised folder has stored nothing, and a status surface that answered by omitting it would say nothing
/// about the folder they configured.
/// </para>
/// </remarks>
public sealed class MailSynchronizationStatusReader
{
    private readonly IMailAccountCatalog accounts;
    private readonly IMailFolderParticipationReader folders;
    private readonly MailSynchronizationRunLedger runLedger;
    private readonly IMailFolderSynchronizationProgressReader progressReader;

    /// <summary>Initializes a reader over the four sources one status answer is composed from.</summary>
    /// <param name="accounts">Names the accounts this deployment serves, and whether it synchronizes at all.</param>
    /// <param name="folders">Names the folders configuration maps, and which of them are mirrored.</param>
    /// <param name="runLedger">Reports what the running process's supervisors are doing.</param>
    /// <param name="progressReader">Reports how far each folder's durable progress has come.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailSynchronizationStatusReader(
        IMailAccountCatalog accounts,
        IMailFolderParticipationReader folders,
        MailSynchronizationRunLedger runLedger,
        IMailFolderSynchronizationProgressReader progressReader)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(runLedger);
        ArgumentNullException.ThrowIfNull(progressReader);

        this.accounts = accounts;
        this.folders = folders;
        this.runLedger = runLedger;
        this.progressReader = progressReader;
    }

    /// <summary>Reads what synchronization is doing across every configured account.</summary>
    /// <param name="cancellationToken">Cancels the durable read.</param>
    /// <returns>The status.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <remarks>
    /// It never refuses. A deployment that configures no account, one that switched synchronization off, and one whose
    /// process has only just started are all supported states an operator reads here rather than failures to report on.
    /// </remarks>
    public async Task<MailSynchronizationStatus> ReadAsync(CancellationToken cancellationToken)
    {
        var progress = await this.progressReader.ReadAsync(cancellationToken);
        var progressByFolder = progress.ToDictionary(entry => entry.Folder);
        var mirrored = this.folders.FoldersSynchronized.ToHashSet();
        var mappedByAccount = this.folders.FoldersMapped
            .ToLookup(folder => folder.AccountId);

        return new MailSynchronizationStatus(
            this.accounts.SynchronizationEnabled,
            [
                .. this.accounts.ServedAccounts.Select(account => new MailAccountSynchronizationStatus(
                    account.Id,
                    this.runLedger.ReadAccount(account.Id),
                    this.DescribeFolders(mappedByAccount[account.Id], mirrored, progressByFolder))),
            ]);
    }

    /// <summary>Describes one account's mapped folders, in the ordinal alias order the contract states.</summary>
    private IReadOnlyList<MailFolderSynchronizationStatus> DescribeFolders(
        IEnumerable<MailFolderIdentity> mapped,
        HashSet<MailFolderIdentity> mirrored,
        IReadOnlyDictionary<MailFolderIdentity, MailFolderSynchronizationProgress> progressByFolder) =>
    [
        .. mapped
            .OrderBy(folder => folder.Alias.Value, StringComparer.Ordinal)
            .Select(folder => Describe(
                folder,
                mirrored.Contains(folder),
                progressByFolder.GetValueOrDefault(folder),
                this.runLedger.ReadFolder(folder))),
    ];

    private static MailFolderSynchronizationStatus Describe(
        MailFolderIdentity folder,
        bool mirrored,
        MailFolderSynchronizationProgress? progress,
        MailFolderRunReport? lastRun) =>
        new(
            folder.Alias,
            mirrored,
            progress?.UidValidity,
            progress?.LastSeenUid,
            progress?.AdvancedAt,
            lastRun);
}
