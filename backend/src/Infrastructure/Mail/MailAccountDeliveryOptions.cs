// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Transport;

namespace MailFathom.Infrastructure.Mail;

/// <summary>Binds where one account's mail is submitted, separately from where it is read.</summary>
/// <remarks>
/// <para>
/// It is a block of its own beside <see cref="MailAccountTransportSecurityOptions" /> and
/// <see cref="MailAccountSecretOptions" /> because a mailbox provider almost never serves reading and submission on one
/// endpoint: the host and the port differ, and so does the connection security that reaches them, since implicit TLS on
/// the reading endpoint sits beside STARTTLS on the submission one across most providers. What does not differ is the
/// account: the permitted mechanisms, the accepted weakenings, and the certificate authority stay the account's single
/// decision, and the credential is the same one unless this block names another.
/// </para>
/// <para>
/// The block is optional. An account that omits it configures no submission endpoint, which is an ordinary shape rather
/// than a misconfiguration, and no delivery session can be opened for it. An account that names a host has its whole
/// block validated at startup, so an unsafe or incomplete endpoint is refused there rather than discovered at the
/// moment something tries to send.
/// </para>
/// </remarks>
public sealed class MailAccountDeliveryOptions
{
    /// <summary>Gets or sets whether this deployment may send as this account at all.</summary>
    /// <remarks>
    /// <para>
    /// Off unless an operator turns it on, and turned on one account at a time. That is what keeps an upgrade from
    /// making a deployment able to send: a release that gained the capability meets a configuration that never asked
    /// for it, and gaining it is therefore something an operator did rather than something that happened to them. It is
    /// per account rather than per deployment because an owner may want one identity able to write and another purely
    /// archival, which no single switch can express.
    /// </para>
    /// <para>
    /// It is separate from <see cref="Host" />, which says where mail would be submitted. An account may be configured
    /// to send and not permitted to, which is the ordinary shape of an endpoint an operator provisioned before deciding
    /// to use it; the reverse is refused at startup, because an account permitted to send with nowhere to submit is a
    /// permission that could never be acted on.
    /// </para>
    /// </remarks>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the submission server host name, whose presence is what configures the endpoint.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Gets or sets the submission server port.</summary>
    /// <remarks>
    /// The default is the implicit-TLS submission port, so it agrees with the default
    /// <see cref="ConnectionSecurity" />. A provider serving submission over STARTTLS is reached by setting both.
    /// </remarks>
    public int Port { get; set; } = 465;

    /// <summary>Gets or sets how the connection to the submission server is encrypted.</summary>
    /// <remarks>
    /// It is the same three choices with the same default the reading endpoint has, judged by the same rules: a mode
    /// that can leave the channel unencrypted needs the account's <c>AllowInsecureConnection</c>, and a clear-text
    /// mechanism over such a channel needs the second opt-in beside it.
    /// </remarks>
    public MailConnectionSecurity ConnectionSecurity { get; set; } = MailConnectionSecurity.TlsOnConnect;

    /// <summary>Gets or sets the submission user name, or nothing to authenticate as the account's reading user name.</summary>
    /// <remarks>
    /// Absent is the ordinary case, because a provider that authenticates one login for both protocols is the norm.
    /// It exists for the deployments where the two differ, which is usually a relay in front of the provider rather
    /// than the provider itself.
    /// </remarks>
    public string? UserName { get; set; }

    /// <summary>Gets or sets the address this account's mail is written from, or nothing to send from its own user name.</summary>
    /// <remarks>
    /// Absent is the ordinary case, because a provider authenticates the mailbox by its address and the two are then
    /// the same value. It exists for the account whose login is a bare name rather than an address, and for the mailbox
    /// that sends under an address it is not reached at — a shared or aliased sender, which is a configuration decision
    /// rather than anything a request may make.
    /// </remarks>
    public string? FromAddress { get; set; }

