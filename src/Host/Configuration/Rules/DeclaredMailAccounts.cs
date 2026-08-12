// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.Actions;
using MailFathom.Domain.Folders;
using MailFathom.Host.Configuration.Mail;

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
/// A folder alias that is not a value this system issues is dropped for the same reason, and so is an unmirrored
/// folder — the second deliberately, because a rule may only file into a folder whose mail this account mirrors.
/// </para>
/// </remarks>
internal static class DeclaredMailAccounts
{
    /// <summary>The configuration section the accounts are declared in.</summary>
    private const string SynchronizationSectionName = "MailSynchronization";

    /// <summary>Reads the declared accounts straight from configuration, before any binding has happened.</summary>
    /// <param name="configuration">The configuration the host is composing itself from.</param>
    /// <returns>The accounts, in the order they are declared.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> is <see langword="null" />.</exception>
    public static IReadOnlyCollection<DeclaredMailAccount> ReadFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return
        [
            .. configuration
                .GetSection($"{SynchronizationSectionName}:{nameof(MailSynchronizationOptions.Accounts)}")
                .GetChildren()
                .Select(ReadAccount)
                .OfType<DeclaredMailAccount>(),
        ];
    }

    /// <summary>Reads the declared accounts from a bound synchronization configuration.</summary>
    /// <param name="settings">The synchronization configuration a reload published, or the one currently in force.</param>
    /// <returns>The accounts, in the order they are declared.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings" /> is <see langword="null" />.</exception>
    public static IReadOnlyCollection<DeclaredMailAccount> ReadFrom(MailSynchronizationOptions settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return
        [
            .. settings.Accounts
                .Where(account => !string.IsNullOrWhiteSpace(account.AccountId))
                .Select(account => new DeclaredMailAccount(
                    account.AccountId.Trim(),
                    ReadMirroredAliases(account),
                    (account.RuleActions ?? new MailRuleActionPermissionOptions()).ToPermissions())),
        ];
    }

    /// <summary>Reads one account's mirrored folder aliases from the bound folders it is actually run with.</summary>
    private static IReadOnlyCollection<MailFolderAlias> ReadMirroredAliases(MailSynchronizationAccountOptions account) =>
    [
        .. account.EffectiveFolders
            .Where(folder => folder.Participation.IsSynchronized)
            .Select(folder => TryReadAlias(folder.Alias))
            .OfType<MailFolderAlias>(),
    ];

    /// <summary>Reads one account's keys, which is the shape available before anything has been bound.</summary>
    /// <remarks>
    /// The folder participation is read as <c>Synchronize</c> alone rather than through
    /// <see cref="MailFolderParticipation" />, because that type derives the other two answers from this one and the
    /// other two decide nothing about a destination. An account declaring no folder is read as mirroring the inbox,
    /// which is the mapping it is actually run with.
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

        var mirroredAliases = folders.Length == 0
            ? [TryReadAlias(nameof(MailFolderSpecialUse.Inbox))]
            : folders
                .Where(folder => !IsDeclaredFalse(folder[nameof(MailFolderMappingOptions.Synchronize)]))
                .Select(folder => TryReadAlias(folder[nameof(MailFolderMappingOptions.Alias)]))
                .ToArray();

        return new DeclaredMailAccount(
            accountId,
            [.. mirroredAliases.OfType<MailFolderAlias>()],
            ReadPermissions(account.GetSection(nameof(MailSynchronizationAccountOptions.RuleActions))));
    }

    /// <summary>Reads one account's rule-action permissions, with every key it did not write taking its default.</summary>
    private static MailRuleActionPermissions ReadPermissions(IConfigurationSection ruleActions) =>
        new(
            !IsDeclaredFalse(ruleActions[nameof(MailRuleActionPermissionOptions.Move)]),
            !IsDeclaredFalse(ruleActions[nameof(MailRuleActionPermissionOptions.Copy)]),
            IsDeclaredTrue(ruleActions[nameof(MailRuleActionPermissionOptions.Delete)]),
            !IsDeclaredFalse(ruleActions[nameof(MailRuleActionPermissionOptions.MarkAsRead)]));

    /// <summary>Reads a switch an operator wrote, treating an unreadable value as unwritten.</summary>
    /// <remarks>
    /// The binder refuses a value that is neither, so a typo fails startup through the section's own validation rather
    /// than through this reading. Both directions are answered from the written text so that the default of a key is
    /// stated once, on the options type, and inherited here.
    /// </remarks>
    private static bool IsDeclaredFalse(string? value) => bool.TryParse(value, out var declared) && !declared;

    private static bool IsDeclaredTrue(string? value) => bool.TryParse(value, out var declared) && declared;

    private static MailFolderAlias? TryReadAlias(string? alias) =>
        MailRuleActionOptions.TryReadAlias(alias, out var readAlias) ? readAlias : null;
}
