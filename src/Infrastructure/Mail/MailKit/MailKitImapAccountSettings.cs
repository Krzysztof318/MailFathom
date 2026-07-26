// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Infrastructure.Mail.MailKit;

/// <summary>Contains validated IMAP endpoint and credential settings for one configured account.</summary>
/// <remarks>
/// Connection security and permitted authentication mechanisms are deliberately absent: they travel with the account's
/// transport security policy, which the caller hands to the session factory.
/// </remarks>
public sealed record MailKitImapAccountSettings(string AccountId, string Host, int Port, string UserName, string Password);

/// <summary>Resolves IMAP connection settings for configured accounts.</summary>
public interface IMailKitImapAccountSettingsProvider
{
    /// <summary>Gets connection settings for one local account identifier.</summary>
    MailKitImapAccountSettings GetSettings(string accountId);
}
