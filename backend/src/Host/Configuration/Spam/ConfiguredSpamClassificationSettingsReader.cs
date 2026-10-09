// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Application.Spam;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Mail.Readers;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Configuration.Spam;

/// <summary>Reads each account's classification settings from its own record.</summary>
/// <remarks>
/// <para>
/// One source and no layer over it. Whether an account's mail is classified, which of its folders are scanned, and at
/// what threshold are the block that account's record carries and nothing else, so switching classification off in a
/// record switches it off — there is no deployment section behind it to revert to.
/// </para>
/// <para>
/// The wait comes from the deployment's section for every account, because it bounds how long the index may be held
/// back by a scanner that has stopped answering — a cost the process bears rather than a decision about a mailbox.
/// </para>
/// <para>
/// The default scope is resolved here rather than stated in a record because it is not a constant: it is whichever
/// alias that account maps to its inbox. An operator whose server presents the inbox under another name configures the
/// role, and the default has to follow the role rather than the literal text.
/// </para>
/// <para>
/// Both sources are read per request rather than captured, so a reload of the file and a commit of an account's record
/// each take effect on the next classification without a restart — and reading them changes nothing about what is
/// already recorded.
/// </para>
/// </remarks>
internal sealed class ConfiguredSpamClassificationSettingsReader(
    IOptionsMonitor<SpamClassificationOptions> deploymentOptions,
    MailSynchronizationOptions synchronizationOptions,
    IDeploymentMailFolders deploymentFolders)
    : ISpamClassificationSettingsReader
{
    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Read from the settings each account's document was read into when it was written, which are composed by the same
    /// <see cref="Compose" /> that <see cref="SettingsFor(MailAccountId)" /> answers with. That one reads the roster until
    /// #2330 moves it, so the walk that narrows a table and the arrival that asks about one message agree about which
    /// mail is classified only once the roster has republished the account's last write.
    /// </para>
    /// <para>
    /// The deployment's section supplies the wait every classification is bounded by and nothing about which mail is
    /// classified: that is each account's own record. It is read once for the whole scope rather than per account, so a
    /// reload landing part way through cannot bound one account's walk by the old value and the next by the new one.
    /// </para>
    /// </remarks>
    public async Task<SpamClassificationScope> ReadScopeInForceAsync(CancellationToken cancellationToken)
    {
        var deployment = deploymentOptions.CurrentValue;
        var ofClassifyingAccounts = await deploymentFolders.ReadAsync(
            MailFolderSelection.OfAccountsClassifyingSpam,
            cancellationToken);
        var classified = await deploymentFolders.ReadAsync(MailFolderSelection.ClassifiedForSpam, cancellationToken);

        return SpamClassificationScope.Create(
            ofClassifyingAccounts.Select(static folder => folder.AccountId),
            classified,
            deployment.ClassificationWait);
    }

    /// <inheritdoc />
    public SpamClassificationSettings SettingsFor(MailAccountId account) =>
        synchronizationOptions.FindConfiguredAccount(account) is { } declared
            ? Compose(declared)
            : SpamClassificationSettings.Disabled;

    /// <summary>Builds one account's settings out of the block its own declaration carries.</summary>
    /// <param name="account">The account's bound declaration.</param>
    /// <returns>The settings that account's mail is classified under.</returns>
    internal static SpamClassificationSettings Compose(MailSynchronizationAccountOptions account)
    {
        var record = account.SpamClassification ?? new MailAccountSpamClassificationOptions();

        return SpamClassificationSettings.Create(
            record.Enabled,
            record.UseScanner,
            ScannedAliasesOf(record.ScannedFolders, account),
            record.ScannerThreshold);
    }

    /// <summary>Reads the aliases a posture names, or the account's own inbox aliases where it names none.</summary>
    /// <remarks>
    /// An explicitly empty list is honoured as an empty scope, which is the distinction the nullable setting exists to
    /// preserve: whoever wrote no folders asked for the default, and whoever wrote none asked for none. Either way the
    /// aliases only ever reach this account's own mail, because every query the scope narrows is scoped to it — so an
    /// alias only another account carries selects nothing.
    /// </remarks>
    private static IEnumerable<MailFolderAlias> ScannedAliasesOf(
        string[]? scannedFolders,
        MailSynchronizationAccountOptions account) =>
        scannedFolders is { } configured
            ? configured
                .Where(static alias => !string.IsNullOrWhiteSpace(alias))
                .Select(MailFolderAlias.Create)
            : ConfiguredMailFolders.InboxAliasesOf([account]);
}
