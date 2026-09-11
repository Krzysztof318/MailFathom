// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Application.Accounts;
using MailFathom.Domain.Transport;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Mail.Readers;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.TestSupport;

namespace MailFathom.Host.UnitTests.Configuration.Mail;

/// <summary>Builds the configured mailbox a delivery rule is judged against, and runs the rules over it.</summary>
/// <remarks>
/// Every suite about what an account may be configured to do starts from the same complete account and the same
/// submission endpoint, and varies one property of them. Holding the shape here is what keeps each of those suites a
/// statement about the rule it covers rather than a restatement of what a valid account looks like.
/// </remarks>
internal static class ConfiguredMailAccounts
{
    /// <summary>Runs the rules a user's mail accounts are judged by and reports everything they found.</summary>
    /// <param name="options">The deployment whose served mailboxes are judged.</param>
    /// <returns>What the rules reported, empty when every declaration is accepted.</returns>
    /// <remarks>
    /// The account rules are <see cref="UserMailAccountRules" />' rather than the section's, because a mailbox is
    /// declared in its user's record and that is the one place the rules are stated.
    /// </remarks>
    internal static IReadOnlyList<ValidationResult> Validate(MailSynchronizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return UserMailAccountRules.FindRefusals(
            [.. options.DeclaredAccounts],
            nameof(UserAccountOptions.MailAccounts));
    }

    /// <summary>Builds a synchronizing deployment serving exactly one account, recorded against the user who owns it.</summary>
    /// <param name="account">The account it serves.</param>
    /// <returns>The configuration, carrying the roster the startup gate would have published.</returns>
    internal static MailSynchronizationOptions Holding(MailSynchronizationAccountOptions account) =>
        new MailSynchronizationOptions { Enabled = true }
            .WithServedUsers([new ServedMailUser(SyntheticMailUser.Deployment, "user", [account])]);

    /// <summary>Builds the catalog of accounts a deployment serves, as the composition root builds it.</summary>
    /// <param name="options">The snapshot the roster was published onto.</param>
    /// <returns>The catalog, answering with every recorded account under the user who owns it.</returns>
    internal static IDeploymentMailAccountCatalog CatalogOver(MailSynchronizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new ConfiguredMailAccountCatalog(
            options,
            ResolvedServedMailUsers.Serving([.. options.ServedUsers ?? []]));
    }

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
