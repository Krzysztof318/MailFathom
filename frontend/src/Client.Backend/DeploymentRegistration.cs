// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MailFathom.Client.Backend;

/// <summary>Puts everything the client needs to reach one deployment into a service collection.</summary>
public static class DeploymentRegistration
{
    /// <summary>Registers the transports, the sign-in, the session, and the address they all follow.</summary>
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
    /// The credential store is registered <em>if nothing already has</em>, with the one that keeps nothing. A head that
    /// keeps a credential — the desktop head, through the operating system's own store for one user — registers its own
    /// before calling this, and that registration is the one that stands. Defaulting to keeping nothing is the safe
    /// direction: a head composed without saying where it would keep a password keeps none.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddMailFathomDeployment(
        this IServiceCollection services,
        DeploymentOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);
        services.TryAddSingleton<IOwnerCredentialStore>(UnkeptOwnerCredentialStore.Instance);
        services.AddSingleton<SignedInOwner>();
        services.AddSingleton<DeploymentAddress>();
        services.AddTransient<OwnerCredentialHandler>();
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

                    // The backstop for an answer that declares no length, so it is the largest any route on this
                    // transport may read rather than the ordinary one. Each route states its own narrower bound where
                    // it reads, which is the half that can say what happened.
                    transport.MaxResponseContentBufferSize = DeploymentExchange.MaxMailBodyBytes;
                })
            .AddHttpMessageHandler<OwnerCredentialHandler>()

            // This transport carries search text in one route's query string. The factory's default loggers record
            // request URIs, so none may sit around the transport and turn that mail metadata into a log entry.
            .RemoveAllLoggers();

        // Neither a base address nor the credential handler, and the second of those is the point: what this transport
        // sends is one candidate credential, set on the request itself. Attaching whoever is already signed in would
        // make a refused attempt indistinguishable from an accepted one and would present a running session to prove
        // somebody else's password.
        services.AddHttpClient(
            DeploymentHttpClients.SignIn,
            transport =>
            {
                transport.Timeout = options.Timeout;
                transport.MaxResponseContentBufferSize = DeploymentExchange.MaxDocumentBytes;
            });

        // Neither a base address nor the credential handler either, for a reason of its own: this transport is aimed at
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
