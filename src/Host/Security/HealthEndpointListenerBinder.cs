// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.Host.Configuration;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;

namespace MailFathom.Host.Security;

/// <summary>Opens the listener the startup, readiness, and liveness probes are served on.</summary>
/// <remarks>
/// <para>
/// A listener of its own is the exposure control this section delivers. The probes answer without a credential, so what
/// decides who can ask them is which network their port is published on, and that only means anything while the port is
/// not the one MCP clients connect to. The routes are kept apart from the other direction as well, by
/// <see cref="Hosting.HealthEndpointIsolation" />, which is what makes the separation a property of the
/// connection rather than of a request header a caller writes.
/// </para>
/// <para>
/// One socket serves one scheme, so serving both means two listeners on two ports. The TLS listener presents the
/// identity the composition root has already loaded and asks for no client certificate — deliberately, because
/// requesting one is a deployment-wide default the MCP endpoint sets for its own listeners and a probe has no
/// certificate to present.
/// </para>
/// </remarks>
internal static class HealthEndpointListenerBinder
{
    /// <summary>Binds the probe listeners the configured transport calls for.</summary>
    /// <param name="kestrelOptions">The server options being composed.</param>
    /// <param name="healthEndpointSettings">The validated health-endpoint settings.</param>
    /// <param name="certificate">The identity the TLS listener presents, loaded before the server starts.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="FormatException">Thrown when the configured bind address is not an IP address, which validation reports before anything reaches this.</exception>
    internal static void Bind(
        KestrelServerOptions kestrelOptions,
        HealthEndpointOptions healthEndpointSettings,
        HealthEndpointCertificate certificate)
    {
        ArgumentNullException.ThrowIfNull(kestrelOptions);
        ArgumentNullException.ThrowIfNull(healthEndpointSettings);
        ArgumentNullException.ThrowIfNull(certificate);

        var address = IPAddress.Parse(healthEndpointSettings.BindAddress.Trim());

        if (healthEndpointSettings.ServesClearText)
        {
            kestrelOptions.Listen(address, healthEndpointSettings.Port);
        }

        if (!healthEndpointSettings.TerminatesTls)
        {
            return;
        }

        // Never null once the transport terminates TLS, which validation has already proven by the time composition
        // reaches this: HttpAndHttps without a stated port is refused rather than defaulted.
        var tlsPort = healthEndpointSettings.TlsListenerPort!.Value;

        kestrelOptions.Listen(address, tlsPort, listenOptions => listenOptions.UseHttps(httpsOptions =>
        {
            httpsOptions.ServerCertificate = certificate.Leaf;
            httpsOptions.ServerCertificateChain = certificate.Chain;

            // Stated rather than left to the default, because the default is deployment-wide: a deployment whose MCP
            // endpoint accepts client certificates configures Kestrel to ask every HTTPS listener's clients for one,
            // and this listener would otherwise inherit a question no probe can answer.
            httpsOptions.ClientCertificateMode = ClientCertificateMode.NoCertificate;
        }));
    }
}
