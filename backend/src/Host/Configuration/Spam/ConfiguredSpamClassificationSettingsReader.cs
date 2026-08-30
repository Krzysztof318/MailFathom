// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam;
using MailFathom.Domain.Access;
using MailFathom.Domain.Folders;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Mail.Readers;
using MailFathom.Host.Configuration.OwnerSettings;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Configuration.Spam;

/// <summary>Reads each owner's classification settings from whichever source their own record is read from.</summary>
/// <remarks>
/// <para>
/// Two sources and no layer between them. An owner still served from a configuration source takes the deployment's
/// <c>SpamClassification</c> section, and an owner whose document has been written takes the block that document
/// carries — which of the two applies is the per-owner marker the roster holds, and nothing here unions them. That is
/// what makes switching classification off in a written record actually switch it off, rather than reverting to
/// whatever the file still says.
/// </para>
/// <para>
/// The wait comes from the deployment's section for every owner, because it bounds how long the index may be held back
/// by a scanner that has stopped answering — a cost the process bears rather than a decision about somebody's mail.
/// </para>
/// <para>
/// The default scope is resolved here rather than in either section because it is not a constant: it is whichever alias
/// each of that owner's own accounts maps to its inbox. An operator whose server presents the inbox under another name
/// configures the role, and the default has to follow the role rather than the literal text.
/// </para>
/// <para>
/// Both sources are read per request rather than captured, so a reload of the file and a commit of an owner's record
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
    /// Composed from the same per-owner reading <see cref="SettingsFor" /> answers with, so the walk that narrows a
    /// table and the arrival that asks about one message cannot disagree about whose mail is classified. A deployment
    /// whose roster is not settled yet classifies nothing, which is the answer every path takes before the startup gate
    /// has run.
    /// </remarks>
    public SpamClassificationScope ScopeInForce
    {
        get
        {
            if (synchronizationOptions.ServedOwners is not { } owners)
            {
                return SpamClassificationScope.None;
            }

            var classifying = owners
                .Select(owner => new { Owner = owner, Settings = this.SettingsFor(owner.Owner) })
                .Where(entry => entry.Settings.IsEnabled)
                .Select(entry => new { entry.Settings, Folders = this.FoldersOf(entry.Owner).ToArray() })
                .ToArray();

            return SpamClassificationScope.Create(
                classifying.SelectMany(entry => entry.Folders
                    .Select(static folder => folder.Identity.AccountId)),
                classifying.SelectMany(entry => entry.Folders
                    .Where(folder => entry.Settings.Covers(folder.Identity.Alias))
                    .Select(static folder => folder.Identity)),
                deploymentOptions.CurrentValue.ClassificationWait);
        }
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody.</exception>
    public SpamClassificationSettings SettingsFor(MailOwnerId owner)
    {
        if (!owner.IsSpecified)
        {
            throw new ArgumentException("A classification posture is read for a named owner.", nameof(owner));
        }

        if (this.Served(owner) is not { } served)
        {
            return SpamClassificationSettings.Disabled;
        }

        var wait = deploymentOptions.CurrentValue.ClassificationWait;
        var accounts = this.AccountDeclarationsOf(served);

        return served.ReadFromConfiguration
            ? Compose(deploymentOptions.CurrentValue, accounts, wait)
            : Compose(served.SpamClassification ?? new OwnerSpamClassificationOptions(), accounts, wait);
    }

    /// <summary>Builds one owner's settings out of the deployment's section, which is what still reaches them.</summary>
    private static SpamClassificationSettings Compose(
        SpamClassificationOptions deployment,
        IReadOnlyList<MailSynchronizationAccountOptions> accounts,
        TimeSpan wait) => SpamClassificationSettings.Create(
        deployment.Enabled,
        deployment.UseScanner,
        ScannedAliasesOf(deployment.ScannedFolders, accounts),
        deployment.ScannerThreshold,
        wait);

    /// <summary>Builds one owner's settings out of the block their own document carries.</summary>
    private static SpamClassificationSettings Compose(
        OwnerSpamClassificationOptions record,
        IReadOnlyList<MailSynchronizationAccountOptions> accounts,
        TimeSpan wait) => SpamClassificationSettings.Create(
        record.Enabled,
        record.UseScanner,
        ScannedAliasesOf(record.ScannedFolders, accounts),
        record.ScannerThreshold,
        wait);

    /// <summary>Reads the aliases a posture names, or the owner's own accounts' inbox aliases where it names none.</summary>
    /// <remarks>
    /// An explicitly empty list is honoured as an empty scope, which is the distinction the nullable setting exists to
    /// preserve: whoever wrote no folders asked for the default, and whoever wrote none asked for none. Either way the
    /// aliases only ever reach this owner's own mail, because every query the scope narrows is scoped to that owner —
    /// so an alias only another owner's account carries selects nothing.
    /// </remarks>
    private static IEnumerable<MailFolderAlias> ScannedAliasesOf(
        string[]? scannedFolders,
        IReadOnlyList<MailSynchronizationAccountOptions> accounts) =>
        scannedFolders is { } configured
            ? configured
                .Where(static alias => !string.IsNullOrWhiteSpace(alias))
                .Select(MailFolderAlias.Create)
            : ConfiguredMailFolders.InboxAliasesOf(accounts);

    /// <summary>Reads the folders of the accounts this owner is served with.</summary>
    private IEnumerable<ConfiguredFolder> FoldersOf(ServedMailOwner served) =>
        ConfiguredMailFolders.Of(this.AccountDeclarationsOf(served));

    /// <summary>Finds the owner on the roster this snapshot was published with.</summary>
    private ServedMailOwner? Served(MailOwnerId owner) =>
        (synchronizationOptions.ServedOwners ?? [])
            .FirstOrDefault(candidate => candidate.Owner == owner);

    /// <summary>Reads the mailbox declarations this owner is served with, which is not always their own record's.</summary>
    /// <remarks>
    /// The deployment's own section names no owner and therefore belongs to whichever sole owner such a deployment
    /// holds, so an owner served from it takes its accounts and the roster holds none for them. Every other owner's
    /// mailboxes are on the roster, which is where their own declared section and their own document both arrive.
    /// </remarks>
    private IReadOnlyList<MailSynchronizationAccountOptions> AccountDeclarationsOf(ServedMailOwner served) =>
        served.Source is MailOwnerAccountSource.DeploymentSection
            ? synchronizationOptions.Accounts ?? []
            : served.MailAccounts;
}
