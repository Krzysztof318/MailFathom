// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Folders;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Infrastructure.Persistence.Users;

namespace MailFathom.Host.Configuration.Rules;

/// <summary>Reads the accounts a rule is judged against, out of the bound declarations that hold them.</summary>
/// <remarks>
/// <para>
/// A rule's scope, its destination folders, and the actions it declares are all claims about somebody's mailboxes, so
/// judging them needs those mailboxes — and every one of them is a user's own record rather than a configuration key.
/// So both readings here start from bound declarations, which is what keeps a rule set a configuration write accepted one
/// the first reload that changed nothing still accepts.
/// </para>
/// <para>
/// Identifiers are trimmed and blanks are dropped, which is what <see cref="Domain.Accounts.MailAccountId" />
/// does to the same text. A blank identifier is the record's own defect to report, and reporting it again here would
/// name the wrong document.
/// </para>
/// <para>
/// A folder alias that is not a value this system issues is dropped for the same reason. Every mapped folder is read,
/// including one the account does not mirror, because a mapping is the whole of what a destination needs: such a folder
/// is resolved when a change first files into it rather than by a run of its own. Each folder is read with the role it
/// plays beside its alias, because a rule may name its destination either way.
/// </para>
/// </remarks>
internal static class DeclaredMailAccounts
{
    /// <summary>Reads the declared accounts from the account records this deployment serves.</summary>
    /// <param name="records">The served records, as <see cref="Infrastructure.Persistence.Users.AccountSettings.IServedMailAccountReader.ReadServedRecordsAsync" /> reads them.</param>
    /// <returns>The accounts whose document binds, in the order the records are given.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="records" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// What a reload of the rule set and a write of the configuration judge a rule's mailboxes against, because a user's
    /// mailboxes are their own record and no configuration source states one. A record whose document does not bind
    /// declares nothing, so a rule naming it is refused exactly as one naming a mailbox no record holds.
    /// </remarks>
    public static IReadOnlyCollection<DeclaredMailAccount> ReadFrom(IEnumerable<MailAccountRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        return ReadFrom(records
            .Select(MailAccountQueryableSettingsReading.TryBind)
            .OfType<MailSynchronizationAccountOptions>());
    }

    /// <summary>Reads the declared accounts from one bound set of mailbox declarations.</summary>
    /// <param name="accounts">The declarations, which may be one user's own rather than the whole deployment's.</param>
    /// <returns>The accounts, in the order they are declared.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="accounts" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The overload a claim about one user's own mailboxes is judged through, which is every claim a user's record
    /// makes about a folder: their scanned folders and their junk destination resolve within their own accounts and
    /// nowhere else. Reading them the same way the deployment's are read is what keeps one answer to *is this a mapped
    /// folder* rather than two.
    /// </remarks>
    public static IReadOnlyCollection<DeclaredMailAccount> ReadFrom(
        IEnumerable<MailSynchronizationAccountOptions> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        return
        [
            .. accounts
                .Where(account => !string.IsNullOrWhiteSpace(account.AccountId))
                .Select(account => new DeclaredMailAccount(
                    account.AccountId.Trim(),
                    ReadMappedFolders(account),
                    (account.RuleActions ?? new MailRuleActionPermissionOptions()).ToPermissions())),
        ];
    }

    /// <summary>Reads one account's mapped folders from the bound folders it is actually run with.</summary>
    private static IReadOnlyCollection<DeclaredMailFolder> ReadMappedFolders(MailSynchronizationAccountOptions account) =>
    [
        .. account.EffectiveFolders
            .Select(folder => TryReadFolder(folder.Alias, folder.DeclaredRole))
            .OfType<DeclaredMailFolder>(),
    ];

    private static DeclaredMailFolder? TryReadFolder(string? alias, MailFolderSpecialUse? role) =>
        MailRuleActionOptions.TryReadAlias(alias, out var readAlias)
            ? new DeclaredMailFolder(readAlias, role)
            : null;
}
