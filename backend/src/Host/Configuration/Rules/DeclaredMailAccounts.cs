// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.Actions;
using MailFathom.Domain.Folders;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.OwnerSettings;

namespace MailFathom.Host.Configuration.Rules;

/// <summary>Reads the accounts a rule is judged against, from wherever the accounts are available.</summary>
/// <remarks>
/// <para>
/// A rule's scope, its destination folders, and the actions it declares are all claims about another section, so judging
/// them needs that section — and the two moments they are judged in reach it differently. Composition has configuration
/// and no container, so it reads the keys; a reload has a container and the published synchronization snapshot, so it
/// reads that. One type holds both, because what counts as a declared account has to be the same answer in both or a
/// rule set startup accepted would be refused on the first reload that changed nothing.
/// </para>
/// <para>
/// Identifiers are trimmed and blanks are dropped, which is what <see cref="Domain.Accounts.MailAccountId" />
/// does to the same text. A blank identifier is the synchronization section's own defect to report, and reporting it
/// again here would name the wrong section.
/// </para>
/// <para>
/// A folder alias that is not a value this system issues is dropped for the same reason. Every mapped folder is read,
/// including one the account does not mirror, because a mapping is the whole of what a destination needs: such a folder
/// is resolved when a change first files into it rather than by a run of its own.
/// </para>
/// <para>
/// Each folder is read with the role it plays beside its alias, because a rule may name its destination either way and
/// judging the two against different readings of the same section is how a rule set startup accepted would be refused
/// on a reload. A role this system does not support is read as no role at all: the synchronization section reports it
/// against the key that wrote it, and naming it again here would blame the rule for somebody else's typo.
/// </para>
/// </remarks>
internal static class DeclaredMailAccounts
{
    /// <summary>Reads the declared accounts straight from configuration, before any binding has happened.</summary>
    /// <param name="configuration">The configuration the host is composing itself from.</param>
    /// <returns>The accounts, in the order they are declared.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Both places a mailbox is declared are read: the deployment's own section, and each owner's <c>MailAccounts</c>
    /// under the top-level owner collection. A deployment declaring owners is refused a non-empty
    /// <c>MailSynchronization:Accounts</c>, so exactly one of the two is ever populated — and a rule judged against
    /// only the first would refuse every scope such a file names while naming the section that file may not fill.
    /// </remarks>
    public static IReadOnlyCollection<DeclaredMailAccount> ReadFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return
        [
            .. configuration
                .GetSection($"{MailSynchronizationOptions.SectionName}:{nameof(MailSynchronizationOptions.Accounts)}")
                .GetChildren()
                .Concat(configuration
                    .GetSection(DeclaredOwnerOptions.SectionName)
                    .GetChildren()
                    .SelectMany(owner => owner
                        .GetSection(nameof(DeclaredOwnerOptions.MailAccounts))
                        .GetChildren()))
                .Select(ReadAccount)
                .OfType<DeclaredMailAccount>(),
        ];
    }

    /// <summary>Reads the declared accounts from a bound synchronization configuration.</summary>
    /// <param name="settings">The synchronization configuration a reload published, or the one currently in force.</param>
    /// <returns>The accounts, in the order they are declared.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The owners' own declarations come off the roster the snapshot carries rather than out of a section, because
    /// that is where an owner's mailboxes are once the startup gate has resolved them — an owner read from their own
    /// document has no section at all. A reload runs behind that gate, so the roster is established by the time this
    /// is asked.
    /// </remarks>
    public static IReadOnlyCollection<DeclaredMailAccount> ReadFrom(MailSynchronizationOptions settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return ReadFrom(settings.Accounts
            .Concat(settings.ServedOwners?.SelectMany(static owner => owner.MailAccounts) ?? []));
    }

    /// <summary>Reads the declared accounts from one bound set of mailbox declarations.</summary>
    /// <param name="accounts">The declarations, which may be one owner's own rather than the whole deployment's.</param>
    /// <returns>The accounts, in the order they are declared.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="accounts" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The overload a claim about one owner's own mailboxes is judged through, which is every claim an owner's record
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

    /// <summary>Reads one account's keys, which is the shape available before anything has been bound.</summary>
    /// <remarks>
    /// No participation switch is read at all: none of the three decides anything about a destination, and reading one
    /// here would refuse a rule for filing into a folder that is perfectly reachable. An account declaring no folder is
    /// read as mapping the inbox, which is the mapping it is actually run with.
    /// </remarks>
    private static DeclaredMailAccount? ReadAccount(IConfigurationSection account)
    {
        var accountId = account[nameof(MailSynchronizationAccountOptions.AccountId)]?.Trim();

        if (string.IsNullOrEmpty(accountId))
        {
            return null;
        }

        var folders = account
            .GetSection(nameof(MailSynchronizationAccountOptions.Folders))
            .GetChildren()
            .ToArray();

        var mappedFolders = folders.Length == 0
            ? [TryReadFolder(nameof(MailFolderSpecialUse.Inbox), MailFolderSpecialUse.Inbox)]
            : folders
                .Select(folder => TryReadFolder(
                    folder[nameof(MailFolderMappingOptions.Alias)],
                    MailFolderMappingOptions.TryParseSpecialUse(folder[nameof(MailFolderMappingOptions.SpecialUse)], out var role)
                        ? role
                        : null))
                .ToArray();

        return new DeclaredMailAccount(
            accountId,
            [.. mappedFolders.OfType<DeclaredMailFolder>()],
            ReadPermissions(account.GetSection(nameof(MailSynchronizationAccountOptions.RuleActions))));
    }

    /// <summary>Reads one account's rule-action permissions, with every key it did not write taking its default.</summary>
    private static MailRuleActionPermissions ReadPermissions(IConfigurationSection ruleActions) =>
        new(
            !IsDeclaredFalse(ruleActions[nameof(MailRuleActionPermissionOptions.Move)]),
            !IsDeclaredFalse(ruleActions[nameof(MailRuleActionPermissionOptions.Copy)]),
            IsDeclaredTrue(ruleActions[nameof(MailRuleActionPermissionOptions.Delete)]),
            !IsDeclaredFalse(ruleActions[nameof(MailRuleActionPermissionOptions.MarkAsRead)]),
            !IsDeclaredFalse(ruleActions[nameof(MailRuleActionPermissionOptions.MarkAsFlagged)]),
            !IsDeclaredFalse(ruleActions[nameof(MailRuleActionPermissionOptions.WriteKeywords)]));

    /// <summary>Reads a switch an operator wrote, treating an unreadable value as unwritten.</summary>
    /// <remarks>
    /// The binder refuses a value that is neither, so a typo fails startup through the section's own validation rather
    /// than through this reading. Both directions are answered from the written text so that the default of a key is
    /// stated once, on the options type, and inherited here.
    /// </remarks>
    private static bool IsDeclaredFalse(string? value) => bool.TryParse(value, out var declared) && !declared;

    private static bool IsDeclaredTrue(string? value) => bool.TryParse(value, out var declared) && declared;

    private static DeclaredMailFolder? TryReadFolder(string? alias, MailFolderSpecialUse? role) =>
        MailRuleActionOptions.TryReadAlias(alias, out var readAlias)
            ? new DeclaredMailFolder(readAlias, role)
            : null;
}
