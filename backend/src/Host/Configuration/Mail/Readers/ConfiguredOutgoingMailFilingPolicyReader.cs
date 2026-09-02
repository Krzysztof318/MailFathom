// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Filing;
using MailFathom.Domain.Accounts;

namespace MailFathom.Host.Configuration.Mail.Readers;

/// <summary>Reads whether a sent message is filed back into the account's own mailbox from the bound section.</summary>
internal sealed class ConfiguredOutgoingMailFilingPolicyReader(MailSynchronizationOptions settings)
    : IOutgoingMailFilingPolicyReader
{
    /// <inheritdoc />
    /// <remarks>
    /// An account this deployment does not configure is answered as though it files the copy, which is the same
    /// direction the port's own default takes: nothing can send as an account nobody configured, so the answer is
    /// reached only by a caller asking about a message that cannot exist.
    /// </remarks>
    public bool FilesSentCopy(MailAccountId accountId) =>
        settings.FindConfiguredAccount(accountId)?.Delivery.FileSentCopy ?? true;
}
