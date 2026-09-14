// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;

namespace MailFathom.TestSupport;

/// <summary>The postures a deployment serves, stated by a test rather than composed from configuration.</summary>
/// <remarks>
/// Every consumer that scans reads this port, so a suite arranging what one account's mail is scanned under states it
/// here instead of building a roster and a configuration section. The fallback is what an account this instance names
/// no posture for reads, which is the same answer the real composition gives: the deployment's own.
/// </remarks>
internal sealed class FixedSensitiveContentPostures : ISensitiveContentPostures
{
    private readonly IReadOnlyDictionary<MailAccountId, SensitiveContentPosture> byAccount;
    private readonly IReadOnlyDictionary<MailAccountId, MailUserId> assignees;
    private readonly SensitiveContentPosture fallback;

    private FixedSensitiveContentPostures(
        SensitiveContentPosture fallback,
        IReadOnlyDictionary<MailAccountId, SensitiveContentPosture> byAccount,
        IReadOnlyDictionary<MailAccountId, MailUserId> assignees)
    {
        this.fallback = fallback;
        this.byAccount = byAccount;
        this.assignees = assignees;
    }

    /// <summary>Gets the account <see cref="ForEveryAccount" /> names, which a test reading <see cref="Current" /> asserts on.</summary>
    public static MailAccountId SoleAccount { get; } = MailAccountId.Create("primary");

    /// <inheritdoc />
    public bool IsActiveForAnyAccount =>
        this.fallback.IsActive || this.byAccount.Values.Any(posture => posture.IsActive);

    /// <inheritdoc />
    public IReadOnlyList<MailAccountSensitiveContentPosture> Current =>
    [
        .. this.byAccount
            .OrderBy(entry => entry.Key.Value, StringComparer.Ordinal)
            .Select(entry => new MailAccountSensitiveContentPosture(entry.Key, entry.Value)),
    ];

    /// <summary>Builds the postures of a deployment where no account's mail is scanned for anything.</summary>
    /// <returns>Postures that hold no redaction, whichever account is asked about.</returns>
    public static FixedSensitiveContentPostures ScanningNothing() => new(
        SensitiveContentPosture.ScanningNothing,
        new Dictionary<MailAccountId, SensitiveContentPosture>(),
        new Dictionary<MailAccountId, MailUserId>());

    /// <summary>Builds the postures of a deployment that scans every account's mail the same way.</summary>
    /// <param name="posture">What every account's mail is scanned under.</param>
    /// <returns>Postures answering that one posture for every mailbox.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="posture" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The sole account is named as well as answered, so a consumer that walks every account meets one rather than
    /// none. That is the deployment this stands for: one mailbox, whose posture is the deployment's own.
    /// </remarks>
    public static FixedSensitiveContentPostures ForEveryAccount(SensitiveContentPosture posture)
    {
        ArgumentNullException.ThrowIfNull(posture);

        return new FixedSensitiveContentPostures(
            posture,
            new Dictionary<MailAccountId, SensitiveContentPosture> { [SoleAccount] = posture },
            new Dictionary<MailAccountId, MailUserId>
            {
                [SoleAccount] = SyntheticMailUser.Deployment,
            });
    }

    /// <summary>Builds the postures of a deployment whose accounts are scanned differently.</summary>
    /// <param name="fallback">What an account not named below reads, which is the deployment's own posture.</param>
    /// <param name="accounts">What each named account's mail is scanned under, and whose account it is.</param>
    /// <returns>Postures answering each named account its own and every other the fallback.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// The assignee travels with the posture because <see cref="AcrossAccountsOf" /> answers a read that spans a user's
    /// mailboxes, and a double that did not know who holds which account could not compose that answer at all.
    /// </remarks>
    public static FixedSensitiveContentPostures Of(
        SensitiveContentPosture fallback,
        params (MailAccountId Account, SensitiveContentPosture Posture, MailUserId User)[] accounts)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        ArgumentNullException.ThrowIfNull(accounts);

        return new FixedSensitiveContentPostures(
            fallback,
            accounts.ToDictionary(entry => entry.Account, entry => entry.Posture),
            accounts.ToDictionary(entry => entry.Account, entry => entry.User));
    }

    /// <summary>Builds the postures of a deployment whose accounts all belong to the user it serves.</summary>
    /// <param name="fallback">What an account not named below reads, which is the deployment's own posture.</param>
    /// <param name="accounts">What each named account's mail is scanned under.</param>
    /// <returns>Postures answering each named account its own and every other the fallback.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public static FixedSensitiveContentPostures Of(
        SensitiveContentPosture fallback,
        params (MailAccountId Account, SensitiveContentPosture Posture)[] accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        return Of(
            fallback,
            [.. accounts.Select(entry => (entry.Account, entry.Posture, SyntheticMailUser.Deployment))]);
    }

    /// <inheritdoc />
    public SensitiveContentPosture ForAccount(MailAccountId account) =>
        this.byAccount.TryGetValue(account, out var posture) ? posture : this.fallback;

    /// <inheritdoc />
    /// <remarks>
    /// The candidate that already covers every other, rather than a union composed here: this double is handed built
    /// postures instead of the ingredients of one, and a union of two would need a redactor running both their
    /// scanners and a screening policy resolved from both their category lists, neither of which can be composed out
    /// of the postures themselves. An arrangement no candidate covers is therefore refused rather than answered with
    /// the widest of them — the real composition unions the scanners and the screening kinds alike, so the widest
    /// would be a posture it would never produce, and a suite asserting redaction against one would be asserting
    /// against an answer no deployment gives. A test that needs such a posture states the composed one as the
    /// deployment's own.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when the user's accounts ask for things no one of their postures covers, which this double cannot compose.</exception>
    public SensitiveContentPosture AcrossAccountsOf(MailUserId user)
    {
        var candidates = this.assignees
            .Where(entry => entry.Value == user)
            .Select(entry => this.byAccount[entry.Key])
            .Append(this.fallback)
            .ToArray();

        return candidates.FirstOrDefault(candidate => candidates.All(other =>
                other.Scanners.All(candidate.Runs)
                && (candidate.ScreensAnything || !other.ScreensAnything)))
            ?? throw new InvalidOperationException(
                $"The accounts assigned to user {user.Value} ask for scanning no one of their postures covers, so the strictest of them is a union this double cannot build. State the composed posture as the deployment's own instead.");
    }

    /// <inheritdoc />
    public bool RunsForAnyAccount(SensitiveContentScannerKind scanner) =>
        this.fallback.Runs(scanner) || this.byAccount.Values.Any(posture => posture.Runs(scanner));
}
