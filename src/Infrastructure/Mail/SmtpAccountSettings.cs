// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Mail;

/// <summary>Contains validated SMTP endpoint and credential settings for one configured account.</summary>
/// <param name="AccountId">The normalized local account identifier.</param>
/// <param name="Host">The submission server host name.</param>
/// <param name="Port">The submission server port.</param>
/// <param name="UserName">The submission user name, which stays a plain configuration value because it is an identifier rather than a credential.</param>
/// <param name="Material">
/// The password and trust anchor this account's delivery resolved. This record is a carrier and does not own them:
/// the operation that requested the settings owns the material and must dispose it when it ends.
/// </param>
/// <remarks>
/// It is a separate record from <see cref="ImapAccountSettings" /> rather than a shared one, because a mailbox provider
/// almost never serves both protocols on one endpoint and the two are resolved for different operations. Connection
/// security and permitted authentication mechanisms are deliberately absent here for the same reason they are absent
/// there: they travel with the account's transport security policy, which the caller hands to the session factory. The
/// synthesized <see cref="object.ToString" /> is safe only because <see cref="MailAccountConnectionMaterial" /> redacts
/// its password.
/// </remarks>
public sealed record SmtpAccountSettings(
    string AccountId,
    string Host,
    int Port,
    string UserName,
    MailAccountConnectionMaterial Material);

/// <summary>Resolves SMTP connection settings for configured accounts.</summary>
/// <remarks>
/// The port carries behavior of its own rather than exposing bound configuration: it resolves the account's secret
/// references at the moment a connection is about to be made and hands the material to the caller, so no settings
/// object holds a live password between operations. Nothing in the mail client library publishes such a contract, and
/// every member here would survive replacing that library unchanged.
/// </remarks>
public interface ISmtpAccountSettingsProvider
{
    /// <summary>Gets submission settings for one local account identifier, resolving its password and trust anchor.</summary>
    /// <param name="accountId">The local account identifier.</param>
    /// <param name="cancellationToken">Cancels the secret resolution.</param>
    /// <returns>The settings, whose <see cref="SmtpAccountSettings.Material" /> the caller owns and must dispose.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the account is not configured, or configures no submission endpoint.</exception>
    /// <remarks>The contract is asynchronous because secret resolution may reach a managed store; every scheme shipped today reads a local file or the environment block.</remarks>
    Task<SmtpAccountSettings> GetSettingsAsync(string accountId, CancellationToken cancellationToken);
}
