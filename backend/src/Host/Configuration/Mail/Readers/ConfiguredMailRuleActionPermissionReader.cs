// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.Actions;
using MailFathom.Domain.Accounts;

namespace MailFathom.Host.Configuration.Mail.Readers;

/// <summary>Reads which rule actions an operator admitted on an account from the bound section.</summary>
internal sealed class ConfiguredMailRuleActionPermissionReader(MailSynchronizationOptions settings)
    : IMailRuleActionPermissionReader
{
    /// <inheritdoc />
    public MailRuleActionPermissions GetRuleActionPermissions(MailAccountId accountId) =>
        (settings.RequireAccount(accountId).RuleActions ?? new MailRuleActionPermissionOptions()).ToPermissions();
}
