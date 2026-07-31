// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Diagnostics.CodeAnalysis;
using System.Net;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Secrets;

namespace MailFathom.Host.Configuration;

/// <summary>Configures whether the startup, readiness, and liveness probes are served, on which socket, and under which transport.</summary>
/// <remarks>
/// <para>
/// The probes answer without a credential, so what controls their exposure is which network their port is published on.
/// That is why they get a listener of their own instead of sharing the one that serves <c>/</c> and <c>/mcp</c>: an
/// operator publishes the probe port to the orchestrator and publishes the application port to clients, and neither
/// port answers the other's paths. The scaffold this replaced controlled the same exposure by environment name, which
/// left every container and Kubernetes deployment with no probe to call and a development run serving them on the
/// listener its MCP clients reach.
/// </para>
/// <para>
/// The decision is identical in every environment. Nothing here reads <c>IHostEnvironment</c>, so what a developer
/// tests is what runs in production, and the only difference between two deployments is what each one configured.
/// </para>
/// <para>
/// The section is read once, while the host is being composed, because it decides which sockets are opened and which
/// routes exist. A change takes effect on restart; the material behind a configured certificate reference is a
/// different matter and is retrieved before the server starts.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class HealthEndpointOptions
{
    /// <summary>The configuration section the health-endpoint settings are bound from.</summary>
    public const string SectionName = "HealthEndpoints";

    /// <summary>The port the probes are served on when a deployment configures none.</summary>
    /// <remarks>Above 1024 so the process needs no privilege to bind it, and beside the 8080 an application listener conventionally takes, so the two defaults do not collide.</remarks>
    internal const int DefaultPort = 8081;

    /// <summary>Gets or sets whether the probes are served at all.</summary>
    /// <remarks>
    /// Enabled unless a deployment states otherwise, because a process an orchestrator cannot probe is one it cannot
    /// gate traffic to or restart. Turning it off maps no probe route and opens no probe listener, and changes nothing
    /// else about the host.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the IP address the probe listener binds, defaulting to every IPv4 address.</summary>
    /// <remarks>Use <c>127.0.0.1</c> to restrict the probes to the machine, one interface's address to restrict them to that interface, and <c>::</c> to bind IPv6, which on most systems accepts IPv4 connections as well.</remarks>
    public string BindAddress { get; set; } = "0.0.0.0";

    /// <summary>Gets or sets the TCP port the probes are served on.</summary>
    /// <remarks>Under <see cref="HealthEndpointTransport.HttpsOnly" /> this port is the TLS one, because that mode opens no clear-text listener at all.</remarks>
    public int Port { get; set; } = DefaultPort;

    /// <summary>Gets or sets the TCP port the TLS listener binds when both schemes are served.</summary>
    /// <remarks>
    /// Unset by default and required by <see cref="HealthEndpointTransport.HttpAndHttps" /> alone. One socket serves one
    /// scheme, so the second port is what makes that mode possible; defaulting it would pick a port an operator never
    /// published and could collide with something already listening.
    /// </remarks>
    public int? HttpsPort { get; set; }

    /// <summary>Gets or sets which schemes the probe listener is opened under.</summary>
    public HealthEndpointTransport Transport { get; set; } = HealthEndpointTransport.Http;

    /// <summary>Gets or sets the exact DNS domain the TLS listener's certificate covers, in its ASCII form.</summary>
    /// <remarks>
    /// Required by the two TLS modes, because a server certificate is issued for a name and the loader proves the
    /// configured material covers the one this endpoint claims. An orchestrator dialling the pod's address rather than
    /// this name is the ordinary case and verifies nothing, which does not make the claim optional: the operator still
    /// has to say which certificate this is, and a mismatch is a provisioning mistake worth failing on.
    /// </remarks>
    public string Domain { get; set; } = string.Empty;

    /// <summary>Gets or sets where the certificate and private key the TLS listener presents come from.</summary>
    /// <remarks>The same named-secret contract the MCP endpoint's HTTPS profiles use, resolved by the same loader. There is no second loader, no development-certificate fallback, and no self-signed fallback.</remarks>
    public TlsServerCertificateOptions ServerCertificate { get; set; } = new();

    /// <summary>Gets whether the configured transport opens a TLS listener.</summary>
    public bool TerminatesTls => this.Transport is HealthEndpointTransport.HttpAndHttps or HealthEndpointTransport.HttpsOnly;

    /// <summary>Gets whether the configured transport opens a clear-text listener.</summary>
    public bool ServesClearText => this.Transport is HealthEndpointTransport.Http or HealthEndpointTransport.HttpAndHttps;

    /// <summary>Gets the port the TLS listener binds, or <see langword="null" /> when the transport opens none.</summary>
    /// <remarks><see cref="HealthEndpointTransport.HttpsOnly" /> serves TLS on <see cref="Port" /> itself, because it opens no clear-text listener for that port to belong to.</remarks>
    public int? TlsListenerPort => this.Transport switch
    {
        HealthEndpointTransport.HttpsOnly => this.Port,
        HealthEndpointTransport.HttpAndHttps => this.HttpsPort,
        _ => null,
    };

    /// <summary>Gets the ports the probes answer on, which is what tells a probe request apart from an application one.</summary>
    /// <remarks>Empty when the probes are disabled, which is the state in which no listener of this kind exists and every probe path is unmapped.</remarks>
    public IReadOnlySet<int> ListenerPorts
    {
        get
        {
            if (!this.Enabled)
            {
                return new HashSet<int>();
            }

            var ports = new HashSet<int> { this.Port };

            if (this.TlsListenerPort is { } tlsPort)
            {
                ports.Add(tlsPort);
            }

            return ports;
        }
    }

    /// <summary>Reads the section the way composition does, defaults included.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The bound settings.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Strict binding is part of the read rather than something a caller opts into. The section decides which sockets
    /// are opened and whether they carry TLS, so a misspelled key that bound quietly would leave a deployment serving a
    /// posture nobody selected — a probe port an operator believed was off, or clear text where they wrote TLS.
    /// </remarks>
    public static HealthEndpointOptions ReadFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration.GetSection(SectionName)
            .Get<HealthEndpointOptions>(binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
            ?? new HealthEndpointOptions();
    }

    /// <summary>Finds everything an operator must fix before the probes can be served.</summary>
    /// <param name="applicationListenerPorts">The ports the application listener binds, which the probe listener must not take.</param>
    /// <returns>One message per faulty setting, each naming its configuration path, empty when the settings are usable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="applicationListenerPorts" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Whether the configured certificate material resolves and is usable is not asked here. It is proven against the
    /// real material before the server starts, so an unusable certificate fails startup with nothing listening rather
    /// than with a TLS listener already bound.
    /// </remarks>
    public IReadOnlyList<string> FindConfigurationErrors(IReadOnlyCollection<int> applicationListenerPorts)
    {
        ArgumentNullException.ThrowIfNull(applicationListenerPorts);

        if (!this.Enabled)
        {
            return [];
        }

        var errors = new List<string>(this.FindListenerErrors(applicationListenerPorts));

        errors.AddRange(this.FindTransportErrors());

        return errors;
    }

    private IEnumerable<string> FindListenerErrors(IReadOnlyCollection<int> applicationListenerPorts)
    {
        if (!IPAddress.TryParse(this.BindAddress?.Trim(), out _))
        {
            yield return $"{SectionName}:{nameof(this.BindAddress)} — state the IP address to bind, for example '0.0.0.0' for every IPv4 address, '127.0.0.1' to serve the probes to this machine only, or '::' for IPv6.";
        }

        foreach (var error in FindPortRangeErrors(this.Port, nameof(this.Port)))
        {
            yield return error;
        }

        if (this.HttpsPort is { } httpsPort)
        {
            foreach (var error in FindPortRangeErrors(httpsPort, nameof(this.HttpsPort)))
            {
                yield return error;
            }
        }

        // The application listener is what MCP clients reach. A probe listener sharing its port would either fail to
        // bind and take the process down with an address-in-use error naming a socket rather than a setting, or — where
        // the addresses differ — serve the probes to whoever can reach the application port, which is the exposure this
        // section exists to control.
        foreach (var port in this.ListenerPorts.Where(applicationListenerPorts.Contains))
        {
            yield return $"{SectionName} — port {port} is already the application listener's, and the probes are served on a listener of their own so that reaching them does not mean reaching the MCP endpoint. State a port no application listener binds.";
        }

        if (this.Transport is HealthEndpointTransport.HttpAndHttps && this.HttpsPort == this.Port)
        {
            yield return $"{SectionName}:{nameof(this.HttpsPort)} — '{this.Port}' is already the clear-text probe port, and one socket cannot serve both schemes. State a second port for the TLS listener.";
        }
    }

    private IEnumerable<string> FindTransportErrors()
    {
        if (!Enum.IsDefined(this.Transport))
        {
            yield return $"{SectionName}:{nameof(this.Transport)} — '{(int)this.Transport}' names no transport; state '{nameof(HealthEndpointTransport.Http)}', '{nameof(HealthEndpointTransport.HttpAndHttps)}', or '{nameof(HealthEndpointTransport.HttpsOnly)}'.";

            yield break;
        }

        if (this.Transport is HealthEndpointTransport.HttpAndHttps && this.HttpsPort is null)
        {
            yield return $"{SectionName}:{nameof(this.HttpsPort)} — '{nameof(HealthEndpointTransport.HttpAndHttps)}' serves both schemes and one socket serves one scheme, so the TLS listener needs a port of its own. State one, or select '{nameof(HealthEndpointTransport.Http)}' or '{nameof(HealthEndpointTransport.HttpsOnly)}'.";
        }

        // Refused rather than ignored, for the reason the certificate block below is: a port an operator published and
        // nothing binds is a deployment believing its probes answer somewhere they do not.
        if (this.Transport is not HealthEndpointTransport.HttpAndHttps && this.HttpsPort is not null)
        {
            yield return $"{SectionName}:{nameof(this.HttpsPort)} — a second port is configured while '{nameof(this.Transport)}' opens one listener, so nothing binds it. Select '{nameof(HealthEndpointTransport.HttpAndHttps)}', or remove it and state the port through '{nameof(this.Port)}'.";
        }

        if (!this.TerminatesTls)
        {
            if (IsConfigured(this.ServerCertificate?.Bundle)
                || IsConfigured(this.ServerCertificate?.CertificateChain)
                || IsConfigured(this.ServerCertificate?.PrivateKey))
            {
                yield return $"{SectionName}:{nameof(this.ServerCertificate)} — a server certificate is configured while '{nameof(this.Transport)}' opens no TLS listener, so nothing presents it; select '{nameof(HealthEndpointTransport.HttpAndHttps)}' or '{nameof(HealthEndpointTransport.HttpsOnly)}', or remove the material.";
            }

            yield break;
        }

        if (string.IsNullOrWhiteSpace(this.Domain))
        {
            yield return $"{SectionName}:{nameof(this.Domain)} — a TLS transport must state the DNS domain its certificate covers, which is what the configured material is proven against before the listener is opened.";
        }

        foreach (var error in this.FindServerCertificateErrors())
        {
            yield return error;
        }
    }

    /// <summary>Refuses a certificate block that names neither kind of material or both of them.</summary>
    /// <remarks>Only the shape is decided here, on the same terms the MCP HTTPS profiles state it. Whether the referenced material resolves, parses, carries a matching private key, and covers the domain is the loader's question.</remarks>
    private IEnumerable<string> FindServerCertificateErrors()
    {
        var settingPath = $"{SectionName}:{nameof(this.ServerCertificate)}";
        var bundleConfigured = IsConfigured(this.ServerCertificate?.Bundle);
        var chainConfigured = IsConfigured(this.ServerCertificate?.CertificateChain);
        var privateKeyConfigured = IsConfigured(this.ServerCertificate?.PrivateKey);

        if (bundleConfigured && (chainConfigured || privateKeyConfigured))
        {
            yield return $"{settingPath} — a PKCS#12 bundle and separate PEM material are both configured; state one or the other, because which of them supplies the identity would otherwise be decided by nothing an operator wrote.";

            yield break;
        }

        if (bundleConfigured)
        {
            yield break;
        }

        if (!chainConfigured && !privateKeyConfigured)
        {
            yield return $"{settingPath} — a TLS transport must state where its certificate comes from: a '{nameof(TlsServerCertificateOptions.Bundle)}' holding a PKCS#12 bundle, or a '{nameof(TlsServerCertificateOptions.CertificateChain)}' beside its '{nameof(TlsServerCertificateOptions.PrivateKey)}'. There is no development-certificate fallback and no self-signed one, because a probe answering on a port an operator believed was TLS is worse than one that does not answer.";

            yield break;
        }

        if (!chainConfigured)
        {
            yield return $"{settingPath}:{nameof(TlsServerCertificateOptions.CertificateChain)} — a private key is configured with no certificate to pair it with.";
        }

        if (!privateKeyConfigured)
        {
            yield return $"{settingPath}:{nameof(TlsServerCertificateOptions.PrivateKey)} — a certificate is configured with no private key, so the listener could name the domain but not prove it is the domain.";
        }
    }

    private static IEnumerable<string> FindPortRangeErrors(int port, string settingName)
    {
        if (port is < 1 or > 65535)
        {
            yield return $"{SectionName}:{settingName} — '{port}' is not a TCP port; state a value between 1 and 65535.";
        }
    }

    private static bool IsConfigured(ConfiguredSecret? block) =>
        block is not null && !string.IsNullOrWhiteSpace(block.SecretReference);
}
