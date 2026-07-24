// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Infrastructure.Mail.MailKit;

/// <summary>Contains validated IMAP connection settings for one configured account.</summary>
public sealed record MailKitImapAccountSettings(string AccountId, string Host, int Port, bool UseTls, string UserName, string Password);

/// <summary>Resolves IMAP connection settings for configured accounts.</summary>
public interface IMailKitImapAccountSettingsProvider
{
    /// <summary>Gets connection settings for one local account identifier.</summary>
    MailKitImapAccountSettings GetSettings(string accountId);
}
