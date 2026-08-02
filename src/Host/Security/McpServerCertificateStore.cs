// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using MailFathom.Host.Configuration;
using MailFathom.Infrastructure.Certificates;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Security;

/// <summary>Holds the TLS identity of every configured HTTPS profile, and answers which one a handshake gets.</summary>
/// <remarks>
/// <para>
/// The certificates are loaded once, by the composition root, before the server is started — not lazily on the first
/// handshake and not by a hosted service. Both alternatives would let the listener bind and then fail per connection,
/// so an operator whose certificate expired last night would learn it from a client's error rather than from a host
/// that refused to start. Loading before the server means a rejected certificate is a startup failure with nothing
/// listening, which is the only outcome that cannot be mistaken for a working deployment.
/// </para>
/// <para>
/// Lookup is by listener first and server name second, so a name configured for one address is not served on another.
/// A handshake whose server name matches no profile on the listener it arrived at is answered with nothing, which
/// refuses the connection rather than presenting an unrelated profile's certificate to a client that asked for
/// something else.
/// </para>
/// <para>
/// The store owns every certificate it loaded and releases them on disposal, which the container performs at shutdown.
/// </para>
/// </remarks>
internal sealed partial class McpServerCertificateStore : IDisposable
{
    private readonly McpHttpsOptions httpsSettings;
    private readonly TlsServerCertificateLoader certificateLoader;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<McpServerCertificateStore> logger;
    private readonly List<TlsServerCertificate> ownedCertificates = [];

    // Frozen because it is built once at startup and then read on every handshake, which is exactly the shape the
    // frozen collections are for. Empty until loading has published, so a handshake that somehow arrives first is
    // refused rather than served by a half-filled lookup.
    private FrozenDictionary<McpHttpsListenerAddress, FrozenDictionary<string, McpTlsEndpointIdentity>> identities =
        FrozenDictionary<McpHttpsListenerAddress, FrozenDictionary<string, McpTlsEndpointIdentity>>.Empty;

    private bool disposed;

