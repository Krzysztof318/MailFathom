// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Synchronization;

namespace MailFathom.Host.Configuration.Mail.Readers;

/// <summary>Reads the stretch of time an account is synchronized over from the bound section.</summary>
internal sealed class ConfiguredMailSynchronizationWindowReader(MailSynchronizationOptions settings)
    : IMailSynchronizationWindowReader
{
    /// <inheritdoc />
    public MailSynchronizationWindow GetWindow(MailAccountId accountId) =>
        settings.RequireAccount(accountId).CreateSynchronizationWindow();
}
