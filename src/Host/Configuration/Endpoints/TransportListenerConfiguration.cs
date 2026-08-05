// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;

namespace MailFathom.Host.Configuration.Endpoints;

/// <summary>The listener rules the request-serving surfaces hold in common.</summary>
/// <remarks>
/// <para>
/// The MCP endpoint and the administrative endpoint state where they are served in the same four settings —
/// <c>BindAddress</c>, <c>Port</c>, <c>Transport</c>, and the <c>Https</c> profiles beneath them — because it is the
/// same question asked of two surfaces. Answering it in one place is what keeps the two from drifting, and what lets a
/// capability added to one of them, mutual TLS in particular, arrive as configuration the other already understands.
/// </para>
/// <para>
/// The sections stay flat rather than nesting these under a shared key. An operator reads <c>McpEndpoint:Port</c> and
/// <c>AdminEndpoint:Port</c>, which is the shape every other setting on those sections has; what is shared here is the
/// rules, not the path.
/// </para>
/// <para>
/// The probes deliberately do not use this. They carry one certificate rather than profiles, serve no redirect, and are
/// reached without a credential, so <see cref="HealthEndpointOptions" /> states its own smaller rules and shares only
/// <see cref="EndpointTransport" /> with these two.
/// </para>
/// </remarks>
internal static class TransportListenerConfiguration
{
    /// <summary>The configuration key each surface names its transport under.</summary>
    private const string TransportKey = "Transport";

    /// <summary>The configuration key each surface names its clear-text bind address under.</summary>
    private const string BindAddressKey = "BindAddress";

    /// <summary>The configuration key each surface names its clear-text port under.</summary>
    private const string PortKey = "Port";

    /// <summary>The configuration key each surface names its HTTPS profiles under.</summary>
    private const string HttpsKey = "Https";

    /// <summary>Reports whether the selected transport terminates TLS through HTTPS profiles.</summary>
    internal static bool TerminatesTls(EndpointTransport transport) =>
        transport is EndpointTransport.HttpAndHttps or EndpointTransport.HttpsOnly;

    /// <summary>Reports whether the selected transport opens a clear-text socket at the surface's own address.</summary>
    internal static bool OpensClearTextListener(EndpointTransport transport) =>
        transport is EndpointTransport.Http or EndpointTransport.HttpAndHttps;

    /// <summary>Reports whether the clear-text socket answers with the address of the TLS one instead of serving routes.</summary>
    /// <param name="transport">The selected transport.</param>
    /// <param name="redirect">The redirect settings.</param>
    /// <returns><see langword="true" /> when the socket only redirects, otherwise <see langword="false" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="redirect" /> is <see langword="null" />.</exception>
    /// <remarks>Both halves are required: a redirect needs a clear-text socket to bind and somewhere to send what arrives on it, which is <see cref="EndpointTransport.HttpAndHttps" /> alone.</remarks>
    internal static bool RedirectsClearText(EndpointTransport transport, TransportClearTextRedirectOptions redirect)
    {
        ArgumentNullException.ThrowIfNull(redirect);

        return transport is EndpointTransport.HttpAndHttps && redirect.Enabled;
    }

    /// <summary>Reads every port the surface's listeners bind under the selected transport.</summary>
    /// <param name="transport">The selected transport.</param>
    /// <param name="port">The port the clear-text socket binds.</param>
    /// <param name="httpsSettings">The HTTPS profiles.</param>
    /// <returns>The ports, without duplicates.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="httpsSettings" /> is <see langword="null" />.</exception>
    /// <remarks>The clear-text port is one of them under every mode that opens that socket, redirect included, so a deployment cannot give it to another surface and discover the conflict as an address-in-use error naming a socket rather than a section.</remarks>
    internal static IReadOnlySet<int> ListenerPorts(
        EndpointTransport transport,
        int port,
        TransportHttpsOptions httpsSettings)
    {
        ArgumentNullException.ThrowIfNull(httpsSettings);

        var ports = new HashSet<int>();

        if (OpensClearTextListener(transport))
        {
            ports.Add(port);
        }

        if (TerminatesTls(transport))
        {
            ports.UnionWith(httpsSettings.ListenerPorts());
        }

        return ports;
    }

    /// <summary>Describes every socket this surface asks for, which is what composition groups and refuses disagreement over.</summary>
    /// <param name="sectionName">The configuration section the surface is bound from.</param>
    /// <param name="surface">The surface served on these sockets.</param>
    /// <param name="bindAddress">The configured clear-text bind address.</param>
    /// <param name="port">The configured clear-text port.</param>
    /// <param name="transport">The selected transport.</param>
    /// <param name="httpsSettings">The HTTPS profiles.</param>
    /// <param name="requestsClientCertificates">Whether the handshake asks the client for a certificate.</param>
    /// <returns>One declaration per socket, empty when the transport opens none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument other than <paramref name="bindAddress" /> is <see langword="null" />.</exception>
    /// <remarks>The profiles are grouped by the socket they bind rather than declared one apiece, because profiles naming one address share a listener and are told apart by the server name a client sends.</remarks>
    internal static IReadOnlyList<DeclaredListener> DeclareListeners(
        string sectionName,
        ServedSurfaces surface,
        string bindAddress,
        int port,
        EndpointTransport transport,
        TransportHttpsOptions httpsSettings,
        bool requestsClientCertificates)
    {
        ArgumentNullException.ThrowIfNull(sectionName);
        ArgumentNullException.ThrowIfNull(httpsSettings);

        var declarations = new List<DeclaredListener>();

        if (OpensClearTextListener(transport))
        {
            declarations.Add(new DeclaredListener(
                sectionName,
                surface,
                bindAddress,
                port,
                TerminatesTls: false,
                RedirectsClearText(transport, httpsSettings.Redirect),
                PresentsProfiles: false,
                Profiles: [],
                RequestsClientCertificates: false));
        }

        if (!TerminatesTls(transport))
        {
            return declarations;
        }

        declarations.AddRange(httpsSettings.Endpoints
            .GroupBy(static profile => (profile.BindAddress, profile.Port))
            .Select(profileSocket => new DeclaredListener(
                sectionName,
                surface,
                profileSocket.Key.BindAddress,
                profileSocket.Key.Port,
                TerminatesTls: true,
                RedirectsClearText: false,
                PresentsProfiles: true,
                [.. profileSocket],
                requestsClientCertificates)));

        return declarations;
    }

