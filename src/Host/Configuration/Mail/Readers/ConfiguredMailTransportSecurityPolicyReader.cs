// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Transport;

namespace MailFathom.Host.Configuration.Mail.Readers;

/// <summary>Reads the transport security an account connects and submits under from the bound section.</summary>
internal sealed class ConfiguredMailTransportSecurityPolicyReader(MailSynchronizationOptions settings)
    : IMailTransportSecurityPolicyReader
{
    /// <inheritdoc />
    public MailTransportSecurityPolicy GetPolicy(MailAccountId accountId) =>
        settings.RequireAccount(accountId).CreateTransportSecurityPolicy();

    /// <inheritdoc />
    public MailTransportSecurityPolicy? GetDeliveryPolicy(MailAccountId accountId)
    {
        var account = settings.RequireAccount(accountId);

        return account.Delivery.IsConfigured ? account.CreateDeliveryTransportSecurityPolicy() : null;
    }
}
