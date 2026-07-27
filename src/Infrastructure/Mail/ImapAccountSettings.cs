// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Infrastructure.Mail;

/// <summary>Contains validated IMAP endpoint and credential settings for one configured account.</summary>
/// <param name="AccountId">The normalized local account identifier.</param>
/// <param name="Host">The IMAP server host name.</param>
/// <param name="Port">The IMAP server port.</param>
/// <param name="UserName">The IMAP user name, which stays a plain configuration value because it is an identifier rather than a credential.</param>
/// <param name="Secrets">
/// The account's resolved secret material. This record is a carrier and does not own it: the operation that requested
/// the settings owns the secrets and must dispose them when it ends.
/// </param>
/// <remarks>
/// Connection security and permitted authentication mechanisms are deliberately absent: they travel with the account's
/// transport security policy, which the caller hands to the session factory. Every member is plain IMAP endpoint
/// vocabulary, so replacing the IMAP client library would leave this type unchanged and its name must not name one. The
/// synthesized <see cref="object.ToString" /> is safe only because <see cref="MailAccountSecrets" /> redacts its
/// material.
/// </remarks>
public sealed record ImapAccountSettings(
    string AccountId,
    string Host,
    int Port,
    string UserName,
    MailAccountSecrets Secrets);

/// <summary>Resolves IMAP connection settings for configured accounts.</summary>
public interface IImapAccountSettingsProvider
{
    /// <summary>Gets connection settings for one local account identifier, resolving its secrets.</summary>
    /// <param name="accountId">The local account identifier.</param>
    /// <param name="cancellationToken">Cancels the secret resolution.</param>
    /// <returns>The settings, whose <see cref="ImapAccountSettings.Secrets" /> the caller owns and must dispose.</returns>
    /// <remarks>The contract is asynchronous because secret resolution may reach a managed store; every scheme shipped today reads a local file or the environment block.</remarks>
    Task<ImapAccountSettings> GetSettingsAsync(string accountId, CancellationToken cancellationToken);
}
