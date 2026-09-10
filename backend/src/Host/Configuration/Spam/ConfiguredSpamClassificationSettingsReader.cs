// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam;
using MailFathom.Domain.Access;
using MailFathom.Domain.Folders;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Mail.Readers;
using MailFathom.Host.Configuration.UserSettings;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Configuration.Spam;

/// <summary>Reads each user's classification settings from whichever source their own record is read from.</summary>
/// <remarks>
/// <para>
/// Two sources and no layer between them. A user still served from a configuration source takes the deployment's
/// <c>SpamClassification</c> section, and a user whose document has been written takes the block that document
/// carries — which of the two applies is the per-user marker the roster holds, and nothing here unions them. That is
/// what makes switching classification off in a written record actually switch it off, rather than reverting to
/// whatever the file still says.
/// </para>
/// <para>
/// The wait comes from the deployment's section for every user, because it bounds how long the index may be held back
/// by a scanner that has stopped answering — a cost the process bears rather than a decision about somebody's mail.
/// </para>
/// <para>
/// The default scope is resolved here rather than in either section because it is not a constant: it is whichever alias
/// each of that user's own accounts maps to its inbox. An operator whose server presents the inbox under another name
/// configures the role, and the default has to follow the role rather than the literal text.
/// </para>
/// <para>
/// Both sources are read per request rather than captured, so a reload of the file and a commit of a user's record
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
    /// Composed from the same per-user reading <see cref="SettingsFor(MailUserId)" /> answers with, so the walk that
    /// narrows a table and the arrival that asks about one message cannot disagree about whose mail is classified. A
    /// deployment whose roster is not settled yet classifies nothing, which is the answer every path takes before the
    /// startup gate has run.
    /// </para>
    /// <para>
    /// The deployment's section supplies the wait every user's classification is bounded by and nothing about whose
    /// mail is classified: that is each user's own record. It is read once for the whole scope rather than per user,
    /// so a reload landing part way through cannot bound one user's walk by the old value and the next by the new one.
    /// </para>
    /// </remarks>
    public SpamClassificationScope ScopeInForce
    {
        get
        {
            if (synchronizationOptions.ServedUsers is not { } users)
            {
                return SpamClassificationScope.None;
            }

            var deployment = deploymentOptions.CurrentValue;

            var classifying = users
                .Select(served => new { Served = served, Settings = SettingsFor(served) })
                .Where(entry => entry.Settings.IsEnabled)
                .Select(entry => new { entry.Settings, Folders = FoldersOf(entry.Served).ToArray() })
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
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    public SpamClassificationSettings SettingsFor(MailUserId user)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException("A classification posture is read for a named user.", nameof(user));
        }

        return this.Served(user) is { } served
            ? SettingsFor(served)
            : SpamClassificationSettings.Disabled;
    }

    /// <summary>Reads one user's settings from the roster entry that already names them.</summary>
    /// <remarks>
    /// Takes the entry rather than the identifier because <see cref="ScopeInForce" /> holds it already, and searching
    /// the roster again per user would make composing the scope quadratic in a roster that may hold
    /// <see cref="ServedMailUsers.MaximumUsers" /> entries — a cost every stored message pays, because the
    /// derived-work gate reads the scope once per message a synchronization run stores.
    /// </remarks>
    private static SpamClassificationSettings SettingsFor(ServedMailUser served) =>
        Compose(served.SpamClassification ?? new UserSpamClassificationOptions(), served.MailAccounts);

    /// <summary>Builds one user's settings out of the block their own document carries.</summary>
    private static SpamClassificationSettings Compose(
        UserSpamClassificationOptions record,
        IReadOnlyList<MailSynchronizationAccountOptions> accounts) => SpamClassificationSettings.Create(
        record.Enabled,
        record.UseScanner,
        ScannedAliasesOf(record.ScannedFolders, accounts),
        record.ScannerThreshold);

    /// <summary>Reads the aliases a posture names, or the user's own accounts' inbox aliases where it names none.</summary>
    /// <remarks>
    /// An explicitly empty list is honoured as an empty scope, which is the distinction the nullable setting exists to
    /// preserve: whoever wrote no folders asked for the default, and whoever wrote none asked for none. Either way the
    /// aliases only ever reach this user's own mail, because every query the scope narrows is scoped to that user —
    /// so an alias only another user's account carries selects nothing.
    /// </remarks>
    private static IEnumerable<MailFolderAlias> ScannedAliasesOf(
        string[]? scannedFolders,
        IReadOnlyList<MailSynchronizationAccountOptions> accounts) =>
        scannedFolders is { } configured
            ? configured
                .Where(static alias => !string.IsNullOrWhiteSpace(alias))
                .Select(MailFolderAlias.Create)
            : ConfiguredMailFolders.InboxAliasesOf(accounts);

    /// <summary>Reads the folders of the accounts this user is served with.</summary>
    private static IEnumerable<ConfiguredFolder> FoldersOf(ServedMailUser served) =>
        ConfiguredMailFolders.Of(served.MailAccounts);

    /// <summary>Finds the user on the roster this snapshot was published with.</summary>
    private ServedMailUser? Served(MailUserId user) =>
        (synchronizationOptions.ServedUsers ?? [])
            .FirstOrDefault(candidate => candidate.User == user);
}
