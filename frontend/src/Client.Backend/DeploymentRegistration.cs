// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Authorization;
using MailFathom.Client.Backend.Authorization.Redirect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MailFathom.Client.Backend;

/// <summary>Puts everything the client needs to reach one deployment into a service collection.</summary>
public static class DeploymentRegistration
{
    /// <summary>Registers the transports, the sign-in, the credential store, and the address they all follow.</summary>
    /// <param name="services">The collection to add to.</param>
    /// <param name="options">What the installation states about reaching a deployment, which is not which one.</param>
    /// <returns>The same collection, so registration composes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// No address is stated here, and none is composed from a literal. Which deployment this client reaches is
    /// <see cref="DeploymentAddress" />'s, decided by whoever is using the application and changeable while it runs, so
    /// the transport's base address is read from it as each transport is created rather than fixed when the host is
    /// composed. A client registered before anybody has chosen is registered pointed at nothing, which is the accurate
    /// state of a first run.
    /// </para>
    /// <para>
    /// The redirect listener is registered <em>if nothing already has</em>, with the loopback implementation the desktop
    /// head uses. A head that catches its redirect differently — the browser one does — registers its own before
    /// calling this, and that registration is the one that stands.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddMailFathomDeployment(
        this IServiceCollection services,
        DeploymentOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);
        services.AddSingleton<AccessTokenStore>();
        services.AddSingleton<DeploymentAddress>();
        services.AddTransient<AccessTokenHandler>();
        services.TryAddSingleton<ISignInRedirectListenerFactory, LoopbackSignInRedirectListenerFactory>();
        services.AddSingleton<DeploymentSignIn>();
        services.AddSingleton<DeploymentClient>();
        services.AddSingleton<DeploymentProbe>();

        services
            .AddHttpClient(
                DeploymentHttpClients.Deployment,
                (provider, transport) =>
                {
                    // Read here rather than captured above, because this delegate runs once per transport created and
                    // the address a person is pointed at can have moved since the host was built. A transport created
                    // before anybody chose carries no base address at all, and a relative route resolved against it
                    // fails loudly rather than reaching somewhere nobody named.
                    transport.BaseAddress = provider.GetRequiredService<DeploymentAddress>().Current;
                    transport.Timeout = options.Timeout;
                    transport.MaxResponseContentBufferSize = DeploymentExchange.MaxDocumentBytes;
                })
            .AddHttpMessageHandler<AccessTokenHandler>();

        // No base address: every address this one is given is absolute and derived from the issuer the deployment
        // named. No token handler either — a bearer token issued for MailFathom has no business being presented to
        // somebody's identity provider, and the proof key is what authenticates this client there.
        services.AddHttpClient(
            DeploymentHttpClients.AuthorizationServer,
            transport =>
            {
                transport.Timeout = options.Timeout;
                transport.MaxResponseContentBufferSize = DeploymentExchange.MaxDocumentBytes;
            });

        // Neither a base address nor a token handler, and the second of those is the point: this transport is aimed at
        // an address somebody has just typed, which is a machine nobody has vouched for yet. Attaching the session's
        // credential to a request whose whole purpose is to find out what answers would hand that credential over to
        // whatever did.
        services.AddHttpClient(
            DeploymentHttpClients.DeploymentProbe,
            transport =>
            {
                transport.Timeout = options.Timeout;
                transport.MaxResponseContentBufferSize = DeploymentExchange.MaxDocumentBytes;
            });

        return services;
    }
}
