// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Derivation;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Redaction;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.OwnerSettings;

namespace MailFathom.Host.Configuration.SensitiveContent;

/// <summary>Composes each owner's scanning posture from the deployment's section and that owner's own record.</summary>
/// <remarks>
/// <para>
/// The composition takes the stricter of the two in one direction only: a scanner the deployment switched on runs over
/// every owner's mail whatever their record says, and a scanner it left off runs over the mail of whoever asked for it.
/// The same holds for what stops an outgoing message, where the two lists are unioned. Nothing here refuses a
/// loosening, because nothing here is where a record is written — <see cref="OwnerSensitiveContentRules" /> refuses one
/// at the write, so what reaches this is already a record whose own words describe what is in force.
/// </para>
/// <para>
/// Postures are composed once per distinct answer rather than once per owner, so a deployment whose owners all read the
/// deployment's own posture holds one redaction and one stamp however many people it serves. The detectors behind them
/// are the ones the composition root registered, constructed once for the scanners this deployment provides and shared
/// by every posture that runs one, and the permits are the process's single
/// <see cref="SensitiveContentScanConcurrency" />.
/// </para>
/// <para>
/// The roster is followed rather than read per call: it is published by the startup gate and republished by each
/// owner-document commit, and this recomposes when it changes. A deployment before its gate has run serves the
/// deployment's own posture to whoever asks, which is the answer every owner had before any record existed.
/// </para>
/// </remarks>
internal sealed class OwnerSensitiveContentPostures : ISensitiveContentPostures
{
    private readonly SensitiveContentOptions deployment;
    private readonly IReadOnlyList<ISensitiveContentCatalog> catalogs;
    private readonly Func<IEnumerable<ISensitiveContentScanner>> scanners;
    private readonly TimeProvider timeProvider;
    private readonly SensitiveContentScanConcurrency concurrency;
    private readonly ServedMailOwners servedOwners;
    private readonly Lock mutex = new();

    /// <summary>What the roster this instance last read composed to, rebuilt when that roster changes.</summary>
    private Composition? composed;

    /// <summary>Initializes the postures of a deployment, whether or not anybody's mail is scanned.</summary>
    /// <param name="deployment">The bound <c>SensitiveContent</c> section, which every posture is composed over.</param>
    /// <param name="catalogs">Every catalog the registered scanners declare.</param>
    /// <param name="scanners">Resolves the registered detectors, and is asked only where a posture runs one.</param>
    /// <param name="timeProvider">Times each scan's budget and stamps its findings.</param>
    /// <param name="concurrency">The process-wide budget of scans running at once, which every posture shares.</param>
    /// <param name="servedOwners">The roster whose records carry what each owner asked for.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// The detectors arrive behind a delegate rather than as a resolved sequence, because resolving them constructs a
    /// regular-expression corpus and an analyzer client. A deployment where nobody is scanned for composes no posture
    /// and therefore never asks, which is what keeps an opt-in nobody took free of the cost of the opt-in existing.
    /// </remarks>
    public OwnerSensitiveContentPostures(
        SensitiveContentOptions deployment,
        IEnumerable<ISensitiveContentCatalog> catalogs,
        Func<IEnumerable<ISensitiveContentScanner>> scanners,
        TimeProvider timeProvider,
        SensitiveContentScanConcurrency concurrency,
        ServedMailOwners servedOwners)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(catalogs);
        ArgumentNullException.ThrowIfNull(scanners);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(concurrency);
        ArgumentNullException.ThrowIfNull(servedOwners);

