// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.UserSettings;

namespace MailFathom.Host.Configuration.Mail.Readers;

/// <summary>Publishes the accounts this deployment serves, each under the user whose record declares it.</summary>
/// <remarks>
/// The user comes from the roster rather than from a declaration read off a file, because a mailbox is one user's
/// record and the roster is what the start establishes against the database.
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
    /// The users' records are what define the set of accounts, so this answers from the same declarations every other
    /// per-account reader does. It deliberately ignores <see cref="MailSynchronizationOptions.Enabled" />: that switch
    /// stops runs from fetching mail, and an operator who turned it off has not asked for the copy already stored to
    /// become unreadable. An account they removed is a different matter, and its absence here is what makes its stored
    /// mail unreadable.
    /// </para>
    /// <para>
    /// An account whose display name is missing or unusable is omitted rather than published under an invented one.
    /// Both the record write and the startup gate refuse such a declaration, so the omission is only reachable while a
    /// record is being rejected, and publishing an account under a name no operator chose is the one outcome worse
    /// than not publishing it at all.
    /// </para>
    /// <para>
    /// The order is the ordinal order of the identifiers, across every user rather than within each, because a scope
    /// resolved from this set is the deployment's own and a continuation cursor issued for it has to stay valid while
    /// the declarations do not change. Deduplication is by identifier for the same reason the lookup is keyed by one:
    /// this release resolves an account's settings by its identifier alone across the users a deployment serves.
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
    private IEnumerable<(MailUserId User, MailSynchronizationAccountOptions Account)> DeclaredAccounts() =>
        servedUsers.Users.SelectMany(user => user.MailAccounts.Select(account => (user.User, Account: account)));
}
