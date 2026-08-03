// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.Infrastructure.Certificates;

namespace MailFathom.Host.Configuration;

/// <summary>One HTTPS listener profile: the domain it publishes, the socket it binds, and the identity it presents.</summary>
/// <remarks>
/// <para>
/// The domain is the endpoint's public identity and the name a TLS handshake selects on, not a shortcut for the socket
/// to bind. Which socket to bind is <see cref="BindAddress" /> and <see cref="Port" />, deliberately separate, because
/// a deployment routinely publishes one name while binding an address that name does not resolve to — behind a
/// forwarder, inside a container, on a host with several interfaces.
/// </para>
/// <para>
/// Several profiles may name the same address and port. They then share one listener and are told apart by the server
/// name the client sends, which is what makes one address serve a general MCP client and a managed one under different
/// certificates. Provisioning the DNS record and proving ownership of the name stay the operator's; what this section
/// refuses is a name that could not be a DNS name at all.
/// </para>
/// </remarks>
internal sealed class TransportHttpsEndpointOptions
{
    /// <summary>Gets or sets the operator-chosen identity of this profile, unique across the configured profiles.</summary>
    /// <remarks>It is what every diagnostic names, so a rejected certificate is reported against a profile an operator recognizes rather than against an array position that renumbers on the next edit.</remarks>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the exact DNS domain this profile publishes and selects on, in its ASCII form.</summary>
    /// <remarks>
    /// Exact, so no wildcard and no catch-all: a handshake naming something else is refused rather than answered with
    /// this profile's certificate. An internationalized domain is configured as its punycode A-label, because that is
    /// what a certificate's subject alternative names carry and what a client sends.
    /// </remarks>
    public string Domain { get; set; } = string.Empty;

    /// <summary>Gets or sets the IP address the listener binds, defaulting to every IPv4 address.</summary>
    /// <remarks>Use <c>::</c> to bind IPv6, which on most systems accepts IPv4 connections as well, and a specific address to bind one interface.</remarks>
    public string BindAddress { get; set; } = "0.0.0.0";

    /// <summary>Gets or sets the TCP port the listener binds.</summary>
    /// <remarks>The default is above 1024 so the process needs no privilege to bind it; a deployment serving 443 grants that capability explicitly rather than running as root.</remarks>
    public int Port { get; set; } = 8443;

    /// <summary>Gets or sets the oldest TLS version this profile completes a handshake with.</summary>
    public TransportMinimumTlsVersion MinimumTlsVersion { get; set; } = TransportMinimumTlsVersion.Tls12;

    /// <summary>Gets or sets the HTTP versions this profile serves, or <see langword="null" /> to serve the default HTTP/1.1 and HTTP/2.</summary>
    /// <remarks>
    /// Nullable rather than a pre-filled list, because the configuration binder adds to a collection it finds rather
    /// than replacing it: a default of HTTP/1.1 and HTTP/2 written here would leave an operator who configured HTTP/3
    /// alone serving all three. Absent therefore means the default and an explicitly empty list is a configuration
    /// error, which are two different mistakes and are reported as such.
    /// </remarks>
    public IList<TransportHttpProtocol>? HttpProtocols { get; set; }

    /// <summary>Gets or sets where the certificate and private key this profile presents come from.</summary>
    public TlsServerCertificateOptions ServerCertificate { get; set; } = new();

    /// <summary>Gets the HTTP versions this profile serves, with the default applied.</summary>
    internal IReadOnlyList<TransportHttpProtocol> ServedHttpProtocols => this.HttpProtocols is { Count: > 0 } configured
        ? [.. configured.Distinct()]
        : [TransportHttpProtocol.Http1, TransportHttpProtocol.Http2];

    /// <summary>Gets the address and port this profile binds, which profiles sharing one listener have in common.</summary>
    /// <exception cref="FormatException">Thrown when <see cref="BindAddress" /> is not an IP address, which validation reports before anything reads this.</exception>
    internal TransportHttpsListenerAddress ListenerAddress => new(IPAddress.Parse(this.BindAddress.Trim()), this.Port);

