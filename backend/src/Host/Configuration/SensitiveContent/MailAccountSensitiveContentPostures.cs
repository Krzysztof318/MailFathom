// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Derivation;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Redaction;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Host.Hosting.Workers;
using MailFathom.Infrastructure.Persistence.Users.AccountSettings;

namespace MailFathom.Host.Configuration.SensitiveContent;

/// <summary>Composes each account's scanning posture from the deployment's section and that account's own record.</summary>
/// <remarks>
/// <para>
/// The composition takes the stricter of the two in one direction only: a scanner the deployment switched on runs over
/// every mailbox whatever an account's record says, and a scanner it left off runs over the mail of whichever accounts
/// asked for it. The same holds for what stops an outgoing message, where the two lists are unioned. Nothing here
/// refuses a loosening, because nothing here is where a record is written —
/// <see cref="MailAccountSensitiveContentRules" /> refuses one at the write, so what reaches this is already a record
/// whose own words describe what is in force.
/// </para>
/// <para>
/// A user's own answer is composed beside the accounts', as the union over the accounts they are assigned, for the one
/// reader that cannot name an account: a read spanning a person's whole mail. It is the strictest of their mailboxes
/// rather than any one of them, which only ever redacts more than the message's own account asked for. That answer, and
/// the two about every account, are read from the account records, because no replica
/// holds every account to walk: what is read is the handful of distinct answers the accounts gave, never the accounts.
/// </para>
/// <para>
/// Postures are composed once per distinct answer rather than once per account, so a deployment whose accounts all read
/// the deployment's own posture holds one redaction and one stamp however many mailboxes it serves. The detectors
/// behind them are the ones the composition root registered, constructed once for the scanners this deployment provides
/// and shared by every posture that runs one, and the permits are the process's single
/// <see cref="SensitiveContentScanConcurrency" />.
/// </para>
/// <para>
/// The answer about one account is read from that account's record and reused for <see cref="Freshness" />, which is the
/// interval within which every other replica takes a committed record as well: one account's mail is read message by
/// message, and a read per message would cost a statement each. Nothing here holds every account, so what a replica
/// keeps follows the accounts it is working on.
/// </para>
/// </remarks>
internal sealed class MailAccountSensitiveContentPostures : ISensitiveContentPostures
{
    private readonly SensitiveContentOptions deployment;
    private readonly IReadOnlyList<ISensitiveContentCatalog> catalogs;
    private readonly Func<IEnumerable<ISensitiveContentScanner>> scanners;
    private readonly TimeProvider timeProvider;
    private readonly SensitiveContentScanConcurrency concurrency;
    private readonly IServedMailAccountReader servedAccounts;

    /// <summary>Every posture an answer read from the account records composed to, bounded by how many distinct answers the scanners allow.</summary>
    private readonly ConcurrentDictionary<EffectivePosture, SensitiveContentPosture> composedAnswers = new();

    /// <summary>The answer about each account read within <see cref="Freshness" />, and the moment it was read.</summary>
    private readonly ConcurrentDictionary<MailAccountId, HeldPosture> byAccount = new();

    /// <summary>The moment the latest reading of every account began; an answer whose read did not begin after it is read again rather than reused.</summary>
    private long everyAccountReadFrom;

    /// <summary>Initializes the postures of a deployment, whether or not anybody's mail is scanned.</summary>
    /// <param name="deployment">The bound <c>SensitiveContent</c> section, which every posture is composed over.</param>
    /// <param name="catalogs">Every catalog the registered scanners declare.</param>
    /// <param name="scanners">Resolves the registered detectors, and is asked only where a posture runs one.</param>
    /// <param name="timeProvider">Times each scan's budget and stamps its findings.</param>
    /// <param name="concurrency">The process-wide budget of scans running at once, which every posture shares.</param>
    /// <param name="servedAccounts">Reads what each account asked for, from the account records.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// The detectors arrive behind a delegate rather than as a resolved sequence, because resolving them constructs a
    /// regular-expression corpus and an analyzer client. A deployment where nobody is scanned for composes no posture
    /// and therefore never asks, which is what keeps an opt-in nobody took free of the cost of the opt-in existing.
    /// </remarks>
    public MailAccountSensitiveContentPostures(
        SensitiveContentOptions deployment,
        IEnumerable<ISensitiveContentCatalog> catalogs,
        Func<IEnumerable<ISensitiveContentScanner>> scanners,
        TimeProvider timeProvider,
        SensitiveContentScanConcurrency concurrency,
        IServedMailAccountReader servedAccounts)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(catalogs);
        ArgumentNullException.ThrowIfNull(scanners);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(concurrency);
        ArgumentNullException.ThrowIfNull(servedAccounts);

