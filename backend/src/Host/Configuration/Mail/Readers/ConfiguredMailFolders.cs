// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Host.Configuration.Mail.Readers;

/// <summary>Reads the configured folder entries the folder ports answer from.</summary>
/// <remarks>
/// Shared by the participation reader, the junk catalog, and the classification scope default rather than written once
/// per reader, because all three ask the same question of the same section: which folders an operator mapped, under
/// which account, and what each one takes part in.
/// </remarks>
internal static class ConfiguredMailFolders
{
    /// <summary>Reads every account's folders as the pair of identity and participation the ports answer with.</summary>
    /// <param name="settings">The snapshot the folders are read from.</param>
    /// <returns>One entry per usable configured folder.</returns>
    /// <remarks>
    /// It walks <see cref="MailSynchronizationAccountOptions.EffectiveFolders" /> rather than the configured list, so an
    /// account that configures no folder answers for the inbox mapping it is actually run with. An entry whose alias or
    /// account identifier is unusable is skipped: startup validation refuses that configuration, and inventing an
    /// identity for it here would attach one folder's decision to a name no operator wrote.
    /// </remarks>
    internal static IEnumerable<ConfiguredFolder> Of(MailSynchronizationOptions settings) =>
        Of(settings.Accounts ?? []);

    /// <summary>Reads one set of account declarations as the pair of identity and participation the ports answer with.</summary>
    /// <param name="accounts">The declarations, which may be one owner's own rather than the whole deployment's.</param>
    /// <returns>One entry per usable configured folder.</returns>
    /// <remarks>
    /// The overload a decision about one owner's own mailboxes is read through. An owner's folders are theirs, so a
    /// question asked about them has to be asked of their accounts and of no others — and asking it the same way the
    /// deployment's own section is read is what keeps one answer to *which folder plays which part*.
    /// </remarks>
    internal static IEnumerable<ConfiguredFolder> Of(IEnumerable<MailSynchronizationAccountOptions> accounts) =>
        accounts
            .SelectMany(account => account.EffectiveFolders.Select(folder => new { account.AccountId, Folder = folder }))
            .Select(static configured => ConfiguredFolder.TryRead(configured.AccountId, configured.Folder))
            .OfType<ConfiguredFolder>();

    /// <summary>Reads the aliases one set of accounts maps to its inbox, which is the scope classification defaults to.</summary>
    /// <param name="accounts">The declarations, which may be one owner's own rather than the whole deployment's.</param>
    /// <returns>One alias per account that maps a folder to the inbox role.</returns>
    /// <remarks>
    /// Read beside the folder mappings because the default has to follow them: whoever's server presents the inbox
    /// under another name configures the role, and the default scope has to be the alias that role resolved to rather
    /// than the literal text INBOX.
    /// </remarks>
    internal static IEnumerable<MailFolderAlias> InboxAliasesOf(
        IEnumerable<MailSynchronizationAccountOptions> accounts) =>
        Of(accounts)
            .Where(static folder => folder.SpecialUse is MailFolderSpecialUse.Inbox)
            .Select(static folder => folder.Identity.Alias);
}

/// <summary>One configured folder read as the identity, the participation, and the role the folder ports answer with.</summary>
/// <param name="Identity">The account and alias the folder is known by.</param>
/// <param name="Participation">What the folder takes part in.</param>
/// <param name="SpecialUse">The role the operator configured for it, or <see langword="null" /> when they configured none.</param>
internal sealed record ConfiguredFolder(
    MailFolderIdentity Identity,
    MailFolderParticipation Participation,
    MailFolderSpecialUse? SpecialUse)
{
    /// <summary>Reads one account's folder entry, or nothing when its names are not values this system issues.</summary>
    /// <param name="configuredAccountId">The account identifier the entry was configured under.</param>
    /// <param name="folder">The configured folder entry.</param>
    /// <returns>The folder read, or <see langword="null" /> when its names are unusable.</returns>
    internal static ConfiguredFolder? TryRead(
        string configuredAccountId,
        MailFolderMappingOptions folder)
    {
        if (string.IsNullOrWhiteSpace(configuredAccountId) || string.IsNullOrWhiteSpace(folder.Alias))
        {
            return null;
        }

        return new ConfiguredFolder(
            new MailFolderIdentity(
                MailAccountId.Create(configuredAccountId),
                MailFolderAlias.Create(folder.Alias)),
            folder.Participation,
            folder.ConfiguredSpecialUse);
    }
}
