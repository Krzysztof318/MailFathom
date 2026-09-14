// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

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
    MailSynchronizationOptions synchronizationOptions)
    : ISpamClassificationSettingsReader
{
    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Composed from the same per-account reading <see cref="SettingsFor(MailAccountId)" /> answers with, so the walk
    /// that narrows a table and the arrival that asks about one message cannot disagree about which mail is classified.
    /// A deployment whose roster is not settled yet classifies nothing, which is the answer every path takes before the
    /// startup gate has run.
    /// </para>
    /// <para>
    /// The deployment's section supplies the wait every classification is bounded by and nothing about which mail is
    /// classified: that is each account's own record. It is read once for the whole scope rather than per account, so a
    /// reload landing part way through cannot bound one account's walk by the old value and the next by the new one.
    /// </para>
    /// </remarks>
    public SpamClassificationScope ScopeInForce
    {
        get
        {
            if (synchronizationOptions.ServedUsers is null)
            {
                return SpamClassificationScope.None;
            }

            var deployment = deploymentOptions.CurrentValue;

            // Filtered before the folders are composed, because this property is read once per stored message and a
            // folder graph built for an account that classifies nothing is allocated and then discarded.
            var classifying = this.DeclaredAccounts()
                .Select(account => new { Account = account, Settings = Compose(account) })
                .Where(entry => entry.Settings.IsEnabled)
                .Select(entry => new { entry.Settings, Folders = ConfiguredMailFolders.Of([entry.Account]).ToArray() })
                .ToArray();

            return SpamClassificationScope.Create(
                classifying.SelectMany(entry => entry.Folders
                    .Select(static folder => folder.Identity.AccountId)),
                classifying.SelectMany(entry => entry.Folders
                    .Where(folder => entry.Settings.Covers(folder.Identity.Alias))
                    .Select(static folder => folder.Identity)),
                deployment.ClassificationWait);
        }
    }

    /// <inheritdoc />
    public SpamClassificationSettings SettingsFor(MailAccountId account) =>
        synchronizationOptions.FindConfiguredAccount(account) is { } declared
            ? Compose(declared)
            : SpamClassificationSettings.Disabled;

    /// <summary>Builds one account's settings out of the block its own declaration carries.</summary>
    private static SpamClassificationSettings Compose(MailSynchronizationAccountOptions account)
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

    /// <summary>Reads every account this deployment serves, one entry per mailbox however many users hold it.</summary>
    /// <remarks>
    /// A mailbox assigned to two people is one record with one posture, so classifying it twice would file one message
    /// under two verdicts and narrow the walk by the same folders twice.
    /// </remarks>
    private IEnumerable<MailSynchronizationAccountOptions> DeclaredAccounts() =>
        synchronizationOptions.DeclaredAccounts
            .Where(static account => MailSynchronizationOptions.TryReadAccountId(account.AccountId) is not null)
            .DistinctBy(static account => MailSynchronizationOptions.TryReadAccountId(account.AccountId), StringComparer.Ordinal);
}
