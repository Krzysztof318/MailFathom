// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.OwnerSettings;

namespace MailFathom.Host.Configuration.Mail.Readers;

/// <summary>Publishes the accounts this deployment serves, each under the owner the roster says it belongs to.</summary>
/// <remarks>
/// The owner comes from the roster rather than from a declaration, because only one of the two places a mailbox is
/// declared can say whose it is. An owner's own section names them, so their accounts arrive with the owner already
/// attached; the deployment's own <c>MailSynchronization:Accounts</c> names nobody, so its accounts belong to the sole
/// owner such a deployment holds — which is a fact the start establishes against the database rather than one any file
/// states.
/// </remarks>
internal sealed class ConfiguredMailAccountCatalog(
    MailSynchronizationOptions settings,
    ServedMailOwners servedOwners) : IDeploymentMailAccountCatalog
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
    /// The order is the ordinal order of the identifiers, across every owner rather than within each, because a scope
    /// resolved from this set is the deployment's own and a continuation cursor issued for it has to stay valid while
    /// the configuration does not change. Deduplication is by identifier for the same reason the lookup is: this
    /// release bounds mail-account names across the owners it serves.
    /// </para>
    /// </remarks>
    public IReadOnlyList<ServedMailAccount> ServedAccounts =>
    [
        .. this.DeclaredAccounts()
            .Where(static candidate => !string.IsNullOrWhiteSpace(candidate.Account.AccountId))
            .Select(static candidate => candidate.Account.CreateServedAccount(candidate.Owner))
            .OfType<ServedMailAccount>()
            .DistinctBy(static account => account.Id.Value, StringComparer.Ordinal)
            .OrderBy(static account => account.Id.Value, StringComparer.Ordinal),
    ];

    /// <summary>Pairs every declared mail account with the owner it belongs to.</summary>
    /// <remarks>
    /// The deployment's own section is read for the owners it can belong to, which is at most one: a deployment that
    /// declares owners has no sole owner and its own section is refused as a place to declare a mailbox, so the two
    /// halves are never both non-empty.
    /// </remarks>
    private IEnumerable<(MailOwnerId Owner, MailSynchronizationAccountOptions Account)> DeclaredAccounts()
    {
        var owners = servedOwners.Owners;

        var deploymentAccounts = owners
            .Where(owner => owner.Source == MailOwnerAccountSource.DeploymentSection)
            .SelectMany(owner => (settings.Accounts ?? []).Select(account => (owner.Owner, Account: account)));

        var ownedAccounts = owners
            .SelectMany(owner => owner.MailAccounts.Select(account => (owner.Owner, Account: account)));

        return [.. deploymentAccounts, .. ownedAccounts];
    }
}
