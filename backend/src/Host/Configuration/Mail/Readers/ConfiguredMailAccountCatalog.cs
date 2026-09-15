// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Host.Configuration.UserSettings;

namespace MailFathom.Host.Configuration.Mail.Readers;

/// <summary>Publishes the accounts this deployment serves, and which of them each user is assigned.</summary>
/// <remarks>
/// <para>
/// Both answers come from the roster, because the roster is what the start establishes against the database: a user's
/// composed record carries the accounts assigned to them, so the deployment's set is the union of those and the
/// assignment relation is which record an account appeared in. Reading them from one place is what stops the two from
/// disagreeing about an account an administrator has just assigned or unassigned.
/// </para>
/// <para>
/// One account appears once in the deployment's set however many users are assigned it. That is the whole of one
/// mailbox being one mailbox: the identifier is generated and unique across the deployment, so a second assignment
/// adds a user rather than a second account, and every row of the mailbox's mail is the one copy both of them read.
/// </para>
/// </remarks>
internal sealed class ConfiguredMailAccountCatalog(
    MailSynchronizationOptions settings,
    ServedMailUsers servedUsers) : IDeploymentMailAccountCatalog, IMailAccountAssignments
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
    /// The order is the ordinal order of the identifiers, because a scope resolved from this set is the deployment's
    /// own and a continuation cursor issued for it has to stay valid while the declarations do not change.
    /// Deduplication is by identifier because an account assigned to several users is composed into each of their
    /// records and is one account in every other respect.
    /// </para>
    /// </remarks>
    public IReadOnlyList<ServedMailAccount> ServedAccounts =>
    [
        .. this.DeclaredAccounts()
            .Where(static account => !string.IsNullOrWhiteSpace(account.AccountId))
            .Select(static account => account.CreateServedAccount())
            .OfType<ServedMailAccount>()
            .DistinctBy(static account => account.Id.Value, StringComparer.Ordinal)
            .OrderBy(static account => account.Id.Value, StringComparer.Ordinal),
    ];

    /// <inheritdoc />
    /// <remarks>
    /// A user this roster does not hold is assigned nothing, which is the same answer as a user holding a record that
    /// declares no account. Both are served nothing rather than served everything, and neither is told apart here:
    /// what a caller acting for a user the roster never established reads is decided by the resolution, which turns an
    /// empty answer into a scope admitting no folder.
    /// </remarks>
    public IReadOnlyList<MailAccountId> AccountsAssignedTo(MailUserId user) =>
    [
        .. servedUsers.Users
            .Where(served => served.User == user)
            .SelectMany(static served => served.MailAccounts)
            .Select(static account => MailSynchronizationOptions.TryReadAccountId(account.AccountId))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Select(MailAccountId.Create),
    ];

    /// <inheritdoc />
    /// <remarks>
    /// The order is the ordinal order of the users' identifiers, so a fan-out over a shared mailbox reaches them in
    /// one order whichever record the roster happened to compose first.
    /// </remarks>
    public IReadOnlyList<MailUserId> UsersAssignedTo(MailAccountId account) =>
    [
        .. servedUsers.Users
            .Where(served => served.MailAccounts.Any(declared =>
                MailSynchronizationOptions.TryReadAccountId(declared.AccountId) is { } identifier
                && StringComparer.Ordinal.Equals(identifier, account.Value)))
            .Select(static served => served.User)
            .Distinct()
            .OrderBy(static user => user.Value),
    ];

    /// <summary>Every mail account declared by any user's record, an account assigned to several appearing once per assignment.</summary>
    private IEnumerable<MailSynchronizationAccountOptions> DeclaredAccounts() =>
        servedUsers.Users.SelectMany(static user => user.MailAccounts);
}
