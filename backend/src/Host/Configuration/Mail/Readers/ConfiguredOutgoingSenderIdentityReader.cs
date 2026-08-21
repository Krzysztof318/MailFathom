// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Host.Configuration.Mail.Readers;

/// <summary>Reads the mailbox an account sends as from the bound section.</summary>
internal sealed class ConfiguredOutgoingSenderIdentityReader(MailSynchronizationOptions settings)
    : IOutgoingSenderIdentityReader
{
    /// <inheritdoc />
    public OutgoingSenderIdentity? FindSenderIdentity(MailAccountId accountId)
    {
        var account = settings.FindConfiguredAccount(accountId);

        if (account?.Delivery is not { IsConfigured: true } delivery)
        {
            return null;
        }

        return EmailAddress.TryCreate(delivery.FromDisplayName, delivery.ResolveFromAddress(account.UserName), out var address)
            ? OutgoingSenderIdentity.Create(accountId, address)
            : null;
    }
}