    /// <summary>Gets or sets the name written beside the sending address, or nothing to write the address alone.</summary>
    /// <remarks>
    /// It is deliberately not the account's <c>DisplayName</c>, which is the alias an operator invented to name this
    /// mailbox in their own tooling. Sending mail signed <c>work</c> because that is what the account is called locally
    /// would put an internal name in front of every recipient, so writing the address alone is the honest default and
    /// the name a mailbox signs itself with is stated on purpose.
    /// </remarks>
    public string? FromDisplayName { get; set; }

    /// <summary>Gets or sets whether a copy of each delivered message is put into this account's sent folder.</summary>
    /// <remarks>
    /// <para>
    /// It defaults to on, because a submission server does not file anything: SMTP carries the message to its
    /// recipients and says nothing about the sender's own mailbox, so a deployment that appends nothing leaves the
    /// owner with mail they sent and no record of it in the client they read.
    /// </para>
    /// <para>
    /// It is turned off for the account whose provider files the copy itself, which several webmail providers do for
    /// mail submitted through their own servers. That is configured rather than detected: a provider files the copy
    /// asynchronously, so looking in the folder after a delivery cannot tell a copy that is about to appear from one
    /// that never will, and guessing wrong leaves either a duplicate or a gap.
    /// </para>
    /// </remarks>
    public bool FileSentCopy { get; set; } = true;

    /// <summary>Gets or sets the submission credential, or nothing to present the account's reading credential.</summary>
    /// <remarks>
    /// The block is nullable and defaults to absent rather than to an empty block, for the reason
    /// <see cref="MailAccountSecretOptions.Password" /> gives: secret discovery walks the bound options graph by type,
    /// so an empty block left here by default would be discovered for every account and fail startup with an
    /// unresolvable reference no operator wrote.
    /// </remarks>
    public MailAccountSecretOptions? Secrets { get; set; }

    /// <summary>Gets whether this account configures a submission endpoint at all.</summary>
    /// <remarks>The host is what decides it, because an endpoint without one names no server, while every other setting here has a usable default or an inherited value.</remarks>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(this.Host);

    /// <summary>Gets the user name a submission connection authenticates with.</summary>
    /// <param name="accountUserName">The user name the account reads its mailbox with.</param>
    /// <returns>The submission user name when this block names one; otherwise the account's own.</returns>
    public string ResolveUserName(string accountUserName) =>
        string.IsNullOrWhiteSpace(this.UserName) ? accountUserName : this.UserName.Trim();

    /// <summary>Gets the address a composed message is written from.</summary>
    /// <param name="accountUserName">The user name the account reads its mailbox with, which is a mailbox address on most providers.</param>
    /// <returns>The configured sending address, the account's user name when it is one, or nothing when neither names a mailbox.</returns>
    /// <remarks>
    /// The user name is the fallback for the reason the account's own domains are read from it: it is the only mailbox
    /// identity an account states, and a login without an at-sign states none. Inventing an address from the host would
    /// send mail from a mailbox nobody configured.
    /// </remarks>
    public string? ResolveFromAddress(string accountUserName)
    {
        if (!string.IsNullOrWhiteSpace(this.FromAddress))
        {
            return this.FromAddress.Trim();
        }

        return !string.IsNullOrWhiteSpace(accountUserName)
            && accountUserName.Contains('@', StringComparison.Ordinal)
                ? accountUserName.Trim()
                : null;
    }

    /// <summary>Gets the secret block a submission connection resolves its password from.</summary>
    /// <param name="accountSecrets">The account's own secret block, which a rejected configuration may have left absent.</param>
    /// <returns>This block's secrets when it names a password reference; otherwise the account's, or an empty block when the account has none.</returns>
    /// <remarks>
    /// A block present but naming no password reference reads as absent, so <c>"Secrets": {}</c> falls back to the
    /// account's credential instead of resolving nothing and failing the connection with a missing-material error.
    /// </remarks>
    public MailAccountSecretOptions ResolveSecrets(MailAccountSecretOptions? accountSecrets) =>
        string.IsNullOrWhiteSpace(this.Secrets?.Password?.SecretReference)
            ? accountSecrets ?? new MailAccountSecretOptions()
            : this.Secrets;
}