    /// <summary>Finds everything an operator must fix before the surface's listeners can bind.</summary>
    /// <param name="sectionName">The configuration section the surface is bound from, which prefixes every reported error.</param>
    /// <param name="bindAddress">The configured clear-text bind address.</param>
    /// <param name="port">The configured clear-text port.</param>
    /// <param name="transport">The selected transport.</param>
    /// <param name="httpsSettings">The HTTPS profiles.</param>
    /// <param name="http3Supported">Whether the host platform can provide the QUIC transport HTTP/3 needs.</param>
    /// <returns>One message per faulty setting, each naming its configuration path, empty when the settings are usable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument other than <paramref name="bindAddress" /> is <see langword="null" />.</exception>
    internal static IReadOnlyList<string> FindConfigurationErrors(
        string sectionName,
        string? bindAddress,
        int port,
        EndpointTransport transport,
        TransportHttpsOptions httpsSettings,
        bool http3Supported)
    {
        ArgumentNullException.ThrowIfNull(sectionName);
        ArgumentNullException.ThrowIfNull(httpsSettings);

        // Every rule below asks what a particular transport does, and a value naming none answers no to all of them:
        // the surface would open no listener and report no error. Refusing it here is what keeps a typo from leaving a
        // deployment with a section it believes it configured and a process serving nothing from it.
        if (!Enum.IsDefined(transport))
        {
            return
            [
                $"{sectionName}:{TransportKey} — '{(int)transport}' names no transport; state '{nameof(EndpointTransport.Http)}', '{nameof(EndpointTransport.HttpAndHttps)}', or '{nameof(EndpointTransport.HttpsOnly)}'.",
            ];
        }

        var errors = new List<string>(FindClearTextListenerErrors(sectionName, bindAddress, port, transport));

        errors.AddRange(FindTransportAgreementErrors(sectionName, transport, httpsSettings));

        var httpsPath = $"{sectionName}:{HttpsKey}";

        if (TerminatesTls(transport))
        {
            errors.AddRange(httpsSettings.FindConfigurationErrors(httpsPath, http3Supported));
        }

        if (transport is EndpointTransport.HttpAndHttps)
        {
            errors.AddRange(httpsSettings.FindClearTextCollisions(httpsPath, bindAddress, port));
        }

        return errors;
    }

    /// <summary>Refuses a clear-text socket that could not bind, and only where one is opened at all.</summary>
    /// <remarks><see cref="EndpointTransport.HttpsOnly" /> opens none, so its address and port describe nothing and are left unjudged rather than reported against a listener that does not exist.</remarks>
    private static IEnumerable<string> FindClearTextListenerErrors(
        string sectionName,
        string? bindAddress,
        int port,
        EndpointTransport transport)
    {
        if (!OpensClearTextListener(transport))
        {
            yield break;
        }

        if (!IPAddress.TryParse(bindAddress?.Trim(), out _))
        {
            yield return $"{sectionName}:{BindAddressKey} — state the IP address to bind, for example '0.0.0.0' for every IPv4 address, '127.0.0.1' to serve this surface to this machine only, or '::' for IPv6.";
        }

        if (port is < 1 or > 65535)
        {
            yield return $"{sectionName}:{PortKey} — '{port}' is not a TCP port; state a value between 1 and 65535.";
        }
    }

    /// <summary>Refuses settings the selected transport never reads.</summary>
    /// <remarks>Configured-but-unread is refused rather than ignored in both directions, because a setting nothing acts on is a deployment believing it selected a posture it did not.</remarks>
    private static IEnumerable<string> FindTransportAgreementErrors(
        string sectionName,
        EndpointTransport transport,
        TransportHttpsOptions httpsSettings)
    {
        var httpsPath = $"{sectionName}:{HttpsKey}";
        var endpointsPath = $"{httpsPath}:{nameof(TransportHttpsOptions.Endpoints)}";

        if (TerminatesTls(transport) && httpsSettings.Endpoints.Count == 0)
        {
            yield return $"{endpointsPath} — '{transport}' terminates TLS and no HTTPS profile is configured, so there would be no certificate to present and nothing served over it.";
        }

        if (!TerminatesTls(transport) && httpsSettings.Endpoints.Count > 0)
        {
            yield return $"{endpointsPath} — HTTPS profiles are configured while '{sectionName}:{TransportKey}' is '{transport}', so none of them is served; select '{nameof(EndpointTransport.HttpAndHttps)}' or '{nameof(EndpointTransport.HttpsOnly)}', or remove them.";
        }

        if (transport is not EndpointTransport.HttpAndHttps && httpsSettings.Redirect.WasStated)
        {
            yield return $"{httpsPath}:{nameof(TransportHttpsOptions.Redirect)} — a clear-text redirect is configured while '{sectionName}:{TransportKey}' is '{transport}', which is a mode with either no clear-text socket to redirect from or no profiles to redirect to. Select '{nameof(EndpointTransport.HttpAndHttps)}', or remove this section.";
        }
    }
}
