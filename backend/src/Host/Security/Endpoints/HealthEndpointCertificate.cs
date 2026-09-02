// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography.X509Certificates;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Transport;
using MailFathom.Infrastructure.Certificates;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Security.Endpoints;

/// <summary>Holds the TLS identity the health-endpoint listener presents, loaded before the listener is opened.</summary>
/// <remarks>
/// <para>
/// The material is retrieved through the same loader the MCP endpoint's HTTPS profiles use, so a deployment provisions
/// one kind of certificate reference for both. There is no second loader, no ASP.NET Core development-certificate
/// fallback, and no self-signed fallback: material that is missing, unresolvable, expired, or unusable fails startup
/// with nothing listening, because a probe answering on a port an operator believed was TLS is worse than one that
/// does not answer at all.
/// </para>
/// <para>
/// Loading happens in the composition root, before the server is started, for the same reason it does for the MCP
/// profiles: a certificate proven after the listener has bound would be proven after the port was already open, and an
/// operator whose certificate expired last night would learn it from a probe failure rather than from a host that
/// refused to start.
/// </para>
/// <para>
/// The instance owns the certificate it loaded and releases it on disposal, which the container performs at shutdown.
/// </para>
/// </remarks>
internal sealed partial class HealthEndpointCertificate : IDisposable
{
    private readonly HealthEndpointOptions healthEndpointSettings;
    private readonly TlsServerCertificateLoader certificateLoader;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<HealthEndpointCertificate> logger;

    private TlsServerCertificate? identity;
    private bool disposed;

    /// <summary>Initializes a new health-endpoint certificate holder.</summary>
    /// <param name="healthEndpointSettings">The health-endpoint settings composition read.</param>
    /// <param name="certificateLoader">The loader that turns configured material into a validated identity.</param>
    /// <param name="timeProvider">The clock the expiry notice is measured against.</param>
    /// <param name="logger">The startup logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public HealthEndpointCertificate(
        IOptions<HealthEndpointOptions> healthEndpointSettings,
        TlsServerCertificateLoader certificateLoader,
        TimeProvider timeProvider,
        ILogger<HealthEndpointCertificate> logger)
    {
        ArgumentNullException.ThrowIfNull(healthEndpointSettings);
        ArgumentNullException.ThrowIfNull(certificateLoader);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.healthEndpointSettings = healthEndpointSettings.Value;
        this.certificateLoader = certificateLoader;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    /// <summary>Gets the leaf certificate the TLS listener presents, which carries the private key the handshake signs with.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the listener is being bound before the material has been loaded, which is a composition-order defect rather than a configuration one.</exception>
    internal X509Certificate2 Leaf => this.identity?.Leaf
        ?? throw new InvalidOperationException(
            "The health endpoint's server certificate is being read before it was loaded. Composition loads it before the server starts.");

    /// <summary>Gets the intermediate certificates presented after the leaf, so a client can build a path to a root it trusts.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the listener is being bound before the material has been loaded.</exception>
    internal X509Certificate2Collection Chain => this.identity is { } loaded
        ? [.. loaded.Intermediates]
        : throw new InvalidOperationException(
            "The health endpoint's server certificate is being read before it was loaded. Composition loads it before the server starts.");

    /// <summary>Loads the identity the TLS listener presents, or refuses to start the host.</summary>
    /// <param name="cancellationToken">Cancels the material retrieval.</param>
    /// <returns>A task that completes once the identity is usable, or immediately when the configured transport opens no TLS listener.</returns>
    /// <exception cref="OptionsValidationException">Thrown when the configured material produced no usable identity.</exception>
    internal async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (!this.healthEndpointSettings.Enabled || !this.healthEndpointSettings.TerminatesTls)
        {
            return;
        }

        var domain = this.healthEndpointSettings.Domain.Trim();

        var result = await this.certificateLoader.LoadAsync(
            this.healthEndpointSettings.ServerCertificate,
            domain,
            cancellationToken);

        if (result.Certificate is not { } certificate)
        {
            throw new OptionsValidationException(
                HealthEndpointOptions.SectionName,
                typeof(HealthEndpointOptions),
                [
                    $"{HealthEndpointOptions.SectionName}:{nameof(HealthEndpointOptions.ServerCertificate)} — the health endpoint has no usable server certificate [{result.Failure}]. The configured transport serves TLS and never falls back to clear text, so the host does not start.",
                ]);
        }

        this.identity = certificate;
        this.ReportLoaded(certificate.Leaf);
    }

    /// <summary>Records that the listener has an identity, and how long that identity lasts.</summary>
    /// <remarks>The expiry instant is the whole of it. The subject, the serial number, and the thumbprint identify the certificate wherever this log is read or shipped, and an operator renewing it needs to know by when rather than which certificate it was.</remarks>
    private void ReportLoaded(X509Certificate2 leaf)
    {
        var expiration = ServerCertificateExpiry.ExpirationOf(leaf);

        if (ServerCertificateExpiry.IsExpiringSoon(expiration, this.timeProvider.GetUtcNow()))
        {
            this.LogServerCertificateExpiringSoon(expiration);

            return;
        }

        this.LogServerCertificateLoaded(expiration);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.identity?.Dispose();
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The health endpoint presents a server certificate valid until {ServerCertificateExpiration:u}.")]
    private partial void LogServerCertificateLoaded(DateTimeOffset serverCertificateExpiration);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The health endpoint presents a server certificate that expires at {ServerCertificateExpiration:u}. "
            + "Renew it before then: once it expires the host stops starting, because a certificate outside its validity "
            + "period is refused rather than served.")]
    private partial void LogServerCertificateExpiringSoon(DateTimeOffset serverCertificateExpiration);
}
