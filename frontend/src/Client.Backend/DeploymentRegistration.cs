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
    /// <summary>Registers the transports, the sign-in, and the token store for one deployment.</summary>
    /// <param name="services">The collection to add to.</param>
    /// <param name="options">Which deployment, and as which registered client.</param>
    /// <returns>The same collection, so registration composes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// The address and the timeout are the caller's to state and are stated once, here. Nothing in this assembly has a
    /// default address and nothing composes one from a literal, so a deployment that moves is a change to whatever the
    /// host reads its options from rather than to any code below.
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
        services.AddTransient<AccessTokenHandler>();
        services.TryAddSingleton<ISignInRedirectListenerFactory, LoopbackSignInRedirectListenerFactory>();
        services.AddSingleton<DeploymentSignIn>();

        services
            .AddHttpClient<DeploymentClient>(
                DeploymentHttpClients.Deployment,
                transport =>
                {
                    transport.BaseAddress = options.Address;
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

        return services;
    }
}
