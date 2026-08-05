// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Transport;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;

namespace MailFathom.Host.Security.Endpoints;

/// <summary>Opens the sockets the composition asked for, one <c>Listen</c> per socket.</summary>
/// <remarks>
/// <para>
/// Binding is driven by the composed sockets rather than by the surfaces, because a socket may serve more than one of
/// them. A deployment that puts the MCP endpoint and the administrative endpoint on one port publishes one socket, and
/// asking Kestrel to open it once per surface would fail the second bind and take the process down with an
/// address-in-use error naming a socket rather than the sections that asked for it.
/// </para>
/// <para>
/// What each surface still decides for itself is which routes it answers and what guards them. Which paths a request
/// arriving on a shared port may ask for is the isolation middlewares' question, answered from the same composition.
/// </para>
/// </remarks>
internal static class TransportListenerBinder
{
    /// <summary>Binds every composed socket.</summary>
    /// <param name="kestrelOptions">The server options being composed.</param>
    /// <param name="listeners">The composed sockets.</param>
    /// <param name="findProfileIdentity">Reads the identity a server name resolves to on a profile-backed TLS socket.</param>
    /// <param name="probeCertificate">The identity a probe TLS socket presents, read when one is composed.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="FormatException">Thrown when a configured bind address is not an IP address, which validation reports before anything reaches this.</exception>
    internal static void Bind(
        KestrelServerOptions kestrelOptions,
        IReadOnlyList<ComposedListener> listeners,
        Func<TransportHttpsListenerAddress, string?, TransportTlsEndpointIdentity?> findProfileIdentity,
        Func<HealthEndpointCertificate> probeCertificate)
    {
        ArgumentNullException.ThrowIfNull(kestrelOptions);
        ArgumentNullException.ThrowIfNull(listeners);
        ArgumentNullException.ThrowIfNull(findProfileIdentity);
        ArgumentNullException.ThrowIfNull(probeCertificate);

        foreach (var listener in listeners)
        {
            Bind(kestrelOptions, listener, findProfileIdentity, probeCertificate);
        }
    }

    private static void Bind(
        KestrelServerOptions kestrelOptions,
        ComposedListener listener,
        Func<TransportHttpsListenerAddress, string?, TransportTlsEndpointIdentity?> findProfileIdentity,
        Func<HealthEndpointCertificate> probeCertificate)
    {
        if (!listener.TerminatesTls)
        {
            // No protocol selection, deliberately. Under a redirect this socket exists to accept whatever a client that
            // was never repointed happens to speak, and narrowing it would turn the client this helps into a connection
            // failure; where it serves the routes instead, the listener default is the same HTTP/1.1 and HTTP/2 a
            // profile serves without one.
            kestrelOptions.Listen(listener.Address.Address, listener.Address.Port);

            return;
        }

        if (listener.PresentsProfiles)
        {
            TransportHttpsEndpointBinder.Bind(kestrelOptions, listener, findProfileIdentity);

            return;
        }

        // The probes' own TLS socket: one certificate presented to every connection rather than a profile selected by
        // server name. The composition refuses to share this socket with a surface that selects, so nothing else is
        // served here and the client-certificate mode is stated rather than inherited — a probe has none to present.
        kestrelOptions.Listen(
            listener.Address.Address,
            listener.Address.Port,
            listenOptions => listenOptions.UseHttps(httpsOptions =>
            {
                var certificate = probeCertificate();

                httpsOptions.ServerCertificate = certificate.Leaf;
                httpsOptions.ServerCertificateChain = certificate.Chain;
                httpsOptions.ClientCertificateMode = ClientCertificateMode.NoCertificate;
            }));
    }
}
