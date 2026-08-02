// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Hosting;

/// <summary>Keeps the probe paths off the application listener and everything else off the probe listener.</summary>
/// <remarks>
/// <para>
/// The decision is taken from the port the connection arrived at, which is a property of the socket the operating
/// system accepted it on and therefore something a caller cannot state, spoof, or forward. A host header or a path
/// prefix would be a claim the request makes about itself; this is what the deployment published.
/// </para>
/// <para>
/// It runs ahead of everything the MCP endpoint adds, so a request for <c>/mcp</c> that arrived on the probe port is
/// refused before it reaches CORS, authentication, the client-certificate check, or the rate limiter — none of which
/// are configured on that listener and none of which should be reached from it. A probe request on the application
/// port is refused just as early, which is what keeps an unauthenticated dependency report off the network MCP clients
/// connect to.
/// </para>
/// <para>
/// The refusal is a bare <c>404</c>, the same answer an unmapped path receives. A response distinguishing "wrong
/// listener" from "no such route" would tell an unauthenticated caller that the probes exist and where to look for
/// them.
/// </para>
/// </remarks>
internal static class HealthEndpointIsolation
{
    /// <summary>Reports whether the listener a request arrived at serves the path it asked for.</summary>
    /// <param name="localPort">The TCP port the connection was accepted on.</param>
    /// <param name="path">The request path.</param>
    /// <param name="probeListenerPorts">The ports the probes are served on.</param>
    /// <returns><see langword="true" /> when this listener serves this path, otherwise <see langword="false" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="probeListenerPorts" /> is <see langword="null" />.</exception>
    /// <remarks>The two directions are one rule: a probe listener serves probe paths and nothing else, and every other listener serves everything else.</remarks>
    internal static bool ListenerServesPath(int localPort, PathString path, IReadOnlySet<int> probeListenerPorts)
    {
        ArgumentNullException.ThrowIfNull(probeListenerPorts);

        return probeListenerPorts.Contains(localPort) == HealthProbe.IsProbePath(path);
    }

    /// <summary>Refuses every request whose listener does not serve its path.</summary>
    /// <param name="app">The application pipeline being composed.</param>
    /// <param name="probeListenerPorts">The ports the probes are served on.</param>
    /// <returns>The same application instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static IApplicationBuilder UseHealthEndpointIsolation(
        this IApplicationBuilder app,
        IReadOnlySet<int> probeListenerPorts)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(probeListenerPorts);

        return app.Use(async (context, next) =>
        {
            if (!ListenerServesPath(context.Connection.LocalPort, context.Request.Path, probeListenerPorts))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;

                return;
            }

            await next(context);
        });
    }
}
