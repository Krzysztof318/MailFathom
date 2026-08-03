// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Transport;

namespace MailFathom.Host.Hosting.Startup;

/// <summary>Keeps the administrative surface on its own listener, and everything else off it.</summary>
/// <remarks>
/// <para>
/// Routing matches a path, not a socket, so a route mapped for the administrative listener would otherwise answer on
/// every listener the process opened — including the one MCP clients reach. Giving the endpoint a port of its own would
/// then be a setting that changed where it *also* answers rather than where it answers, which is the opposite of the
/// control an operator configured a separate port to get.
/// </para>
/// <para>
/// The refusal runs in both directions, and the second one matters as much as the first: an administrative listener
/// that served the MCP route or the readiness response would be a second way in to surfaces with their own credentials
/// and their own reasons to be reachable from somewhere else.
/// </para>
/// <para>
/// A refused request is answered <c>404</c> rather than <c>403</c>, because the honest answer is that nothing is served
/// there. A refusal that distinguished the two would tell an unauthenticated caller which ports carry an administrative
/// surface.
/// </para>
/// </remarks>
internal static class AdminEndpointIsolation
{
    /// <summary>The one administrative path that does not sit beneath the route prefix, because RFC 9728 places it at the root.</summary>
    private static readonly PathString ProtectedResourceMetadataPath =
        new(ProtectedResourceMetadataAddress.BeneathRoutePrefix(AdminEndpointOptions.RoutePrefix));

    /// <summary>Reports whether the listener a request arrived on is the one that serves its path.</summary>
    /// <param name="localPort">The port the connection was accepted on.</param>
    /// <param name="path">The request path.</param>
    /// <param name="adminListenerPorts">The ports the administrative listener binds.</param>
    /// <returns><see langword="true" /> when the listener serves the path, otherwise <see langword="false" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="adminListenerPorts" /> is <see langword="null" />.</exception>
    internal static bool ListenerServesPath(int localPort, PathString path, IReadOnlySet<int> adminListenerPorts)
    {
        ArgumentNullException.ThrowIfNull(adminListenerPorts);

        return adminListenerPorts.Contains(localPort) == IsAdminPath(path);
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
    /// the administrative listener — where its only reader arrives — and serve it on whichever listener answers the
    /// root, which is the MCP endpoint's. Both halves of that are wrong, and the second is a document about the
    /// administrative surface answering on a port that does not serve it.
    /// </para>
    /// </remarks>
    internal static bool IsAdminPath(PathString path) =>
        path.StartsWithSegments(AdminEndpointOptions.RoutePrefix, StringComparison.OrdinalIgnoreCase)
        || path.Equals(ProtectedResourceMetadataPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>Refuses any request that reached a listener not serving its path.</summary>
    /// <param name="app">The application pipeline.</param>
    /// <param name="adminListenerPorts">The ports the administrative listener binds.</param>
    /// <returns>The pipeline, so composition reads as one sequence.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="app" /> or <paramref name="adminListenerPorts" /> is <see langword="null" />.</exception>
    internal static IApplicationBuilder UseAdminEndpointIsolation(
        this IApplicationBuilder app,
        IReadOnlySet<int> adminListenerPorts)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(adminListenerPorts);

        return app.Use(async (context, next) =>
        {
            if (!ListenerServesPath(context.Connection.LocalPort, context.Request.Path, adminListenerPorts))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;

                return;
            }

            await next(context);
        });
    }
}
