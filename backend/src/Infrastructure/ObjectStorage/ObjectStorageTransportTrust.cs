// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Secrets.Discovery;
using Microsoft.Extensions.Hosting;

namespace MailFathom.Infrastructure.ObjectStorage;

/// <summary>Decides whether the object-storage endpoint's TLS certificate is trusted, and holds the private authority a deployment supplied for it.</summary>
/// <remarks>
/// <para>
/// A deployment reaching a hosted endpoint configures nothing here: the platform's own trust store answers, under the
/// platform TLS policy, exactly as it does for every other outbound call this process makes. What this exists for is the
/// other shape — an endpoint the operator runs themselves, whose certificate a private authority signed — and it is
/// supported by supplying that authority rather than by accepting an error. There is no setting anywhere that turns
/// validation off, and there will not be one: mail is what travels over this connection.
/// </para>
/// <para>
/// The anchor is loaded once, while the host starts, rather than per handshake. The decision is a synchronous callback
/// inside a pooled TLS handler, so there is nowhere to await a secret from; that makes replacing the anchor a restart
/// rather than a rotation, which is the one place this differs from every other secret here and is why the type says so
/// itself. What the startup load buys instead is a failure that names the configuration key, at the moment the host
/// comes up, rather than a handshake failure per request afterwards.
/// </para>
/// <para>
/// Trust is decided by rebuilding the chain against the anchor, which is
/// <see cref="PrivateAuthorityServerCertificateValidator" />'s single rule rather than a second one written for this
/// protocol: which authority signed a certificate is not a question a mail server and an object store answer
/// differently.
/// </para>
/// </remarks>
internal sealed class ObjectStorageTransportTrust : IHostedService, IDisposable
{
    private readonly ConfiguredSecret? configuredAnchor;
    private readonly TrustAnchorLoader trustAnchorLoader;

    private X509Certificate2? anchor;

    /// <summary>Initializes the trust of the object-storage transport.</summary>
    /// <param name="configuredAnchor">The block referencing the private authority, or <see langword="null" /> for an endpoint the platform's own trust store answers for.</param>
    /// <param name="trustAnchorLoader">Turns the referenced material into a certificate.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="trustAnchorLoader" /> is <see langword="null" />.</exception>
    public ObjectStorageTransportTrust(ConfiguredSecret? configuredAnchor, TrustAnchorLoader trustAnchorLoader)
    {
        ArgumentNullException.ThrowIfNull(trustAnchorLoader);

        this.configuredAnchor = configuredAnchor;
        this.trustAnchorLoader = trustAnchorLoader;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when a configured authority could not be loaded, which fails the host rather than leaving every later handshake to fail one at a time.</exception>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (this.configuredAnchor is null)
        {
            return;
        }

        var loadResult = await this.trustAnchorLoader.LoadAsync(this.configuredAnchor, cancellationToken);

        this.anchor = loadResult.TrustAnchor ?? throw new InvalidOperationException(
            $"The certificate authority ContentStorage:ObjectStorage:TrustAnchor references could not be loaded [{loadResult.Failure}]. Supply the authority that signed the object-storage endpoint's certificate, or remove the setting for an endpoint the platform already trusts.");
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Reports whether the endpoint's certificate is trusted.</summary>
    /// <param name="serverCertificate">The certificate the endpoint presented, or <see langword="null" /> when it presented none.</param>
    /// <param name="platformChain">The chain the platform built, whose intermediates are reused as path-building candidates.</param>
    /// <param name="platformErrors">What the platform's own validation objected to.</param>
    /// <returns><see langword="true" /> when the certificate is trusted; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// With no authority configured this is the platform's own answer and nothing more, which is what keeps the callback
    /// safe to install unconditionally: a deployment that configured no anchor gets exactly the validation it would have
    /// got with no callback at all.
    /// </remarks>
    public bool IsServerCertificateTrusted(
        X509Certificate? serverCertificate,
        X509Chain? platformChain,
        SslPolicyErrors platformErrors) => this.anchor is { } configuredAuthority
        ? PrivateAuthorityServerCertificateValidator.IsServerCertificateTrusted(
            configuredAuthority,
            serverCertificate,
            platformChain,
            platformErrors)
        : platformErrors == SslPolicyErrors.None;

    /// <inheritdoc />
    public void Dispose() => this.anchor?.Dispose();
}
