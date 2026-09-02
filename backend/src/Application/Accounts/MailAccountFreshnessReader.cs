// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Synchronization.Administration;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Accounts;

/// <summary>Reads the caller's own accounts as one line each — which account it is and whether its local copy is current — and the same reading for every folder that answers for one.</summary>
/// <remarks>
/// <para>
/// It answers the same question <see cref="MailAccountDirectoryReader" /> does and answers it at a different size. That
/// use case publishes what synchronization durably committed, folder by folder; a reader deciding whether to trust the
/// mailbox needs that reduced to one instant and one word per folder, and to one of each for the account above them.
/// </para>
/// <para>
/// Composed over that use case rather than beside it, so the two cannot come to disagree about which accounts an owner
/// has. Whose accounts these are, which folders count, the grant that is required, and the read that is recorded are all
/// decided there, once; what is added here is the reading none of those sources holds on its own.
/// </para>
/// <para>
/// The account's own state is the reduction of its folders' states rather than a second derivation beside them, which
/// is what keeps a folder tree and the mailbox list above it from disagreeing about the same account. A folder that
/// failed outranks one whose server was unreachable, because a run that also failed some other way reached the server;
/// the account's own run answers for what no folder reports, which is a failure to carry its outstanding mailbox
/// changes.
/// </para>
/// <para>
/// The state is what this process has observed. <see cref="MailSynchronizationRunLedger" /> is deliberately not durable,
/// so a process that has just started reports an account it has not run yet by what its stored progress says rather than
/// by how its runs were going before the restart — which is the honest answer, since the backoff that was failing is not
/// one this process is applying.
/// </para>
/// <para>
/// It reaches no mail server and returns no mail: an account's configured identifier, its display name, its folders'
/// aliases, one instant apiece, and one state apiece are the whole of what a caller receives.
/// </para>
/// </remarks>
public sealed class MailAccountFreshnessReader
{
    private readonly MailAccountDirectoryReader directoryReader;
    private readonly MailSynchronizationRunLedger runLedger;

    /// <summary>Initializes the use case.</summary>
    /// <param name="directoryReader">Reads which accounts the caller's owner owns and how current each of their folders is.</param>
    /// <param name="runLedger">Reports how this process's most recent run of each account and each folder ended.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailAccountFreshnessReader(
        MailAccountDirectoryReader directoryReader,
        MailSynchronizationRunLedger runLedger)
    {
        ArgumentNullException.ThrowIfNull(directoryReader);
        ArgumentNullException.ThrowIfNull(runLedger);

        this.directoryReader = directoryReader;
        this.runLedger = runLedger;
    }

    /// <summary>Reads the caller's accounts and how current each one and each of its folders is.</summary>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>One entry per account the caller's owner owns, and whether the deployment refreshes them.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.MailRead" /> that is acting for an owner.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <remarks>
    /// An owner who owns no account is answered with an empty list rather than a refusal, because owning nothing is a
    /// state a client renders and not a failure. A caller whose grant does not carry the permission is refused instead,
    /// which is what keeps the two answers from arriving as the same empty collection.
    /// </remarks>
    public async Task<MailAccountFreshnessDirectory> ReadAsync(CancellationToken cancellationToken)
    {
        var directory = await this.directoryReader.ReadAsync(cancellationToken);

        return new MailAccountFreshnessDirectory(
            directory.SynchronizationEnabled,
            [.. directory.Accounts.Select(this.Summarize)]);
    }

    /// <summary>Reduces one account's folders and its last run to the facts a reader deciding whether to trust the copy needs.</summary>
    private MailAccountFreshness Summarize(DescribedMailAccount account)
    {
        IReadOnlyList<MailFolderFreshness> folders =
        [
            .. account.Folders.Select(folder => this.Summarize(account.Account.Id, folder)),
        ];

        var lastSynchronizedAt = folders.Max(static folder => folder.SynchronizedAt);
        var lastRunFailed = this.runLedger.ReadAccount(account.Account.Id).LastRun?.Failed is true;

        return new MailAccountFreshness(
            account.Account,
            StateOf(folders, lastRunFailed, lastSynchronizedAt),
            lastSynchronizedAt,
            folders.Any(static folder => folder.IsBehind),
            folders);
    }

    /// <summary>Reduces one folder's durable progress and its last turn through a run to the reading a screen draws it by.</summary>
    private MailFolderFreshness Summarize(MailAccountId accountId, MailboxFolderFreshness folder)
    {
        var lastRun = this.runLedger.ReadFolder(new MailFolderIdentity(accountId, folder.FolderAlias));

        return new MailFolderFreshness(
            folder.FolderAlias,
            StateOf(lastRun, folder.SynchronizedAt),
            folder.SynchronizedAt,
            lastRun?.HasMoreEmails is true);
    }

    /// <summary>Names where one folder stands, from how its last turn ended and how much of it had already been stored.</summary>
    /// <remarks>
    /// A shutdown is not one of the failures. The supervisor counts it under none — an account backed off for every
    /// restart would be one approached less often for being stopped — so a folder interrupted by one reports what its
    /// stored progress says, exactly as a folder no run of this process has taken a turn for does.
    /// </remarks>
    private static MailSynchronizationState StateOf(MailFolderRunReport? lastRun, DateTimeOffset? synchronizedAt) =>
        lastRun?.Outcome switch
        {
            MailFolderRunOutcome.DeferredAfterMailServerUnavailable => MailSynchronizationState.Unreachable,
            MailFolderRunOutcome.AliasUnresolved
                or MailFolderRunOutcome.AliasAmbiguous
                or MailFolderRunOutcome.DeferredAfterConcurrencyConflict
                or MailFolderRunOutcome.UnexpectedFailure => MailSynchronizationState.Failing,
            _ => StoredOrNever(synchronizedAt),
        };

    /// <summary>Names where one account stands, as the worst of its folders and then as what its own run reports.</summary>
    /// <remarks>
    /// The account's own run is read last rather than first, because it says only that something failed and the folders
    /// say what. What it answers for alone is a run that failed while every folder succeeded, which is a mailbox change
    /// this deployment could not carry.
    /// </remarks>
    private static MailSynchronizationState StateOf(
        IReadOnlyList<MailFolderFreshness> folders,
        bool lastRunFailed,
        DateTimeOffset? lastSynchronizedAt)
    {
        if (folders.Any(static folder => folder.State is MailSynchronizationState.Failing))
        {
            return MailSynchronizationState.Failing;
        }

        if (folders.Any(static folder => folder.State is MailSynchronizationState.Unreachable))
        {
            return MailSynchronizationState.Unreachable;
        }

        return lastRunFailed ? MailSynchronizationState.Failing : StoredOrNever(lastSynchronizedAt);
    }

    /// <summary>Separates a copy something has committed progress for from one nothing ever has.</summary>
    private static MailSynchronizationState StoredOrNever(DateTimeOffset? synchronizedAt) => synchronizedAt is null
        ? MailSynchronizationState.NeverSynchronized
        : MailSynchronizationState.Synchronized;
}
