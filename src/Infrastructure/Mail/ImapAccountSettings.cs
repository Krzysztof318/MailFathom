// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Infrastructure.Mail;

/// <summary>Contains validated IMAP endpoint and credential settings for one configured account.</summary>
/// <remarks>
/// Connection security and permitted authentication mechanisms are deliberately absent: they travel with the account's
/// transport security policy, which the caller hands to the session factory. Every member is plain IMAP endpoint
/// vocabulary, so replacing the IMAP client library would leave this type unchanged and its name must not name one.
/// </remarks>
public sealed record ImapAccountSettings(string AccountId, string Host, int Port, string UserName, string Password);

/// <summary>Resolves IMAP connection settings for configured accounts.</summary>
public interface IImapAccountSettingsProvider
{
    /// <summary>Gets connection settings for one local account identifier.</summary>
    ImapAccountSettings GetSettings(string accountId);
}
