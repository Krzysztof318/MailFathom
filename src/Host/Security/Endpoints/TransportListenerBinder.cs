// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Transport;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace MailFathom.Host.Security.Endpoints;

/// <summary>Opens the sockets a request-serving surface is served on, and only those.</summary>
/// <remarks>
/// <para>
/// Each surface gets listeners of its own rather than a path on a shared one, because that is what lets a deployment
/// reach the mailbox over MCP from one network and administer the service from another — or from nowhere at all. A port
/// is the coarsest control an operator has and the only one a firewall can act on, so putting two surfaces on one socket
/// would take that control away whatever the credentials said.
/// </para>
/// <para>
/// One binder serves both surfaces because both answer the same question in the same settings. What differs between
/// them — which credentials guard the routes, whether a client certificate is asked for, which origins are served — is
/// decided by the caller and by the pipeline, never here.
/// </para>
/// <para>
/// <see cref="EndpointTransport.HttpAndHttps" /> is the one mode that opens both kinds of socket, and it is deliberate
/// rather than a state arrived at by accident: whether the clear-text one redirects or serves the routes is the
/// redirect section's answer, and the pipeline reads it from the port a connection arrived on.
/// <see cref="EndpointTransport.HttpsOnly" /> opens no clear-text socket at all, which is the promise that nothing
/// stays behind the profiles serving the same routes without the protection they were configured to add.
/// </para>
/// </remarks>
internal static class TransportListenerBinder
{
    /// <summary>Binds the listeners the configured transport calls for.</summary>
    /// <param name="kestrelOptions">The server options being composed.</param>
    /// <param name="transport">The schemes the surface is served under.</param>
    /// <param name="bindAddress">The address the clear-text socket binds.</param>
    /// <param name="port">The port the clear-text socket binds.</param>
    /// <param name="httpsSettings">The HTTPS profiles, read only under a transport that terminates TLS.</param>
    /// <param name="certificateStore">The store the handshake reads its identities from.</param>
    /// <param name="requestClientCertificates">Whether the handshake asks the client for a certificate.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument other than <paramref name="bindAddress" /> is <see langword="null" />.</exception>
    /// <exception cref="FormatException">Thrown when the configured bind address is not an IP address, which validation reports before anything reaches this.</exception>
    internal static void Bind(
        KestrelServerOptions kestrelOptions,
        EndpointTransport transport,
        string bindAddress,
        int port,
        TransportHttpsOptions httpsSettings,
        TransportServerCertificateStore certificateStore,
        bool requestClientCertificates)
    {
        ArgumentNullException.ThrowIfNull(kestrelOptions);
        ArgumentNullException.ThrowIfNull(httpsSettings);
        ArgumentNullException.ThrowIfNull(certificateStore);

        if (TransportListenerConfiguration.OpensClearTextListener(transport))
        {
            // No protocol selection, deliberately. Under a redirect this socket exists to accept whatever a client that
            // was never repointed happens to speak, and narrowing it would turn the client this helps into a connection
            // failure; where it serves the routes instead, the listener default is the same HTTP/1.1 and HTTP/2 a
            // profile serves without one.
            //
            // Validation has already refused an address that does not parse, so this cannot throw on a configuration an
            // operator could have written.
            kestrelOptions.Listen(IPAddress.Parse(bindAddress.Trim()), port);
        }

        if (TransportListenerConfiguration.TerminatesTls(transport))
        {
            TransportHttpsEndpointBinder.Bind(
                kestrelOptions,
                httpsSettings,
                certificateStore,
                requestClientCertificates);
        }
    }
}
