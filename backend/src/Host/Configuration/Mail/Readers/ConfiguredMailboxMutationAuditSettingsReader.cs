// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Domain.Accounts;

namespace MailFathom.Host.Configuration.Mail.Readers;

/// <summary>Reads whether a mailbox mutation is recorded, and for how long, from the bound section.</summary>
internal sealed class ConfiguredMailboxMutationAuditSettingsReader(MailSynchronizationOptions settings)
    : IMailboxMutationAuditSettingsReader
{
    /// <inheritdoc />
    /// <remarks>
    /// An account this snapshot no longer names reports <see cref="MailboxMutationAuditSettings.Disabled" /> rather
    /// than failing, unlike every other per-account reader here. The two callers are why: a mutation is only recorded
    /// for a configured account, and the retention pass runs over accounts a reload may have removed between one run and
    /// the next — where the honest answer is that no operator decision applies, not that the deployment is broken.
    /// </remarks>
    public MailboxMutationAuditSettings GetAuditSettings(MailAccountId accountId) =>
        settings.FindConfiguredAccount(accountId)?.CreateAuditSettings() ?? MailboxMutationAuditSettings.Disabled;
}
