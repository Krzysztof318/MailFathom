// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Domain.Access;

namespace MailFathom.TestSupport;

/// <summary>The postures a deployment serves, stated by a test rather than composed from configuration.</summary>
/// <remarks>
/// Every consumer that scans reads this port, so a suite arranging what one user's mail is scanned under states it
/// here instead of building a roster and a configuration section. The fallback is what a user this instance names no
/// posture for reads, which is the same answer the real composition gives: the deployment's own.
/// </remarks>
internal sealed class FixedSensitiveContentPostures : ISensitiveContentPostures
{
    private readonly IReadOnlyDictionary<MailUserId, SensitiveContentPosture> byUser;
    private readonly SensitiveContentPosture fallback;

    private FixedSensitiveContentPostures(
        SensitiveContentPosture fallback,
        IReadOnlyDictionary<MailUserId, SensitiveContentPosture> byUser)
    {
        this.fallback = fallback;
        this.byUser = byUser;
    }

    /// <inheritdoc />
    public bool IsActiveForAnyUser => this.fallback.IsActive || this.byUser.Values.Any(posture => posture.IsActive);

    /// <inheritdoc />
    public IReadOnlyList<UserSensitiveContentPosture> Current =>
    [
        .. this.byUser
            .OrderBy(entry => entry.Key.Value)
            .Select(entry => new UserSensitiveContentPosture(entry.Key, entry.Value)),
    ];

    /// <summary>Builds the postures of a deployment where nobody's mail is scanned for anything.</summary>
    /// <returns>Postures that hold no redaction, whichever user is asked about.</returns>
    public static FixedSensitiveContentPostures ScanningNothing() =>
        new(SensitiveContentPosture.ScanningNothing, new Dictionary<MailUserId, SensitiveContentPosture>());

    /// <summary>Builds the postures of a deployment that scans every user's mail the same way.</summary>
    /// <param name="posture">What every user's mail is scanned under.</param>
    /// <returns>Postures answering that one posture for everybody.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="posture" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The sole user is named as well as answered, so a consumer that walks every user meets one rather than none.
    /// That is the deployment this stands for: one person, whose posture is the deployment's own.
    /// </remarks>
    public static FixedSensitiveContentPostures ForEveryUser(SensitiveContentPosture posture)
    {
        ArgumentNullException.ThrowIfNull(posture);

        return new FixedSensitiveContentPostures(
            posture,
            new Dictionary<MailUserId, SensitiveContentPosture> { [SyntheticMailUser.Deployment] = posture });
    }

    /// <summary>Builds the postures of a deployment whose users are scanned differently.</summary>
    /// <param name="fallback">What a user not named below reads, which is the deployment's own posture.</param>
    /// <param name="users">What each named user's mail is scanned under.</param>
    /// <returns>Postures answering each named user their own and everybody else the fallback.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public static FixedSensitiveContentPostures Of(
        SensitiveContentPosture fallback,
        params (MailUserId User, SensitiveContentPosture Posture)[] users)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        ArgumentNullException.ThrowIfNull(users);

        return new FixedSensitiveContentPostures(
            fallback,
            users.ToDictionary(entry => entry.User, entry => entry.Posture));
    }

    /// <inheritdoc />
    public SensitiveContentPosture ForUser(MailUserId user) =>
        this.byUser.TryGetValue(user, out var posture) ? posture : this.fallback;

    /// <inheritdoc />
    public bool RunsForAnyUser(SensitiveContentScannerKind scanner) =>
        this.fallback.Runs(scanner) || this.byUser.Values.Any(posture => posture.Runs(scanner));
}