        this.deployment = deployment;
        this.catalogs = catalogs as IReadOnlyList<ISensitiveContentCatalog> ?? [.. catalogs];
        this.scanners = scanners;
        this.timeProvider = timeProvider;
        this.concurrency = concurrency;
        this.servedOwners = servedOwners;
    }

    /// <inheritdoc />
    public bool IsActiveForAnyOwner => this.Composed().IsActiveForAnyOwner;

    /// <inheritdoc />
    public IReadOnlyList<OwnerSensitiveContentPosture> Current => this.Composed().Owners;

    /// <inheritdoc />
    public SensitiveContentPosture ForOwner(MailOwnerId owner)
    {
        var current = this.Composed();

        return current.ByOwner.TryGetValue(owner, out var posture) ? posture : current.Deployment;
    }

    /// <inheritdoc />
    public bool RunsForAnyOwner(SensitiveContentScannerKind scanner)
    {
        var current = this.Composed();

        return current.Deployment.Runs(scanner) || current.Owners.Any(owner => owner.Posture.Runs(scanner));
    }

    /// <summary>Composes what one owner asked for over the deployment's own answer, in the one direction allowed.</summary>
    /// <remarks>
    /// <para>
    /// A scanner is on where either side switched it on, and the screening list is the union of the two. Both are the
    /// same rule read twice: the deployment's answer is a floor, and an owner's record can only stand on it.
    /// </para>
    /// <para>
    /// An owner's opt-in reaches only what the deployment provides. <see cref="OwnerSensitiveContentRules" /> refuses
    /// the other case at the write, but a record accepted while an analyzer was configured outlives the operator
    /// removing that address, and honouring it then would compose a plan naming a scanner no detector is registered
    /// for — which would throw out of here and take every scanning path on the deployment with it. The deployment's
    /// own switch needs no such guard: startup validation already refuses it.
    /// </para>
    /// </remarks>
    private static EffectivePosture Compose(
        SensitiveContentOptions deployment,
        OwnerSensitiveContentOptions? owner)
    {
        var provided = deployment.ProvidedScanners;
        var switchedOn = Enum.GetValues<SensitiveContentScannerKind>()
            .Where(scanner => deployment.For(scanner).Enabled
                || (owner?.For(scanner).Enabled is true && provided.Contains(scanner)))
            .ToArray();

        var screening = SensitiveContentPlanMapper.ScreeningScannersOf(deployment)
            .Concat(owner?.ScreenOutgoingMailFor is { } named
                ? named
                    .Select(scanner => Enum.TryParse<SensitiveContentScannerKind>(scanner, ignoreCase: true, out var kind)
                        ? kind
                        : (SensitiveContentScannerKind?)null)
                    .Where(kind => kind is not null)
                    .Select(kind => kind!.Value)
                : [])
            .Distinct()
            .Order()
            .ToArray();

        return new EffectivePosture(switchedOn, screening);
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
            var roster = this.servedOwners.TryGetOwners();

            if (this.composed is { } current && ReferenceEquals(current.Roster, roster))
            {
                return current;
            }

            this.composed = this.Build(roster);

            return this.composed;
        }
    }

    /// <summary>Builds every posture one roster produces, sharing one redaction between owners who read the same one.</summary>
    private Composition Build(IReadOnlyList<ServedMailOwner>? roster)
    {
        var built = new Dictionary<EffectivePosture, SensitiveContentPosture>();
        var deploymentAnswer = Compose(this.deployment, null);
        var deploymentPosture = this.PostureOf(built, deploymentAnswer);

        if (roster is null)
        {
            return new Composition(
                null,
                deploymentPosture,
                new Dictionary<MailOwnerId, SensitiveContentPosture>(),
                []);
        }

        var answers = roster
            .Select(served => (served.Owner, Answer: Compose(this.deployment, served.SensitiveContent)))
            .ToArray();

        var byOwner = answers.ToDictionary(
            entry => entry.Owner,
            entry => this.PostureOf(built, entry.Answer));

        return new Composition(
            roster,
            deploymentPosture,
            byOwner,
            [.. byOwner
                .OrderBy(entry => entry.Key.Value)
                .Select(entry => new OwnerSensitiveContentPosture(entry.Key, entry.Value))]);
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

    /// <summary>One answer about one owner's mail, which two owners asking the same thing share a posture for.</summary>
    /// <param name="SwitchedOn">Which scanners run, deployment and owner composed.</param>
    /// <param name="Screening">Which of their findings stop an outgoing message, deployment and owner composed.</param>
    private readonly record struct EffectivePosture(
        SensitiveContentScannerKind[] SwitchedOn,
        SensitiveContentScannerKind[] Screening)
    {
        /// <inheritdoc />
        /// <remarks>
        /// Compared by what the two arrays hold rather than by their identity, which is the whole point of the key: two
        /// owners who asked for the same thing must meet the same posture rather than compose one each.
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
    /// <param name="Deployment">The posture of an owner this roster does not name, which is the deployment's own.</param>
    /// <param name="ByOwner">The posture of each owner the roster names.</param>
    /// <param name="Owners">The same postures as an ordered list, for the walk that judges every owner's rows at once.</param>
    private sealed record Composition(
        IReadOnlyList<ServedMailOwner>? Roster,
        SensitiveContentPosture Deployment,
        IReadOnlyDictionary<MailOwnerId, SensitiveContentPosture> ByOwner,
        IReadOnlyList<OwnerSensitiveContentPosture> Owners)
    {
        /// <summary>Gets whether anything at all is scanned for on this deployment.</summary>
        public bool IsActiveForAnyOwner =>
            this.Deployment.IsActive || this.Owners.Any(owner => owner.Posture.IsActive);
    }
}
