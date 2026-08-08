// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using MailFathom.SyntheticMail.Configuration;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace MailFathom.SyntheticMail.Delivery;

/// <summary>The real submission session, over a connection the credential is safe on.</summary>
/// <remarks>
/// <para>
/// The security option is chosen from the account and is never <c>Auto</c> or the opportunistic
/// <c>StartTlsWhenAvailable</c>. Both of those fall back to an unencrypted session when the server does not offer the
/// extension, which is precisely the downgrade this tool must refuse: the very next command it sends is the password.
/// <see cref="SecureSocketOptions.StartTls" /> fails the connection instead, and <see cref="SmtpTransportSecurity" />
/// offers no third value to reach for.
/// </para>
/// <para>
/// The envelope is stated explicitly on every submission rather than derived from the headers, and that is a privacy
/// decision rather than a protocol one. A generated message names invented participants in <c>From</c> and <c>Cc</c>,
/// and letting MailKit read the envelope out of those headers would make the server attempt delivery to each of them —
/// turning a reserved-domain address that was supposed to reach nobody into a stream of bounces.
/// </para>
/// </remarks>
internal sealed class SmtpSyntheticMailTransport : ISyntheticMailTransport
{
    private readonly SendingAccount account;
    private readonly ISmtpClient client;

    /// <summary>Initializes a session against one account, over a real SMTP client.</summary>
    /// <param name="account">The throwaway account to submit as.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="account" /> is <see langword="null" />.</exception>
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Ownership of the client passes to the instance being constructed, which disposes it.")]
    internal SmtpSyntheticMailTransport(SendingAccount account)
        : this(account, new SmtpClient())
    {
    }

    /// <summary>Initializes a session over a client the caller supplies.</summary>
    /// <param name="account">The throwaway account to submit as.</param>
    /// <param name="client">The client to submit through, which this instance owns and disposes.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// MailKit publishes <see cref="ISmtpClient" />, so the seam is the library's own interface rather than a port
    /// restating it. It exists because what this class decides is the part that must never regress quietly: which
    /// <see cref="SecureSocketOptions" /> the connection is opened with, that authentication happens only after that
    /// connection, and that a submission states its envelope instead of letting the headers supply one. None of the
    /// three can be observed from outside without either a real server or this substitution.
    /// </remarks>
    internal SmtpSyntheticMailTransport(SendingAccount account, ISmtpClient client)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(client);

        this.account = account;
        this.client = client;
    }

    /// <summary>Chooses the socket option an account's security is opened with.</summary>
    /// <param name="security">How the connection carrying the credential is to be secured.</param>
    /// <returns>An option that fails the connection rather than continuing unencrypted.</returns>
    /// <remarks>
    /// Separate from <see cref="OpenAsync" /> so the mapping this tool's whole security claim rests on is asserted
    /// directly rather than inferred from a connection nobody can open in a unit test. Neither answer may ever become
    /// <see cref="SecureSocketOptions.None" />, <see cref="SecureSocketOptions.Auto" />, or
    /// <see cref="SecureSocketOptions.StartTlsWhenAvailable" />: each of those continues in the clear against a server
    /// that offers no encryption, and the next command this transport sends is the password.
    /// </remarks>
    internal static SecureSocketOptions ResolveSocketOptions(SmtpTransportSecurity security) =>
        security == SmtpTransportSecurity.ImplicitTls
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTls;

    /// <inheritdoc />
    public async Task OpenAsync(CancellationToken cancellationToken)
    {
        var security = ResolveSocketOptions(this.account.Security);

        try
        {
            await this.client.ConnectAsync(this.account.Host, this.account.Port, security, cancellationToken);
        }
        catch (Exception failure) when (IsTransportFailure(failure))
        {
            throw new SyntheticMailFailure(
                $"Could not open a {this.account.Security} connection to {this.account.Host}:{this.account.Port}: {failure.Message}",
                failure);
        }

        try
        {
            await this.client.AuthenticateAsync(this.account.UserName, this.account.Password, cancellationToken);
        }
        catch (Exception failure) when (IsTransportFailure(failure))
        {
            throw new SyntheticMailFailure(
                $"{this.account.Host} refused the configured credential: {failure.Message}",
                failure);
        }
    }

    /// <inheritdoc />
    public async Task SendAsync(MimeMessage message, MailboxAddress recipient, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(recipient);

        try
        {
            await this.client.SendAsync(message, this.account.Address, [recipient], cancellationToken);
        }
        catch (Exception failure) when (IsTransportFailure(failure))
        {
            throw new SyntheticMailFailure(failure.Message, failure);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (this.client.IsConnected)
            {
                await this.client.DisconnectAsync(quit: true, CancellationToken.None);
            }
        }
        catch (Exception failure) when (IsTransportFailure(failure))
        {
            // A session being torn down has nothing left to report. Letting this out of an `await using` would replace
            // whatever actually went wrong with the noise of the connection noticing afterwards.
        }
        finally
        {
            // In a `finally` rather than after the catch, because the filter above deliberately admits only failures
            // the network produced: anything else — a racing cancellation, a MailKit exception this list does not name
            // — leaves through `DisposeAsync`, and the socket underneath would go with it undisposed.
            this.client.Dispose();
        }
    }

    /// <summary>Reports whether a failure is one the server or the network produced rather than a defect here.</summary>
    /// <remarks>
    /// <see cref="NotSupportedException" /> is in the set deliberately: it is what MailKit raises when a server does
    /// not advertise <c>STARTTLS</c>, which is the refusal that keeps the password off an unencrypted socket and is
    /// the most important thing in this list.
    /// </remarks>
    private static bool IsTransportFailure(Exception failure) => failure is SmtpCommandException
        or SmtpProtocolException
        or AuthenticationException
        or SslHandshakeException
        or ServiceNotConnectedException
        or ServiceNotAuthenticatedException
        or SocketException
        or IOException
        or NotSupportedException;
}
