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
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.UserSettings;
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
/// the two about every account, are read from the account records rather than from the roster, because no replica
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
/// The answer about one account follows the roster rather than being read per call: it is published by the startup gate
/// and republished by each record commit, and this recomposes when it changes. A deployment before its gate has run
/// serves the deployment's own posture to whoever asks, which is the answer every mailbox had before any record existed.
/// </para>
/// </remarks>
internal sealed class MailAccountSensitiveContentPostures : ISensitiveContentPostures
{
    private readonly SensitiveContentOptions deployment;
    private readonly IReadOnlyList<ISensitiveContentCatalog> catalogs;
    private readonly Func<IEnumerable<ISensitiveContentScanner>> scanners;
    private readonly TimeProvider timeProvider;
    private readonly SensitiveContentScanConcurrency concurrency;
    private readonly ServedUsers servedUsers;
    private readonly IServedMailAccountReader servedAccounts;
    private readonly Lock mutex = new();

    /// <summary>Every posture an answer read from the account records composed to, bounded by how many distinct answers the scanners allow.</summary>
    private readonly ConcurrentDictionary<EffectivePosture, SensitiveContentPosture> composedAnswers = new();

    /// <summary>What the roster this instance last read composed to, rebuilt when that roster changes.</summary>
    private Composition? composed;

    /// <summary>Initializes the postures of a deployment, whether or not anybody's mail is scanned.</summary>
    /// <param name="deployment">The bound <c>SensitiveContent</c> section, which every posture is composed over.</param>
    /// <param name="catalogs">Every catalog the registered scanners declare.</param>
    /// <param name="scanners">Resolves the registered detectors, and is asked only where a posture runs one.</param>
    /// <param name="timeProvider">Times each scan's budget and stamps its findings.</param>
    /// <param name="concurrency">The process-wide budget of scans running at once, which every posture shares.</param>
    /// <param name="servedUsers">The roster whose records carry what each account asked for.</param>
    /// <param name="servedAccounts">Reads what the accounts every question about more than one account spans asked for.</param>
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
        ServedUsers servedUsers,
        IServedMailAccountReader servedAccounts)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(catalogs);
        ArgumentNullException.ThrowIfNull(scanners);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(concurrency);
        ArgumentNullException.ThrowIfNull(servedUsers);
        ArgumentNullException.ThrowIfNull(servedAccounts);

        this.deployment = deployment;
        this.catalogs = catalogs as IReadOnlyList<ISensitiveContentCatalog> ?? [.. catalogs];
        this.scanners = scanners;
        this.timeProvider = timeProvider;
        this.concurrency = concurrency;
        this.servedUsers = servedUsers;
        this.servedAccounts = servedAccounts;
    }

    /// <inheritdoc />
    public async Task<bool> IsActiveForAnyAccountAsync(CancellationToken cancellationToken)
    {
        if (this.Composed().Deployment.IsActive)
        {
            return true;
        }

        var requests = await this.servedAccounts.ReadScanningRequestsAsync(user: null, cancellationToken);

        return requests.Any(request => this.PostureOf(this.Compose(request)).IsActive);
    }

    /// <inheritdoc />
    public IReadOnlyList<MailAccountSensitiveContentPosture> Current => this.Composed().Accounts;

    /// <inheritdoc />
    public SensitiveContentPosture ForAccount(MailAccountId account)
    {
        var current = this.Composed();

        return current.ByAccount.TryGetValue(account, out var posture) ? posture : current.Deployment;
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
        if (this.Composed().Deployment.Runs(scanner))
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

    /// <summary>Composes what one account asked for, as its own declaration states it.</summary>
    private static EffectivePosture Compose(
        SensitiveContentOptions deployment,
        MailAccountSensitiveContentOptions account) =>
        Compose(
            deployment,
            [.. Enum.GetValues<SensitiveContentScannerKind>().Where(scanner => account.For(scanner).Enabled is true)],
            account.ScreenedScanners);

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

    /// <summary>Reads the composition, rebuilding it when the roster it was composed from has been replaced.</summary>
    /// <remarks>
    /// The roster the last composition was built from is what says whether it is still current, compared by identity
    /// because the roster is replaced whole rather than edited. Rebuilding on read rather than from a change-token
    /// callback is what keeps a posture from being composed against a roster the startup gate has not finished
    /// settling: the first call after a change pays for the rebuild, and every other call reads a finished answer.
    /// </remarks>
    private Composition Composed()
    {
        lock (this.mutex)
        {
            var roster = this.servedUsers.TryGetUsers();

            if (this.composed is { } current && ReferenceEquals(current.Roster, roster))
            {
                return current;
            }

            this.composed = this.Build(roster);

            return this.composed;
        }
    }

    /// <summary>Builds every posture one roster produces, sharing one redaction between accounts that read the same one.</summary>
    /// <remarks>
    /// An account assigned to two users is one mailbox with one record, so it is composed once and reached under one
    /// answer however many people it is declared for. An account whose identifier is unusable is passed over, because
    /// nothing that asks here could name it.
    /// </remarks>
    private Composition Build(IReadOnlyList<ServedUser>? roster)
    {
        var built = new Dictionary<EffectivePosture, SensitiveContentPosture>();
        var deploymentAnswer = Compose(this.deployment, [], []);
        var deploymentPosture = this.PostureOf(built, deploymentAnswer);

        if (roster is null)
        {
            return new Composition(
                null,
                deploymentPosture,
                new Dictionary<MailAccountId, SensitiveContentPosture>(),
                []);
        }

        var declared = roster
            .SelectMany(served => served.MailAccounts.Select(account => new
            {
                Account = MailSynchronizationOptions.TryReadAccountId(account.AccountId),
                Answer = Compose(this.deployment, account.SensitiveContent),
            }))
            .Where(entry => entry.Account is not null)
            .ToArray();

        var byAccount = declared
            .DistinctBy(entry => entry.Account, StringComparer.Ordinal)
            .ToDictionary(
                entry => MailAccountId.Create(entry.Account!),
                entry => this.PostureOf(built, entry.Answer));

        return new Composition(
            roster,
            deploymentPosture,
            byAccount,
            [.. byAccount
                .OrderBy(entry => entry.Key.Value, StringComparer.Ordinal)
                .Select(entry => new MailAccountSensitiveContentPosture(entry.Key, entry.Value))]);
    }

    /// <summary>Finds the posture one effective answer produces, composing it the first time that answer is met.</summary>
    private SensitiveContentPosture PostureOf(
        Dictionary<EffectivePosture, SensitiveContentPosture> built,
        EffectivePosture effective)
    {
        if (built.TryGetValue(effective, out var already))
        {
            return already;
        }

        var composed = this.PostureFor(effective);

        built[effective] = composed;

        return composed;
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

    /// <summary>What one roster composed to, held whole so a reader never observes a half-rebuilt set.</summary>
    /// <param name="Roster">The roster this was built from, which is what says whether it is still current.</param>
    /// <param name="Deployment">The posture of an account this roster does not name, which is the deployment's own.</param>
    /// <param name="ByAccount">The posture of each account the roster declares.</param>
    /// <param name="Accounts">The account postures as an ordered list, for the walk that judges every account's rows at once.</param>
    private sealed record Composition(
        IReadOnlyList<ServedUser>? Roster,
        SensitiveContentPosture Deployment,
        IReadOnlyDictionary<MailAccountId, SensitiveContentPosture> ByAccount,
        IReadOnlyList<MailAccountSensitiveContentPosture> Accounts);
}
