// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Coordination;
using MailFathom.Application.Emails.AttachmentText.Administration;
using MailFathom.Application.Folders;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
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
/// <para>
/// The lease table is the fourth source, and it is what makes the answer the deployment's rather than this process's.
/// An account is supervised by exactly one replica, so a replica that holds none of them would otherwise report every
/// mailbox as never run — which is the reading an operator gets today from whichever pod their request reached. What
/// the ledger says is kept for the accounts this replica actually holds, and reported as another replica's business for
/// the rest.
/// </para>
/// </remarks>
public sealed class MailSynchronizationStatusReader
{
    private readonly IDeploymentMailAccountCatalog accounts;
    private readonly IMailFolderParticipationReader folders;
    private readonly MailSynchronizationRunLedger runLedger;
    private readonly IMailFolderSynchronizationProgressReader progressReader;
    private readonly IAttachmentDerivationCoverageReader attachmentCoverage;
    private readonly IWorkLeaseStore leases;
    private readonly ReplicaIdentity replica;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes a reader over the five sources one status answer is composed from.</summary>
    /// <param name="accounts">Names the accounts this deployment serves, and whether it synchronizes at all.</param>
    /// <param name="folders">Names the folders configuration maps, and which of them are mirrored.</param>
    /// <param name="runLedger">Reports what the running process's supervisors are doing.</param>
    /// <param name="progressReader">Reports how far each folder's durable progress has come.</param>
    /// <param name="attachmentCoverage">Counts how far reading each account's attachments has come.</param>
    /// <param name="leases">Reports which replica is supervising each account, which is the half no process knows about itself.</param>
    /// <param name="replica">Names the replica answering, which is what decides whether the ledger describes an account at all.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailSynchronizationStatusReader(
        IDeploymentMailAccountCatalog accounts,
        IMailFolderParticipationReader folders,
        MailSynchronizationRunLedger runLedger,
        IMailFolderSynchronizationProgressReader progressReader,
        IAttachmentDerivationCoverageReader attachmentCoverage,
        IWorkLeaseStore leases,
        ReplicaIdentity replica,
        AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(runLedger);
        ArgumentNullException.ThrowIfNull(progressReader);
        ArgumentNullException.ThrowIfNull(attachmentCoverage);
        ArgumentNullException.ThrowIfNull(leases);
        ArgumentNullException.ThrowIfNull(replica);
        ArgumentNullException.ThrowIfNull(authorization);

        this.accounts = accounts;
        this.folders = folders;
        this.runLedger = runLedger;
        this.progressReader = progressReader;
        this.attachmentCoverage = attachmentCoverage;
        this.leases = leases;
        this.replica = replica;
        this.authorization = authorization;
    }

    /// <summary>Reads what synchronization is doing across every configured account.</summary>
    /// <param name="cancellationToken">Cancels the durable read.</param>
    /// <returns>The status.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.AdminRead" />.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <remarks>
    /// It refuses nothing about the deployment's own state. A deployment that configures no account, one that switched
    /// synchronization off, and one whose process has only just started are all supported states an operator reads here
    /// rather than failures to report on — what it does refuse is a caller whose grant does not carry the permission
    /// this report is published under.
    /// </remarks>
    public async Task<MailSynchronizationStatus> ReadAsync(CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminRead);

        var progress = await this.progressReader.ReadAsync(cancellationToken);
        var progressByFolder = progress.ToDictionary(entry => entry.Folder);
        var mirrored = this.folders.FoldersSynchronized.ToHashSet();
        var mappedByAccount = this.folders.FoldersMapped
            .ToLookup(folder => folder.AccountId);
        var supervisionByAccount = await this.ReadSupervisionAsync(cancellationToken);

        // Counted per account rather than once for the deployment, because that is the scope this answer is read at:
        // an operator looking at one account's folders needs the attachment figure for that account beside them. The
        // accounts a deployment serves are the handful configuration names, so this is one aggregate each rather than
        // an unbounded fan-out, and awaiting them in order is what keeps them on one scoped database context.
        List<MailAccountSynchronizationStatus> accountStatuses = [];

        foreach (var account in this.accounts.ServedAccounts)
        {
            var supervision = supervisionByAccount.GetValueOrDefault(account.Identity);

            accountStatuses.Add(new MailAccountSynchronizationStatus(
                account.Id,
                supervision,
                this.DescribeRun(account.Id, supervision),
                this.DescribeFolders(mappedByAccount[account.Id], mirrored, progressByFolder),
                await this.attachmentCoverage.ReadCoverageAsync(account.Identity, cancellationToken)));
        }

        return new MailSynchronizationStatus(
            this.replica,
            this.accounts.SynchronizationEnabled,
            accountStatuses);
    }

    /// <summary>Reads which replica holds each served account's supervision, in one question to the lease table.</summary>
    /// <remarks>
    /// Keyed by the scope the coordinator takes the hold under, so the two cannot disagree about what one account's
    /// supervision is called. A scope no row answers for is an account nothing is supervising, which is a supported
    /// state rather than an absence to interpret: a deployment with synchronization switched off holds none of them.
    /// </remarks>
    private async Task<Dictionary<MailAccountIdentity, MailAccountSupervision>> ReadSupervisionAsync(
        CancellationToken cancellationToken)
    {
        var accountsByScope = this.accounts.ServedAccounts
            .ToDictionary(account => MailAccountSupervisionScope.For(account.Identity), account => account.Identity);

        var held = await this.leases.ReadHeldAsync(accountsByScope.Keys, cancellationToken);

        return held
            .Where(lease => accountsByScope.ContainsKey(lease.Scope))
            .ToDictionary(
                lease => accountsByScope[lease.Scope],
                lease => new MailAccountSupervision(lease.Replica, lease.ExpiresAt));
    }

    /// <summary>Describes one account's run, which is this replica's own loop wherever it is the replica supervising it.</summary>
    /// <remarks>
    /// An account another replica holds reports the phase that says so and nothing further, rather than whatever this
    /// replica's ledger still remembers: the ledger keeps what it last recorded for an account whose hold has since
    /// moved, and reporting that would name a backoff this deployment stopped applying when the hold moved.
    /// </remarks>
    private MailAccountRunState DescribeRun(MailAccountId accountId, MailAccountSupervision? supervision) =>
        supervision is { } elsewhere && elsewhere.Replica != this.replica
            ? MailAccountRunState.SupervisedElsewhere
            : this.runLedger.ReadAccount(accountId);

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
