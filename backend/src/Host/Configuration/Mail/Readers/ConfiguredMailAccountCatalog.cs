// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.UserSettings;

namespace MailFathom.Host.Configuration.Mail.Readers;

/// <summary>Publishes the accounts this deployment serves, each under the user the roster says it belongs to.</summary>
/// <remarks>
/// The user comes from the roster rather than from a declaration, because only one of the two places a mailbox is
/// declared can say whose it is. A user's own section names them, so their accounts arrive with the user already
/// attached; the deployment's own <c>MailSynchronization:Accounts</c> names nobody, so its accounts belong to the sole
/// user such a deployment holds — which is a fact the start establishes against the database rather than one any file
/// states.
/// </remarks>
internal sealed class ConfiguredMailAccountCatalog(
    MailSynchronizationOptions settings,
    ServedMailUsers servedUsers) : IDeploymentMailAccountCatalog
{
    /// <inheritdoc />
    public bool SynchronizationEnabled => settings.Enabled;

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Configuration is what defines the set of accounts, so this answers from the same declarations every other
    /// per-account reader does. It deliberately ignores <see cref="MailSynchronizationOptions.Enabled" />: that switch
    /// stops runs from fetching mail, and an operator who turned it off has not asked for the copy already stored to
    /// become unreadable. An account they removed is a different matter, and its absence here is what makes its stored
    /// mail unreadable.
    /// </para>
    /// <para>
    /// An account whose display name is missing or unusable is omitted rather than published under an invented one.
    /// Startup validation refuses that configuration, so the omission is only reachable while a reload is being
    /// rejected, and publishing an account under a name no operator chose is the one outcome worse than not publishing
    /// it at all.
    /// </para>
    /// <para>
    /// The order is the ordinal order of the identifiers, across every user rather than within each, because a scope
    /// resolved from this set is the deployment's own and a continuation cursor issued for it has to stay valid while
    /// the configuration does not change. Deduplication is by identifier for the same reason the lookup is: this
    /// release bounds mail-account names across the users it serves.
    /// </para>
    /// </remarks>
    public IReadOnlyList<ServedMailAccount> ServedAccounts =>
    [
        .. this.DeclaredAccounts()
            .Where(static candidate => !string.IsNullOrWhiteSpace(candidate.Account.AccountId))
            .Select(static candidate => candidate.Account.CreateServedAccount(candidate.User))
            .OfType<ServedMailAccount>()
            .DistinctBy(static account => account.Id.Value, StringComparer.Ordinal)
            .OrderBy(static account => account.Id.Value, StringComparer.Ordinal),
    ];

    /// <summary>Pairs every declared mail account with the user it belongs to.</summary>
    /// <remarks>
    /// The deployment's own section is read for the users it can belong to, which is at most one: a deployment that
    /// declares users has no sole user and its own section is refused as a place to declare a mailbox, so the two
    /// halves are never both non-empty.
    /// </remarks>
    private IEnumerable<(MailUserId User, MailSynchronizationAccountOptions Account)> DeclaredAccounts()
    {
        var users = servedUsers.Users;

        var deploymentAccounts = users
            .Where(user => user.Source == MailUserAccountSource.DeploymentSection)
            .SelectMany(user => (settings.Accounts ?? []).Select(account => (user.User, Account: account)));

        var ownedAccounts = users
            .SelectMany(user => user.MailAccounts.Select(account => (user.User, Account: account)));

        return [.. deploymentAccounts, .. ownedAccounts];
    }
}
