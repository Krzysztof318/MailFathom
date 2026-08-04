// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Transport;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace MailFathom.Host.Security.Endpoints;

/// <summary>Opens the socket the administrative endpoint answers on, and only that one.</summary>
/// <remarks>
/// The endpoint gets a listener of its own rather than a path on the application's, because that is what lets a
/// deployment reach the mailbox over MCP from one network and administer the service from another — or from nowhere at
/// all. A port is the coarsest control an operator has and the only one a firewall can act on, so putting the two
/// surfaces on one socket would take that control away whatever the credentials said.
/// </remarks>
internal static class AdminEndpointListenerBinder
{
    /// <summary>Binds the listener the configured settings call for.</summary>
    /// <param name="kestrelOptions">The server being configured.</param>
    /// <param name="endpointSettings">The endpoint settings composition read.</param>
    /// <param name="certificateStore">The store holding the TLS identity of each configured profile.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// Naming any HTTPS profile binds those and nothing else that serves a route, exactly as the MCP endpoint behaves:
    /// there is no mixed state in which a clear-text listener stays open behind a profile an operator configured TLS for,
    /// because that listener would serve the same administrative routes without the protection the profile was added for.
    /// The redirect listener is not that state and is the reason the distinction is worth stating — it maps no route and
    /// answers every request with the address the profiles are served at, so nothing administrative is reachable over it.
    /// </remarks>
    internal static void Bind(
        KestrelServerOptions kestrelOptions,
        AdminEndpointOptions endpointSettings,
        TransportServerCertificateStore certificateStore)
    {
        ArgumentNullException.ThrowIfNull(kestrelOptions);
        ArgumentNullException.ThrowIfNull(endpointSettings);
        ArgumentNullException.ThrowIfNull(certificateStore);

        if (endpointSettings.Https.TerminatesTls)
        {
            TransportHttpsEndpointBinder.Bind(
                kestrelOptions,
                endpointSettings.Https,
                certificateStore,
                requestClientCertificates: false,
                endpointSettings.Https.RedirectsClearText
                    ? endpointSettings.Https.Redirect.ListenerAddress(
                        AdminEndpointOptions.DefaultClearTextRedirectPort)
                    : null);

            return;
        }

        // Validation has already refused an address that does not parse, so this cannot throw on a configuration an
        // operator could have written.
        kestrelOptions.Listen(
            IPAddress.Parse(endpointSettings.BindAddress.Trim()),
            endpointSettings.Port);
    }
}
