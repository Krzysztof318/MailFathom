// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Domain.Transport;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Secrets.Discovery;

namespace MailFathom.Host.UnitTests.Configuration.Mail;

/// <summary>Builds the configured mailbox a delivery rule is judged against, and runs the rules over it.</summary>
/// <remarks>
/// Every suite about what an account may be configured to do starts from the same complete account and the same
/// submission endpoint, and varies one property of them. Holding the shape here is what keeps each of those suites a
/// statement about the rule it covers rather than a restatement of what a valid account looks like.
/// </remarks>
internal static class ConfiguredMailAccounts
{
    /// <summary>Runs the options' own validation rules and reports everything they found.</summary>
    /// <param name="options">The configuration to judge.</param>
    /// <returns>What the rules reported, empty when the configuration is accepted.</returns>
    internal static IReadOnlyList<ValidationResult> Validate(MailSynchronizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return [.. options.Validate(new ValidationContext(options))];
    }

    /// <summary>Builds a synchronizing deployment serving exactly one account.</summary>
    /// <param name="account">The account it serves.</param>
    /// <returns>The configuration.</returns>
    internal static MailSynchronizationOptions Holding(MailSynchronizationAccountOptions account) => new()
    {
        Enabled = true,
        Accounts = [account],
    };

    /// <summary>Builds a complete reading account, which is what a delivery rule is added to and judged over.</summary>
    /// <returns>The account.</returns>
    internal static MailSynchronizationAccountOptions Primary() => new()
    {
        AccountId = "primary",
        DisplayName = "The primary mailbox",
        Host = "imap.example.test",
        UserName = "mailfathom@example.test",
        Secrets = new MailAccountSecretOptions
        {
            Password = new ConfiguredSecret { SecretReference = "systemd-credential:imap-primary-password" },
        },
    };

    /// <summary>Builds a submission endpoint every rule accepts, which a test then varies one property of.</summary>
    /// <returns>The endpoint.</returns>
    internal static MailAccountDeliveryOptions Delivery() => new()
    {
        Host = "smtp.example.test",
        Port = 587,
        ConnectionSecurity = MailConnectionSecurity.StartTlsRequired,
    };
}
