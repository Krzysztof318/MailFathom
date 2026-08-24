// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Observability;
using MailFathom.Infrastructure.Secrets.Discovery;
using Microsoft.Extensions.DependencyInjection;

namespace MailFathom.Infrastructure.ObjectStorage;

/// <summary>Registers everything a deployment needs to reach its S3-compatible endpoint.</summary>
/// <remarks>
/// <para>
/// Called only where the deployment selected the object-storage backend, which is why nothing here is conditional
/// afterwards: an instance storing content in the database registers none of it, opens no transport, and probes nothing.
/// That is also what keeps the integration harness free of it — the call is the composition root's, not
/// <c>AddInfrastructure</c>'s, so a suite that never selects the backend never has to supply its inputs.
/// </para>
/// <para>
/// The endpoint is composed by the caller because it comes from configuration and this boundary binds none. The
/// credential source is not a parameter at all: the composition root registers its own implementation of
/// <see cref="IObjectStorageCredentialSource" /> as a separate singleton, because references, schemes, and resolution
/// rules stay there and what crosses this boundary is material with a defined lifetime. So this method registers what
/// consumes the credentials and never what produces them.
/// </para>
/// </remarks>
public static class ObjectStorageServiceCollectionExtensions
{
    /// <summary>Registers the object-storage transport, its client, its telemetry, its readiness probe, and the content store that writes through it.</summary>
    /// <param name="services">The service collection the registrations are added to.</param>
    /// <param name="endpoint">Where the endpoint is, which bucket it holds, and how a request to it is addressed and bounded.</param>
    /// <param name="configuredTrustAnchor">The block referencing the private authority that signed the endpoint's certificate, or <see langword="null" /> for one the platform already trusts.</param>
    /// <returns>The same service collection, so registration reads as one expression.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> or <paramref name="endpoint" /> is <see langword="null" />.</exception>
    public static IServiceCollection AddObjectStorage(
        this IServiceCollection services,
        ObjectStorageEndpoint endpoint,
        ConfiguredSecret? configuredTrustAnchor)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(endpoint);

        services.AddSingleton(endpoint);
        services.AddSingleton(provider => new ObjectStorageTransportTrust(
            configuredTrustAnchor,
            provider.GetRequiredService<TrustAnchorLoader>()));

        // The same instance twice: one the transport reads the loaded authority from, and one the host starts so the
        // authority is loaded before the first handshake needs it. Registered after the secret-configuration validator,
        // so an unresolvable reference is reported by the pass that reports every unusable secret at once rather than by
        // this one failing first.
        services.AddHostedService(provider => provider.GetRequiredService<ObjectStorageTransportTrust>());

        services.AddSingleton<ObjectStorageTelemetry>();
        services.AddSingleton<ObjectStorageOperationRunner>();
        services.AddSingleton<IObjectStorageClientFactory, S3ObjectStorageClientFactory>();
        services.AddSingleton<IObjectStorageEndpointProbe, S3ObjectStorageEndpointProbe>();

        // Registered here and nowhere else, which is what makes the presence of this service the deployment's selection
        // of the object backend: the content store asks the container for it and writes to the database when it is absent.
        services.AddSingleton<IEmailContentObjectStore, S3EmailContentObjectStore>();

        AddObjectStorageTransport(services);

        return services;
    }

    /// <summary>Registers the transport every request to the endpoint travels over.</summary>
    /// <remarks>
    /// <para>
    /// No base address is set, because the AWS client composes the whole address itself from the configured endpoint and
    /// the addressing style. Redirects are refused for the reason every credential-bearing client refuses them: a moved
    /// endpoint answering with a redirect would carry a signed request, and on a write the mail body with it, to
    /// whatever host it named.
    /// </para>
    /// <para>
    /// No response buffer ceiling is set, and that is deliberate rather than an omission. The SDK streams an object's
    /// body, so a ceiling here would bound a future content read rather than an error page, and what bounds a payload is
    /// the size limit the store applies before a message is ever written.
    /// </para>
    /// <para>
    /// It carries no resilience handler at all: every call already runs under the
    /// <see cref="Application.Resilience.OutboundDependency.ObjectStorageInvocation" /> pipeline, and the host's service
    /// defaults add the standard handler to every client the factory builds, so keeping both would put three attempts
    /// inside three against an endpoint that is already refusing. The removal takes out what was registered before it,
    /// so it holds only while the host adds the service defaults ahead of this call; the host's composition root does.
    /// </para>
    /// </remarks>
    private static void AddObjectStorageTransport(IServiceCollection services)
    {
        var client = services.AddHttpClient(ObjectStorageEndpoint.TransportName)
            .ConfigurePrimaryHttpMessageHandler(static provider =>
            {
                var endpoint = provider.GetRequiredService<ObjectStorageEndpoint>();
                var trust = provider.GetRequiredService<ObjectStorageTransportTrust>();

                var handler = new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    ConnectTimeout = endpoint.ConnectTimeout,
                };

                // Installed whether or not a private authority was configured. With none, the callback answers exactly
                // what the platform answered, so the connection is validated the way every other outbound call in this
                // process is; with one, the chain is rebuilt against it. Nothing here can return true for a certificate
                // the platform rejected unless that rebuild succeeded.
                handler.SslOptions.RemoteCertificateValidationCallback =
                    (_, certificate, chain, sslPolicyErrors) =>
                        trust.IsServerCertificateTrusted(certificate, chain, sslPolicyErrors);

                return handler;
            })
            .ConfigureHttpClient(static (provider, client) =>
                client.Timeout = provider.GetRequiredService<ObjectStorageEndpoint>().RequestTimeout);

#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is experimental, and is how the standard handler is opted out of.
        client.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
    }
}