    /// <summary>Initializes a new certificate store over the configured HTTPS profiles.</summary>
    /// <param name="endpointSettings">The endpoint settings composition read.</param>
    /// <param name="certificateLoader">The loader that turns configured material into a validated identity.</param>
    /// <param name="timeProvider">The clock expiry notices are measured against.</param>
    /// <param name="logger">The startup logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public McpServerCertificateStore(
        IOptions<McpEndpointOptions> endpointSettings,
        TlsServerCertificateLoader certificateLoader,
        TimeProvider timeProvider,
        ILogger<McpServerCertificateStore> logger)
    {
        ArgumentNullException.ThrowIfNull(endpointSettings);
        ArgumentNullException.ThrowIfNull(certificateLoader);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.httpsSettings = endpointSettings.Value.Https;
        this.certificateLoader = certificateLoader;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    /// <summary>Loads and publishes the identity of every configured profile, or refuses to publish any of them.</summary>
    /// <param name="cancellationToken">Cancels the material retrieval.</param>
    /// <returns>A task that completes once every configured profile has a usable identity.</returns>
    /// <exception cref="OptionsValidationException">Thrown when any configured profile produced no usable identity.</exception>
    /// <remarks>
    /// Every profile is attempted before anything is thrown, so an operator who provisioned two endpoints wrongly reads
    /// both in one message instead of one restart at a time. Nothing is published when any of them failed: a deployment
    /// that serves one of its two configured domains and silently refuses the other is harder to diagnose than one that
    /// does not start.
    /// </remarks>
    internal async Task LoadAsync(CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var loaded = new Dictionary<McpHttpsListenerAddress, Dictionary<string, McpTlsEndpointIdentity>>();

        // The loop stays a loop rather than becoming a projection: each step awaits a retrieval, and each successful
        // step takes ownership of a certificate that has to be released even when a later step fails.
        foreach (var (index, endpoint) in this.httpsSettings.Endpoints.Index())
        {
            var configurationPath = $"{McpEndpointOptions.SectionName}:{nameof(McpEndpointOptions.Https)}:{nameof(McpHttpsOptions.Endpoints)}:{index}";
            var domain = endpoint.Domain.Trim();

            var result = await this.certificateLoader.LoadAsync(
                endpoint.ServerCertificate,
                domain,
                cancellationToken);

            if (result.Certificate is not { } certificate)
            {
                errors.Add($"{configurationPath} — the HTTPS profile '{endpoint.Name}' has no usable server certificate [{result.Failure}].");

                continue;
            }

            this.ownedCertificates.Add(certificate);
            this.ReportLoaded(endpoint.Name, certificate.Leaf);

            var identity = new McpTlsEndpointIdentity(
                endpoint.Name,
                SslStreamCertificateContext.Create(certificate.Leaf, [.. certificate.Intermediates]),
                EnabledProtocolsFor(endpoint.MinimumTlsVersion));

            if (!loaded.TryGetValue(endpoint.ListenerAddress, out var perListener))
            {
                perListener = new Dictionary<string, McpTlsEndpointIdentity>(StringComparer.OrdinalIgnoreCase);
                loaded.Add(endpoint.ListenerAddress, perListener);
            }

            perListener[domain] = identity;
        }

        if (errors.Count > 0)
        {
            throw new OptionsValidationException(
                McpEndpointOptions.SectionName,
                typeof(McpEndpointOptions),
                errors);
        }

        this.identities = loaded.ToFrozenDictionary(
            static listener => listener.Key,
            static listener => listener.Value.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Finds the identity a handshake on one listener receives for the server name it asked for.</summary>
    /// <param name="listener">The address and port the connection arrived at.</param>
    /// <param name="serverName">The server name the client sent, or <see langword="null" /> when it sent none.</param>
    /// <returns>The identity, or <see langword="null" /> when no configured profile serves that name on that listener.</returns>
    /// <remarks>
    /// A client that sends no server name is answered with nothing rather than with a default profile. Every profile
    /// publishes an exact domain, so a connection that names none is one this deployment has no identity for — and
    /// picking one anyway would hand a certificate to a client that never asked whether it was the right one.
    /// </remarks>
    internal McpTlsEndpointIdentity? Find(McpHttpsListenerAddress listener, string? serverName)
    {
        if (string.IsNullOrEmpty(serverName))
        {
            return null;
        }

        return this.identities.TryGetValue(listener, out var perListener)
            && perListener.TryGetValue(serverName, out var identity)
                ? identity
                : null;
    }

    /// <summary>Maps the configured floor onto the versions the platform is allowed to negotiate.</summary>
    /// <remarks>
    /// A floor rather than a selection, so 1.2 still admits 1.3. Nothing below 1.2 appears in either branch, which is
    /// what makes a deprecated version unreachable by configuration rather than merely undocumented.
    /// </remarks>
    [SuppressMessage("Security", "CA5398:Avoid hardcoded SslProtocols values", Justification = "Naming the versions is the feature. CA5398 asks for SslProtocols.None so the operating system chooses, which is the opposite of what this setting exists to do: it hands the floor back to machine policy, where a deployment that must refuse TLS 1.2 cannot state so and a policy change can silently lower what the endpoint accepts. Both values named here are current, nothing below TLS 1.2 is reachable through the configuration that reaches this method, and a future version is added by extending McpMinimumTlsVersion rather than by inheriting whatever the machine allows.")]
    private static SslProtocols EnabledProtocolsFor(McpMinimumTlsVersion minimumVersion) =>
        minimumVersion == McpMinimumTlsVersion.Tls13
            ? SslProtocols.Tls13
            : SslProtocols.Tls12 | SslProtocols.Tls13;

    /// <summary>Records that a profile has an identity, and how long that identity lasts.</summary>
    /// <remarks>
    /// The profile name and the expiry instant are the whole of it. The subject, the serial number, and the thumbprint
    /// are deliberately absent: they identify the certificate wherever this log is read or shipped, and an operator
    /// renewing it needs to know which profile and by when, not which certificate it was.
    /// </remarks>
    private void ReportLoaded(string profileName, X509Certificate2 leaf)
    {
        var expiration = ServerCertificateExpiry.ExpirationOf(leaf);

        if (ServerCertificateExpiry.IsExpiringSoon(expiration, this.timeProvider.GetUtcNow()))
        {
            this.LogServerCertificateExpiringSoon(profileName, expiration);

            return;
        }

        this.LogServerCertificateLoaded(profileName, expiration);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;

        foreach (var certificate in this.ownedCertificates)
        {
            certificate.Dispose();
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The MCP HTTPS profile {HttpsProfileName} presents a server certificate valid until {ServerCertificateExpiration:u}.")]
    private partial void LogServerCertificateLoaded(string httpsProfileName, DateTimeOffset serverCertificateExpiration);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The MCP HTTPS profile {HttpsProfileName} presents a server certificate that expires at {ServerCertificateExpiration:u}. "
            + "Renew it before then: once it expires the profile stops starting, because a certificate outside its validity "
            + "period is refused rather than served.")]
    private partial void LogServerCertificateExpiringSoon(string httpsProfileName, DateTimeOffset serverCertificateExpiration);
}
