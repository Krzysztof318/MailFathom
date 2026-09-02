// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Synchronization.Reconciliation;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Host.Configuration.Mail.Readers;

/// <summary>Reads what becomes of a stored email the server no longer holds from the bound section.</summary>
internal sealed class ConfiguredRemotelyDeletedEmailDispositionReader(MailSynchronizationOptions settings)
    : IRemotelyDeletedEmailDispositionReader
{
    /// <inheritdoc />
    public RemotelyDeletedEmailDisposition GetDisposition(MailAccountId accountId) =>
        settings.RequireAccount(accountId).RemotelyDeletedEmailDisposition;
}
