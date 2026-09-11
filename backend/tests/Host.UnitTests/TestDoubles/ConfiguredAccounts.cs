// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.TestSupport;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Builds the recorded mail accounts a host test starts from.</summary>
internal static class ConfiguredAccounts
{
    /// <summary>Builds one user's record holding one account per supplied identifier, each carrying the given password reference.</summary>
    /// <remarks>The secret is named after the account it belongs to, which is what a real deployment does and what keeps the names unique within the record that declares them.</remarks>
    internal static UserAccountOptions WithPasswordReferences(
        params (string AccountId, string SecretReference)[] accounts) => new()
        {
            MailAccounts = [.. accounts.Select(account => new MailSynchronizationAccountOptions
            {
                AccountId = account.AccountId,
                DisplayName = $"The {account.AccountId} mailbox",
                Host = "imap.example.test",
                UserName = "mailfathom@example.test",
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

    /// <summary>Reads one declared mailbox back off the roster, for a test that states it and then adjusts it.</summary>
    /// <remarks>The roster carries the declarations a test handed it rather than copies of them, so an adjustment made through this reaches the reader under test.</remarks>
    internal static MailSynchronizationAccountOptions DeclaredAccount(
        this MailSynchronizationOptions settings,
        int position) =>
        settings.DeclaredAccounts.ElementAt(position);

    /// <summary>Publishes these bound settings over a deployment serving one user whose record holds the given mailboxes.</summary>
    /// <remarks>The roster is what carries a mailbox onto a snapshot, so a test naming accounts states them the way the composition root ends up with them.</remarks>
    internal static MailSynchronizationOptions Serving(
        this MailSynchronizationOptions settings,
        params MailSynchronizationAccountOptions[] mailAccounts) =>
        settings.WithServedUsers([new ServedMailUser(SyntheticMailUser.Deployment, "user", mailAccounts)]);

    /// <summary>Builds the published mail snapshot of a deployment serving one user who records those accounts.</summary>
    /// <remarks>The roster is what carries a mailbox onto a snapshot, so a test about what a deployment serves states it the way the composition root ends up with it.</remarks>
    internal static MailSynchronizationOptions ServingPasswordReferences(
        params (string AccountId, string SecretReference)[] accounts) =>
        new MailSynchronizationOptions { Enabled = true }.Serving(
            [.. WithPasswordReferences(accounts).MailAccounts]);
}
