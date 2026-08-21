// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Retrieval.AskMail.Audit;
using MailFathom.Domain.Accounts;

namespace MailFathom.Host.Configuration.Mail.Readers;

/// <summary>Reads whether an answered question is recorded, and for how long, from the bound section.</summary>
internal sealed class ConfiguredMailAnsweringAuditSettingsReader(MailSynchronizationOptions settings)
    : IMailAnsweringAuditSettingsReader
{
    /// <inheritdoc />
    public MailAnsweringAuditSettings GetAnsweringAuditSettings(MailAccountId accountId) =>
        settings.FindConfiguredAccount(accountId)?.CreateAnsweringAuditSettings() ?? MailAnsweringAuditSettings.Disabled;
}
