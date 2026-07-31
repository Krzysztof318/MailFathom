// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailMcp.Host.Configuration;
using MailMcp.Infrastructure.Mail;
using MailMcp.Infrastructure.Secrets;

namespace MailMcp.Host.UnitTests;

/// <summary>Builds the bound account configuration a host test starts from.</summary>
internal static class ConfiguredAccounts
{
    /// <summary>Binds one account per supplied identifier, each carrying the given password reference.</summary>
    /// <remarks>The secret is named after the account it belongs to, which is what a real deployment does and what keeps the names unique across the bound section.</remarks>
    internal static MailSynchronizationOptions WithPasswordReferences(
        params (string AccountId, string SecretReference)[] accounts) => new()
        {
            Accounts = [.. accounts.Select(account => new MailSynchronizationAccountOptions
            {
                AccountId = account.AccountId,
                Host = "imap.example.test",
                UserName = "mailmcp@example.test",
                Secrets = new MailAccountSecretOptions
                {
                    Password = new ConfiguredSecret
                    {
                        Name = $"{account.AccountId}-password",
                        SecretReference = account.SecretReference,
                    },
                },
            })],
        };
}
