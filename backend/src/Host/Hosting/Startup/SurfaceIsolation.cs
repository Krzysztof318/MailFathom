// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Transport;

namespace MailFathom.Host.Hosting.Startup;

/// <summary>Serves each path only on the listeners of the surface that owns it.</summary>
/// <remarks>
/// <para>
/// Routing matches a path, not a socket, so a route mapped for one surface would otherwise answer on every listener the
/// process opened. Giving a surface a port of its own would then be a setting that changed where it *also* answers
/// rather than where it answers, which is the opposite of the control an operator configured a separate port to get.
/// </para>
/// <para>
/// The decision is taken from the port the connection arrived at, which is a property of the socket the operating
/// system accepted it on and therefore something a caller cannot state, spoof, or forward. A host header or a path
/// prefix would be a claim the request makes about itself; this is what the deployment published.
/// </para>
/// <para>
/// A port may serve more than one surface, which is what lets a single-node deployment publish one socket, and the rule
/// is then the union rather than an exception to it: a path is served when the port serves the surface that owns it.
/// The middleware runs ahead of everything the surfaces add, so a request for a surface this listener does not serve is
/// refused before it reaches CORS, authentication, the client-certificate check, or the rate limiter — none of which
/// may be configured on that listener, and none of which should be reached from it.
/// </para>
/// <para>
/// A refused request is answered <c>404</c> rather than <c>403</c>, because the honest answer is that nothing is served
/// there. A refusal that distinguished the two would tell an unauthenticated caller which ports carry an administrative
/// surface or a probe.
/// </para>
/// </remarks>
internal static class SurfaceIsolation
{
    /// <summary>The one administrative path that does not sit beneath the route prefix, because RFC 9728 places it at the root.</summary>
    private static readonly PathString AdminProtectedResourceMetadataPath =
        new(ProtectedResourceMetadataAddress.BeneathRoutePrefix(AdminEndpointOptions.RoutePrefix));

    /// <summary>The client surface's document, which sits outside its route prefix for the same reason the administrative one does.</summary>
    private static readonly PathString ClientProtectedResourceMetadataPath =
        new(ProtectedResourceMetadataAddress.BeneathRoutePrefix(ClientEndpointOptions.RoutePrefix));

    /// <summary>Reports whether a listener serving these surfaces answers this path.</summary>
    /// <param name="served">The surfaces served on the listener the request arrived at.</param>
    /// <param name="path">The request path.</param>
    /// <returns><see langword="true" /> when the listener serves the path, otherwise <see langword="false" />.</returns>
    /// <remarks>The MCP surface owns everything the other three do not claim, which is what keeps a path added to it later from having to be listed here.</remarks>
    internal static bool ListenerServesPath(ServedSurfaces served, PathString path)
    {
        if (HealthProbe.IsProbePath(path))
        {
            return served.HasFlag(ServedSurfaces.Probes);
        }

        if (IsAdminPath(path))
        {
            return served.HasFlag(ServedSurfaces.Admin);
        }

        if (IsClientPath(path))
        {
            return served.HasFlag(ServedSurfaces.Client);
        }

        return served.HasFlag(ServedSurfaces.Mcp);
    }

    /// <summary>Reports whether a request path is one the administrative surface answers.</summary>
    /// <param name="path">The request path.</param>
    /// <returns><see langword="true" /> when the path is the administrative surface's, otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// Matched by segment rather than by prefix string, so a path such as <c>/apiary</c> is not mistaken for one of
    /// these.
    /// <para>
    /// The protected resource metadata document counts as one of these even though it sits outside the route prefix.
    /// RFC 9728 places it under a well-known segment at the root, so a rule reading the prefix alone would refuse it on
    /// the administrative listener — where its only reader arrives — and serve it wherever the MCP surface answers the
    /// root. Both halves of that are wrong, and the second is a document about the administrative surface answering on
    /// a port that does not serve it.
    /// </para>
    /// </remarks>
    internal static bool IsAdminPath(PathString path) =>
        path.StartsWithSegments(AdminEndpointOptions.RoutePrefix, StringComparison.OrdinalIgnoreCase)
        || path.Equals(AdminProtectedResourceMetadataPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>Reports whether a request path is one the client surface answers.</summary>
    /// <param name="path">The request path.</param>
    /// <returns><see langword="true" /> when the path is the client surface's, otherwise <see langword="false" />.</returns>
    /// <remarks>Matched exactly as the administrative surface's paths are, and for the same two reasons: by segment, so a path such as <c>/api/clients</c> is not mistaken for one of these, and with the protected resource metadata document counted in even though RFC 9728 places it outside the route prefix.</remarks>
    internal static bool IsClientPath(PathString path) =>
        path.StartsWithSegments(ClientEndpointOptions.RoutePrefix, StringComparison.OrdinalIgnoreCase)
        || path.Equals(ClientProtectedResourceMetadataPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>Refuses every request whose listener does not serve its path.</summary>
    /// <param name="app">The application pipeline being composed.</param>
    /// <param name="servedSurfacesByPort">Which surfaces each bound port serves.</param>
    /// <returns>The same application instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static IApplicationBuilder UseSurfaceIsolation(
        this IApplicationBuilder app,
        IReadOnlyDictionary<int, ServedSurfaces> servedSurfacesByPort)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(servedSurfacesByPort);

        return app.Use(async (context, next) =>
        {
            // A port this process did not bind serves nothing, which is the honest answer for a connection that reached
            // the pipeline some other way rather than a reason to fall back to serving everything.
            var served = servedSurfacesByPort.TryGetValue(context.Connection.LocalPort, out var surfaces)
                ? surfaces
                : ServedSurfaces.None;

            if (!ListenerServesPath(served, context.Request.Path))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;

                return;
            }

            await next(context);
        });
    }
}
