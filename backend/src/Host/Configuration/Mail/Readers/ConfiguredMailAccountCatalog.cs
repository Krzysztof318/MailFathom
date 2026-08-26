// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;

namespace MailFathom.Host.Configuration.Mail.Readers;

/// <summary>Publishes the accounts this deployment serves, read from the bound section that defines them.</summary>
/// <remarks>
/// The owner is supplied rather than configured, because an account block names none: while accounts are declared in a
/// file the deployment holds exactly one owner and every declared account is theirs. It reaches every account this
/// catalog publishes, so a write that resolved an account here has already resolved whose it is.
/// </remarks>
internal sealed class ConfiguredMailAccountCatalog(
    MailSynchronizationOptions settings,
    IDeploymentMailOwnerSource deploymentOwner) : IDeploymentMailAccountCatalog
{
    /// <inheritdoc />
    public bool SynchronizationEnabled => settings.Enabled;

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Configuration is what defines the set of accounts, so this answers from the same bound options every other
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
    /// </remarks>
    public IReadOnlyList<ServedMailAccount> ServedAccounts =>
    [
        .. (settings.Accounts ?? [])
            .Where(static candidate => !string.IsNullOrWhiteSpace(candidate.AccountId))
            .Select(candidate => candidate.CreateServedAccount(deploymentOwner.Owner))
            .OfType<ServedMailAccount>()
            .DistinctBy(static account => account.Id.Value, StringComparer.Ordinal)
            .OrderBy(static account => account.Id.Value, StringComparer.Ordinal),
    ];
}