        this.deployment = deployment;
        this.catalogs = catalogs as IReadOnlyList<ISensitiveContentCatalog> ?? [.. catalogs];
        this.scanners = scanners;
        this.timeProvider = timeProvider;
        this.concurrency = concurrency;
        this.servedAccounts = servedAccounts;
        this.Deployment = this.PostureOf(Compose(deployment, [], []));
    }

    /// <summary>How long an answer about one account is reused before its record is read again.</summary>
    internal static TimeSpan Freshness => ConfigurationConvergenceWorker.Interval;

    /// <summary>How many accounts' answers are held before the expired ones are let go.</summary>
    internal const int HeldAccountsBeforeSweep = 4096;

    /// <inheritdoc />
    public SensitiveContentPosture Deployment { get; }

    /// <inheritdoc />
    public async Task<bool> IsActiveForAnyAccountAsync(CancellationToken cancellationToken)
    {
        if (this.Deployment.IsActive)
        {
            return true;
        }

        var requests = await this.servedAccounts.ReadScanningRequestsAsync(user: null, cancellationToken);

        return requests.Any(request => this.PostureOf(this.Compose(request)).IsActive);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The reading also retires every answer about one account read before it began, because a rebuild walks with
    /// both: it decides which rows are stale against this list and re-derives each through
    /// <see cref="ForAccountAsync" />. An answer held from before an account's last commit would re-derive a row under
    /// the posture the list has just moved past, stamp it with the stamp the walk is leaving behind, and let the cursor
    /// step over it — and so would a read of one account that began before the list's and lands after it. Each such
    /// account is read again the first time the walk asks, so the cache still holds only the accounts this replica works
    /// on.
    /// </remarks>
    public async Task<IReadOnlyList<MailAccountSensitiveContentPosture>> ReadAccountsBeyondDeploymentAsync(
        CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref this.everyAccountReadFrom, this.timeProvider.GetTimestamp());

        var declarations = await this.servedAccounts.ReadAccountsRequestingScanningAsync(cancellationToken);

        return
        [
            .. declarations
                .Select(declared => new MailAccountSensitiveContentPosture(declared.Account, this.PostureOf(this.Compose(declared.Request))))
                .Where(account => !ReferenceEquals(account.Posture, this.Deployment))
                .OrderBy(static account => account.Account.Value, StringComparer.Ordinal),
        ];
    }

    /// <inheritdoc />
    /// <remarks>
    /// An answer is stamped with the moment its read began, and one held from a read that began later is kept over it:
    /// two reads of one account can land in either order, and the later-landing one may have read the record from
    /// before a commit the other already saw.
    /// </remarks>
    public async Task<SensitiveContentPosture> ForAccountAsync(MailAccountId account, CancellationToken cancellationToken)
    {
        if (this.byAccount.TryGetValue(account, out var held)
            && held.ReadAt > Interlocked.Read(ref this.everyAccountReadFrom)
            && this.timeProvider.GetElapsedTime(held.ReadAt) < Freshness)
        {
            return held.Posture;
        }

        var readStartedAt = this.timeProvider.GetTimestamp();
        var request = await this.servedAccounts.ReadScanningRequestAsync(account, cancellationToken);
        var answer = new HeldPosture(request is null ? this.Deployment : this.PostureOf(this.Compose(request)), readStartedAt);

        this.LetGoOfExpiredAnswers();

        return this.byAccount
            .AddOrUpdate(account, answer, (_, current) => current.ReadAt > readStartedAt ? current : answer)
            .Posture;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The deployment's own answer is one of those composed, so a user assigned no account composes to it exactly as
    /// one whose accounts asked for nothing does.
    /// </remarks>
    public async Task<SensitiveContentPosture> AcrossAccountsOfAsync(UserId user, CancellationToken cancellationToken)
    {
        var requests = await this.servedAccounts.ReadScanningRequestsAsync(user, cancellationToken);

        return this.PostureOf(StrictestOf(
        [
            Compose(this.deployment, [], []),
            .. requests.Select(this.Compose),
        ]));
    }

    /// <inheritdoc />
    public async Task<bool> RunsForAnyAccountAsync(
        SensitiveContentScannerKind scanner,
        CancellationToken cancellationToken)
    {
        if (this.Deployment.Runs(scanner))
        {
            return true;
        }

        var requests = await this.servedAccounts.ReadScanningRequestsAsync(user: null, cancellationToken);

        return requests.Any(request => this.PostureOf(this.Compose(request)).Runs(scanner));
    }

    /// <summary>Composes what one account asked for over the deployment's own answer, in the one direction allowed.</summary>
    /// <remarks>
    /// <para>
    /// A scanner is on where either side switched it on, and the screening list is the union of the two. Both are the
    /// same rule read twice: the deployment's answer is a floor, and an account's record can only stand on it.
    /// </para>
    /// <para>
    /// An account's opt-in reaches only what the deployment provides. <see cref="MailAccountSensitiveContentRules" />
    /// refuses the other case at the write, but a record accepted while an analyzer was configured outlives the
    /// operator removing that address, and honouring it then would compose a plan naming a scanner no detector is
    /// registered for — which would throw out of here and take every scanning path on the deployment with it. The
    /// deployment's own switch needs no such guard: startup validation already refuses it.
    /// </para>
    /// </remarks>
    private static EffectivePosture Compose(
        SensitiveContentOptions deployment,
        IReadOnlyCollection<SensitiveContentScannerKind> accountScansFor,
        IReadOnlyCollection<SensitiveContentScannerKind> accountScreensOutgoingMailFor)
    {
        var provided = deployment.ProvidedScanners;
        var switchedOn = Enum.GetValues<SensitiveContentScannerKind>()
            .Where(scanner => deployment.For(scanner).Enabled
                || (accountScansFor.Contains(scanner) && provided.Contains(scanner)))
            .ToArray();

        var screening = SensitiveContentPlanMapper.ScreeningScannersOf(deployment)
            .Concat(accountScreensOutgoingMailFor)
            .Distinct()
            .Order()
            .ToArray();

        return new EffectivePosture(switchedOn, screening);
    }

    /// <summary>Composes one distinct answer read from the account records over the deployment's own.</summary>
    private EffectivePosture Compose(MailAccountScanningRequest request) =>
        Compose(this.deployment, [.. request.ScansFor], [.. request.ScreensOutgoingMailFor]);

    /// <summary>Finds the posture one effective answer read from the account records produces, composing it the first time that answer is met.</summary>
    private SensitiveContentPosture PostureOf(EffectivePosture effective) =>
        this.composedAnswers.GetOrAdd(effective, this.PostureFor);

    /// <summary>Composes the strictest of several answers, which is what a read spanning accounts is judged by.</summary>
    /// <remarks>
    /// The deployment's own answer is one of them rather than a special case, so a user assigned no account at all
    /// composes to it exactly as one whose accounts asked for nothing does.
    /// </remarks>
    private static EffectivePosture StrictestOf(IEnumerable<EffectivePosture> answers)
    {
        var composed = answers.ToArray();

        return new EffectivePosture(
            [.. composed.SelectMany(answer => answer.SwitchedOn).Distinct().Order()],
            [.. composed.SelectMany(answer => answer.Screening).Distinct().Order()]);
    }

    /// <summary>Lets go of every answer read longer ago than <see cref="Freshness" />, once enough accounts are held for that to matter.</summary>
    /// <remarks>
    /// ponytail: a sweep over every held answer whenever the count passes the threshold; a replica touching more distinct
    /// accounts than that within one interval sweeps on every miss, and a bounded cache with its own eviction is the
    /// upgrade if that ever shows up in a profile.
    /// </remarks>
    private void LetGoOfExpiredAnswers()
    {
        if (this.byAccount.Count < HeldAccountsBeforeSweep)
        {
            return;
        }

        foreach (var expired in this.byAccount.Where(entry => this.timeProvider.GetElapsedTime(entry.Value.ReadAt) >= Freshness).ToArray())
        {
            this.byAccount.TryRemove(expired);
        }
    }

    /// <summary>Composes one posture, which constructs a redaction only where something is switched on.</summary>
    private SensitiveContentPosture PostureFor(EffectivePosture effective)
    {
        if (SensitiveContentPlanMapper.Map(this.deployment, this.catalogs, effective.SwitchedOn) is not { } plan)
        {
            return SensitiveContentPosture.ScanningNothing;
        }

        var registered = this.scanners().ToArray();

        return SensitiveContentPosture.Scanning(
            effective.SwitchedOn,
            new SensitiveContentRedactor(plan, registered, this.timeProvider, this.concurrency),
            SensitiveContentPlanMapper.MapScreeningPolicy(plan, effective.Screening),
            SensitiveContentDerivationStamp.Compute(plan, registered));
    }

    /// <summary>One answer about one mailbox's mail, which two accounts asking the same thing share a posture for.</summary>
    /// <param name="SwitchedOn">Which scanners run, deployment and account composed.</param>
    /// <param name="Screening">Which of their findings stop a message sent from it, deployment and account composed.</param>
    private readonly record struct EffectivePosture(
        SensitiveContentScannerKind[] SwitchedOn,
        SensitiveContentScannerKind[] Screening)
    {
        /// <inheritdoc />
        /// <remarks>
        /// Compared by what the two arrays hold rather than by their identity, which is the whole point of the key: two
        /// mailboxes that asked for the same thing must meet the same posture rather than compose one each — and a
        /// deployment serving a mailbox per user asks this once per account rather than once per person.
        /// </remarks>
        public bool Equals(EffectivePosture other) =>
            this.SwitchedOn.SequenceEqual(other.SwitchedOn) && this.Screening.SequenceEqual(other.Screening);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            var hash = default(HashCode);

            foreach (var scanner in this.SwitchedOn)
            {
                hash.Add(scanner);
            }

            foreach (var scanner in this.Screening)
            {
                // Mixed in under a marker of its own, so a scanner that is switched on and a scanner that screens do
                // not fold into one hash for two different answers.
                hash.Add(-(int)scanner - 1);
            }

            return hash.ToHashCode();
        }
    }

    /// <summary>One account's answer and the timestamp it was read at.</summary>
    /// <param name="Posture">What the account's mail is scanned under.</param>
    /// <param name="ReadAt">The <see cref="TimeProvider.GetTimestamp" /> reading taken when the record was read.</param>
    private sealed record HeldPosture(SensitiveContentPosture Posture, long ReadAt);
}
