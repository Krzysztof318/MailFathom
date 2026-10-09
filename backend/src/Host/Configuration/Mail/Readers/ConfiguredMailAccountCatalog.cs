// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.Persistence.Users.AccountSettings;

namespace MailFathom.Host.Configuration.Mail.Readers;

/// <summary>Publishes the accounts this deployment serves, read from the account records.</summary>
/// <remarks>
/// <para>
/// Every answer is one statement over the records, so a reader holding an account asks about that account rather than
/// about the whole set, and a commit on any replica is the answer the next question gets on this one.
/// </para>
/// <para>
/// One account appears once in the deployment's set however many users are assigned it. That is the whole of one
/// mailbox being one mailbox: the identifier is generated and unique across the deployment, so a second assignment
/// adds a user rather than a second account, and every row of the mailbox's mail is the one copy both of them read.
/// </para>
/// <para>
/// An account whose display name is missing or unusable is omitted rather than published under an invented one. The
/// record write refuses such a name, so the omission is only reachable for a row changed behind MailFathom, and
/// publishing an account under a name no operator chose is the one outcome worse than not publishing it at all.
/// </para>
/// <para>
/// The synchronization switch is read off the published snapshot rather than the scope's, because it is the
/// deployment's own value and every snapshot carries the same one. Reading the scope's would pin it to the published
/// snapshot the moment the catalog is resolved, before an administrative route naming an account has prepared it.
/// </para>
/// </remarks>
internal sealed class ConfiguredMailAccountCatalog(
    ISettingsSnapshot<MailSynchronizationOptions> publishedSettings,
    IServedMailAccountReader servedAccountReader) : IDeploymentMailAccountCatalog
{
    /// <inheritdoc />
    public bool SynchronizationEnabled => publishedSettings.Current.Enabled;

    /// <inheritdoc />
    /// <remarks>
    /// It deliberately ignores <see cref="MailSynchronizationOptions.Enabled" />: that switch stops runs from fetching
    /// mail, and an operator who turned it off has not asked for the copy already stored to become unreadable. An
    /// account they removed is a different matter, and its absence here is what makes its stored mail unreadable.
    /// </remarks>
    public async Task<IReadOnlyList<ServedMailAccount>> ReadServedAccountsAsync(CancellationToken cancellationToken) =>
        ToServedAccounts(await servedAccountReader.ReadServedAsync(cancellationToken));

    /// <inheritdoc />
    /// <remarks>
    /// An identifier that is not one this deployment generates names no record, so it is left out of the answer rather
    /// than refused, which is the same answer as an account the deployment does not serve.
    /// </remarks>
    public async Task<IReadOnlyList<ServedMailAccount>> ReadServedAccountsAsync(
        IReadOnlyCollection<MailAccountId> among,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(among);

        Guid[] identifiers =
        [
            .. among
                .Select(static account => Guid.TryParse(account.Value, out var identifier) ? identifier : (Guid?)null)
                .OfType<Guid>(),
        ];

        return identifiers.Length == 0
            ? []
            : ToServedAccounts(await servedAccountReader.ReadServedAsync(identifiers, cancellationToken));
    }

    private static ServedMailAccount[] ToServedAccounts(IReadOnlyList<ServedMailAccountRow> served) =>
    [
        .. served
            .Select(static account => TryCreateServedAccount(account))
            .OfType<ServedMailAccount>()
            .OrderBy(static account => account.Id.Value, StringComparer.Ordinal),
    ];

    private static ServedMailAccount? TryCreateServedAccount(ServedMailAccountRow account)
    {
        try
        {
            return new ServedMailAccount(
                MailAccountId.Create(account.Id.ToString("D")),
                MailAccountDisplayName.Create(account.DisplayName),
                account.SynchronizationMode);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
