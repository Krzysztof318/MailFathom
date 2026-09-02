// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Host.Configuration.Mail.Readers;

/// <summary>Reads which folders an operator mapped to the junk role from the bound section.</summary>
internal sealed class ConfiguredJunkMailFolderCatalog(MailSynchronizationOptions settings) : IJunkMailFolderCatalog
{
    /// <inheritdoc />
    /// <remarks>
    /// Read from the configured role rather than from what a server advertised, for the reason
    /// <see cref="MailFolderMappingOptions.ConfiguredSpecialUse" /> gives. A deployment that maps no junk folder answers
    /// with nothing here, and every mailbox read then behaves as it did before this existed.
    /// </remarks>
    public IReadOnlyList<MailFolderIdentity> JunkFolders =>
    [
        .. ConfiguredMailFolders.Of(settings)
            .Where(static folder => folder.SpecialUse is MailFolderSpecialUse.Junk)
            .Select(static folder => folder.Identity),
    ];

    /// <inheritdoc />
    public bool IsJunkFolder(MailAccountId accountId, MailFolderAlias folderAlias) =>
        ConfiguredMailFolders.Of(settings).Any(folder =>
            folder.SpecialUse is MailFolderSpecialUse.Junk
            && folder.Identity.AccountId == accountId
            && folder.Identity.Alias == folderAlias);
}
