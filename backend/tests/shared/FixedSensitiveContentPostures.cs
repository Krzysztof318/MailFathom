// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Domain.Access;

namespace MailFathom.TestSupport;

/// <summary>The postures a deployment serves, stated by a test rather than composed from configuration.</summary>
/// <remarks>
/// Every consumer that scans reads this port, so a suite arranging what one owner's mail is scanned under states it
/// here instead of building a roster and a configuration section. The fallback is what an owner this instance names no
/// posture for reads, which is the same answer the real composition gives: the deployment's own.
/// </remarks>
internal sealed class FixedSensitiveContentPostures : ISensitiveContentPostures
{
    private readonly IReadOnlyDictionary<MailOwnerId, SensitiveContentPosture> byOwner;
    private readonly SensitiveContentPosture fallback;

    private FixedSensitiveContentPostures(
        SensitiveContentPosture fallback,
        IReadOnlyDictionary<MailOwnerId, SensitiveContentPosture> byOwner)
    {
        this.fallback = fallback;
        this.byOwner = byOwner;
    }

    /// <inheritdoc />
    public bool IsActiveForAnyOwner => this.fallback.IsActive || this.byOwner.Values.Any(posture => posture.IsActive);

    /// <inheritdoc />
    public IReadOnlyList<OwnerSensitiveContentPosture> Current =>
    [
        .. this.byOwner
            .OrderBy(entry => entry.Key.Value)
            .Select(entry => new OwnerSensitiveContentPosture(entry.Key, entry.Value)),
    ];

    /// <summary>Builds the postures of a deployment where nobody's mail is scanned for anything.</summary>
    /// <returns>Postures that hold no redaction, whichever owner is asked about.</returns>
    public static FixedSensitiveContentPostures ScanningNothing() =>
        new(SensitiveContentPosture.ScanningNothing, new Dictionary<MailOwnerId, SensitiveContentPosture>());

    /// <summary>Builds the postures of a deployment that scans every owner's mail the same way.</summary>
    /// <param name="posture">What every owner's mail is scanned under.</param>
    /// <returns>Postures answering that one posture for everybody.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="posture" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The sole owner is named as well as answered, so a consumer that walks every owner meets one rather than none.
    /// That is the deployment this stands for: one person, whose posture is the deployment's own.
    /// </remarks>
    public static FixedSensitiveContentPostures ForEveryOwner(SensitiveContentPosture posture)
    {
        ArgumentNullException.ThrowIfNull(posture);

        return new FixedSensitiveContentPostures(
            posture,
            new Dictionary<MailOwnerId, SensitiveContentPosture> { [SyntheticMailOwner.Deployment] = posture });
    }

    /// <summary>Builds the postures of a deployment whose owners are scanned differently.</summary>
    /// <param name="fallback">What an owner not named below reads, which is the deployment's own posture.</param>
    /// <param name="owners">What each named owner's mail is scanned under.</param>
    /// <returns>Postures answering each named owner their own and everybody else the fallback.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public static FixedSensitiveContentPostures Of(
        SensitiveContentPosture fallback,
        params (MailOwnerId Owner, SensitiveContentPosture Posture)[] owners)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        ArgumentNullException.ThrowIfNull(owners);

        return new FixedSensitiveContentPostures(
            fallback,
            owners.ToDictionary(entry => entry.Owner, entry => entry.Posture));
    }

    /// <inheritdoc />
    public SensitiveContentPosture ForOwner(MailOwnerId owner) =>
        this.byOwner.TryGetValue(owner, out var posture) ? posture : this.fallback;

    /// <inheritdoc />
    public bool RunsForAnyOwner(SensitiveContentScannerKind scanner) =>
        this.fallback.Runs(scanner) || this.byOwner.Values.Any(posture => posture.Runs(scanner));
}
