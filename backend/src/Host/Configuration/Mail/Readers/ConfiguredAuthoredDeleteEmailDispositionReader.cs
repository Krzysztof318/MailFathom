// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Host.Configuration.Mail.Readers;

/// <summary>Reads what a delete this deployment itself authors does to the message from the bound section.</summary>
internal sealed class ConfiguredAuthoredDeleteEmailDispositionReader(MailSynchronizationOptions settings)
    : IAuthoredDeleteEmailDispositionReader
{
    /// <inheritdoc />
    public AuthoredDeleteEmailDisposition GetAuthoredDeleteDisposition(MailAccountId accountId) =>
        settings.RequireAccount(accountId).AuthoredDeleteEmailDisposition;
}
