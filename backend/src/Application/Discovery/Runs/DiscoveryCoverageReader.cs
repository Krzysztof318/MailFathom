// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Synchronization.Administration;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Discovery.Runs;

/// <summary>Reads what one run drew on: which accounts its scope reached, how far the mail it found reached, and how current each account's local copy was.</summary>
/// <remarks>
/// <para>
/// A Discover answer is composed from a synchronized copy, so how current that copy was is part of the answer. This
/// reduces the same two sources <see cref="Accounts.MailAccountFreshnessReader" /> does — what synchronization durably
/// committed, and how this process's most recent runs ended — to one reading per account, in the shape a plan carries.
/// </para>
/// <para>
/// The accounts are the scope's rather than the passages', which is deliberate. A question asked over two mailboxes
/// read both, and reporting only the one that happened to answer would hide exactly the account whose staleness might
/// be why it did not — so an account that yielded nothing is reported with its freshness and with no dates.
/// </para>
/// <para>
/// It reaches no mail server and returns no mail: an account's configured identifier, one instant, and the two ends of
/// what the run drew on are the whole of what a caller receives. It applies no permission of its own, because it is
/// composed inside a run that has already required its grant and is bounded by that run's own scope.
/// </para>
/// </remarks>
public sealed class DiscoveryCoverageReader
{
    private readonly ISynchronizationFreshnessReader freshnessReader;
    private readonly MailSynchronizationRunLedger runLedger;

    /// <summary>Creates the reading a run reports its own reach through.</summary>
    /// <param name="freshnessReader">Reads how current the local copy of each folder in the run's scope is.</param>
    /// <param name="runLedger">Reports how this process's most recent run of each account and each folder ended.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public DiscoveryCoverageReader(
        ISynchronizationFreshnessReader freshnessReader,
        MailSynchronizationRunLedger runLedger)
    {
        ArgumentNullException.ThrowIfNull(freshnessReader);
        ArgumentNullException.ThrowIfNull(runLedger);

        this.freshnessReader = freshnessReader;
        this.runLedger = runLedger;
    }

    /// <summary>Reads one coverage entry per account the run's scope reached.</summary>
    /// <param name="scope">The scope the run retrieved within.</param>
    /// <param name="passages">What the run found, which is where the dates come from.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>One entry per account, ordered by the account's identifier.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> or <paramref name="passages" /> is <see langword="null" />.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <remarks>
    /// Bounded by <see cref="PresentationPlan.MaxAccountsCovered" /> rather than by the scope, so a deployment serving
    /// more mailboxes than a plan may report produces a plan rather than a refusal. The surplus accounts go unreported
    /// and the ordering is what decides which, which is why it is the account identifier rather than anything about the
    /// mail: a run reports the same accounts every time it is asked. A deployment past that bound is serving something
    /// other than one person's correspondence, which is not what a Discover run is bounded for.
    /// </remarks>
    public async Task<IReadOnlyList<AccountCoverage>> ReadAsync(
        MailboxScope scope,
        IReadOnlyList<EmailKnowledgePassage> passages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(passages);

        var folders = await this.freshnessReader.ReadAsync(scope, cancellationToken);
        var found = passages
            .Where(passage => passage.ReceivedAt is not null)
            .GroupBy(passage => passage.AccountId)
            .ToDictionary(
                group => group.Key,
                group => (
                    Earliest: group.Min(passage => passage.ReceivedAt),
                    Latest: group.Max(passage => passage.ReceivedAt)));

        return
        [
            .. folders
                .GroupBy(folder => folder.AccountId)
                .OrderBy(account => account.Key.Value, StringComparer.Ordinal)
                .Take(PresentationPlan.MaxAccountsCovered)
                .Select(account => this.Summarize(account.Key, [.. account], found)),
        ];
    }

    private AccountCoverage Summarize(
        MailAccountId accountId,
        IReadOnlyList<MailboxFolderFreshness> folders,
        Dictionary<MailAccountId, (DateTimeOffset? Earliest, DateTimeOffset? Latest)> found)
    {
        var drewOn = found.TryGetValue(accountId, out var dates) ? dates : (Earliest: null, Latest: null);

        return new AccountCoverage(
            PresentationText.Create(accountId.Value),
            this.FreshnessOf(accountId, folders),
            drewOn.Earliest,
            drewOn.Latest);
    }

    /// <summary>Reduces one account's folders and its last run to how current a reader should take its copy to be.</summary>
    /// <remarks>
    /// The timestamp is the newest of the account's folders, for the reason the account directory gives: it answers
    /// "when did this mailbox last take anything in", and a folder that has been empty since it was mapped would
    /// otherwise hold the whole account at the beginning of time.
    /// <para>
    /// Behind means something known rather than something guessed. A folder whose last turn left mail it had not taken
    /// in, or an account whose own run failed, is behind the mail server as a fact this process observed; an account
    /// nothing has run recently is not thereby behind, and no elapsed time is turned into staleness here — a mailbox
    /// nobody has written to for a day and one that is a day behind are different situations, and only the second is
    /// this deployment's to report.
    /// </para>
    /// </remarks>
    private PresentationFreshness FreshnessOf(MailAccountId accountId, IReadOnlyList<MailboxFolderFreshness> folders)
    {
        if (folders.Max(folder => folder.SynchronizedAt) is not { } synchronizedAt)
        {
            return PresentationFreshness.Unknown;
        }

        var isBehind = this.runLedger.ReadAccount(accountId).LastRun?.Failed is true
            || folders.Any(folder =>
                this.runLedger.ReadFolder(new MailFolderIdentity(accountId, folder.FolderAlias))?.HasMoreEmails is true);

        return isBehind
            ? PresentationFreshness.StaleSince(synchronizedAt)
            : PresentationFreshness.CurrentAt(synchronizedAt);
    }
}
