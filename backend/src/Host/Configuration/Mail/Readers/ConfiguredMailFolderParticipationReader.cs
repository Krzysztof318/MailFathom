// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Host.Configuration.Mail.Readers;

/// <summary>Reads what each mapped folder takes part in from the bound section.</summary>
internal sealed class ConfiguredMailFolderParticipationReader(MailSynchronizationOptions settings)
    : IMailFolderParticipationReader
{
    /// <inheritdoc />
    public IReadOnlyList<MailFolderIdentity> FoldersMapped =>
        [.. ConfiguredMailFolders.Of(settings).Select(static folder => folder.Identity)];

    /// <inheritdoc />
    public IReadOnlyList<MailFolderIdentity> FoldersSynchronized =>
        [.. ConfiguredMailFolders.Of(settings).Where(static folder => folder.Participation.IsSynchronized).Select(static folder => folder.Identity)];

    /// <inheritdoc />
    public IReadOnlyList<MailFolderIdentity> FoldersVisibleToTools =>
        [.. ConfiguredMailFolders.Of(settings).Where(static folder => folder.Participation.IsVisibleToTools).Select(static folder => folder.Identity)];

    /// <inheritdoc />
    public IReadOnlyList<MailFolderIdentity> FoldersGeneratingEmbeddings =>
        [.. ConfiguredMailFolders.Of(settings).Where(static folder => folder.Participation.GeneratesEmbeddings).Select(static folder => folder.Identity)];

    /// <inheritdoc />
    public MailFolderParticipation GetParticipation(MailAccountId accountId, MailFolderAlias folderAlias) =>
        ConfiguredMailFolders.Of(settings)
            .FirstOrDefault(folder => folder.Identity.AccountId == accountId && folder.Identity.Alias == folderAlias)
            ?.Participation
        ?? MailFolderParticipation.Unmapped;
}
