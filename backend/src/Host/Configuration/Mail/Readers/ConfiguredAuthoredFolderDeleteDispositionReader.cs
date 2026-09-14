// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Host.Configuration.Mail.Readers;

/// <summary>Reads whether deleting a folder through the client reaches the account's mail server, from the bound section.</summary>
internal sealed class ConfiguredAuthoredFolderDeleteDispositionReader(MailSynchronizationOptions settings)
    : IAuthoredFolderDeleteDispositionReader
{
    /// <inheritdoc />
    public AuthoredFolderDeleteDisposition GetAuthoredFolderDeleteDisposition(MailAccountId accountId) =>
        settings.RequireAccount(accountId).AuthoredFolderDeleteDisposition;
}
