// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Backend;

/// <summary>The client's one way of asking a MailFathom deployment something.</summary>
/// <remarks>
/// <para>
/// A typed client over a transport the composing host configured with the deployment's address and a timeout, so no
/// route here is ever an absolute address and nothing in this assembly decides where the deployment is. The bearer
/// token, when there is one, is attached by the handler in the pipeline rather than by any method below — which is
/// what keeps a route from being written without one by accident.
/// </para>
/// <para>
/// It carries exactly one route today, deliberately. <c>/api/client/session</c> reports what the deployment made of the
/// caller and nothing else, which is what lets sign-in be proven end to end before a screen exists to show anything.
/// </para>
/// </remarks>
public sealed class DeploymentClient
{
    private readonly HttpClient transport;

    /// <summary>Initializes the client over a configured transport.</summary>
    /// <param name="transport">The transport, whose base address and timeout the host stated.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="transport" /> is <see langword="null" />.</exception>
    public DeploymentClient(HttpClient transport)
    {
        ArgumentNullException.ThrowIfNull(transport);

        this.transport = transport;
    }

    /// <summary>Asks the deployment what it makes of the credential this client is presenting.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>What the deployment reports about the caller.</returns>
    /// <exception cref="DeploymentFailure">Thrown when the deployment refused, was unreachable, did not answer in time, or answered with something else.</exception>
    /// <remarks>
    /// A caller holding no token still gets an answer, because the route requires no permission at the other end: it
    /// reports an anonymous caller with an empty grant. That is what makes it usable as a reachability check before
    /// anybody has signed in.
    /// </remarks>
    public Task<DeploymentSession> ReadSessionAsync(CancellationToken cancellationToken = default) =>
        DeploymentExchange.ReadAsync(
            this.transport,
            new HttpRequestMessage(HttpMethod.Get, DeploymentRoutes.SessionPath),
            DeploymentJsonContext.Default.DeploymentSession,
            cancellationToken);
}