    /// <summary>Finds everything an operator must fix before this profile can be served.</summary>
    /// <param name="configurationPath">The configuration path of this profile, which prefixes every reported error.</param>
    /// <param name="http3Supported">Whether the host platform can provide the QUIC transport HTTP/3 needs.</param>
    /// <returns>One message per faulty setting, empty when the profile is usable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurationPath" /> is <see langword="null" />.</exception>
    internal IEnumerable<string> FindConfigurationErrors(string configurationPath, bool http3Supported)
    {
        ArgumentNullException.ThrowIfNull(configurationPath);

        if (string.IsNullOrWhiteSpace(this.Name))
        {
            yield return $"{configurationPath}:{nameof(this.Name)} — an HTTPS profile must carry a name, which is what every diagnostic about it reports against.";
        }

        foreach (var error in this.FindDomainErrors(configurationPath))
        {
            yield return error;
        }

        if (this.Port is < 1 or > 65535)
        {
            yield return $"{configurationPath}:{nameof(this.Port)} — '{this.Port}' is not a TCP port; state a value between 1 and 65535.";
        }

        if (!IPAddress.TryParse(this.BindAddress?.Trim(), out _))
        {
            yield return $"{configurationPath}:{nameof(this.BindAddress)} — state the IP address to bind, for example '0.0.0.0' for every IPv4 address or '::' for IPv6.";
        }

        if (!Enum.IsDefined(this.MinimumTlsVersion))
        {
            yield return $"{configurationPath}:{nameof(this.MinimumTlsVersion)} — '{(int)this.MinimumTlsVersion}' names no TLS version; state '{nameof(TransportMinimumTlsVersion.Tls12)}' or '{nameof(TransportMinimumTlsVersion.Tls13)}'.";
        }

        foreach (var error in this.FindHttpProtocolErrors(configurationPath, http3Supported))
        {
            yield return error;
        }

        // The shape of the material block is the same question for every listener that terminates TLS, so it is asked
        // by the type that carries it rather than restated per section.
        foreach (var error in this.ServerCertificate.FindConfigurationErrors($"{configurationPath}:{nameof(this.ServerCertificate)}"))
        {
            yield return error;
        }
    }

    private IEnumerable<string> FindDomainErrors(string configurationPath)
    {
        var settingPath = $"{configurationPath}:{nameof(this.Domain)}";

        if (string.IsNullOrWhiteSpace(this.Domain))
        {
            yield return $"{settingPath} — an HTTPS profile must state the DNS domain it publishes, which is the name clients connect to and the name its certificate has to cover.";

            yield break;
        }

        foreach (var error in ConfiguredDnsName.FindErrors(this.Domain, settingPath))
        {
            yield return error;
        }
    }

    private IEnumerable<string> FindHttpProtocolErrors(string configurationPath, bool http3Supported)
    {
        var settingPath = $"{configurationPath}:{nameof(this.HttpProtocols)}";

        if (this.HttpProtocols is not { } configured)
        {
            yield break;
        }

        if (configured.Count == 0)
        {
            yield return $"{settingPath} — the list is empty, so the profile would serve no HTTP version at all; remove it to serve the default '{nameof(TransportHttpProtocol.Http1)}' and '{nameof(TransportHttpProtocol.Http2)}', or name the versions to serve.";

            yield break;
        }

        var undefined = configured.Where(static protocol => !Enum.IsDefined(protocol)).ToArray();

        foreach (var protocol in undefined)
        {
            yield return $"{settingPath} — '{(int)protocol}' names no HTTP version; state '{nameof(TransportHttpProtocol.Http1)}', '{nameof(TransportHttpProtocol.Http2)}', or '{nameof(TransportHttpProtocol.Http3)}'.";
        }

        if (configured.Distinct().Count() != configured.Count)
        {
            yield return $"{settingPath} — an HTTP version is listed more than once; each version is served or it is not, so a repeat says nothing a single entry does not.";
        }

        // Reported rather than silently dropped: an operator who asked for HTTP/3 and got HTTP/2 would read a working
        // endpoint and never learn that the version they configured is not the one being served.
        if (!http3Supported && undefined.Length == 0 && configured.Contains(TransportHttpProtocol.Http3))
        {
            yield return $"{settingPath} — '{nameof(TransportHttpProtocol.Http3)}' is configured and this host cannot provide the QUIC transport it needs; install the platform's QUIC support or remove the version rather than have it quietly fall back.";
        }
    }
}
