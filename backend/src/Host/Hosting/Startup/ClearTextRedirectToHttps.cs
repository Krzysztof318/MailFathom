// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Security.Transport;
using Microsoft.AspNetCore.Http.Extensions;

namespace MailFathom.Host.Hosting.Startup;

/// <summary>Answers a clear-text listener with the address the surface's HTTPS profiles are served at, and with nothing else.</summary>
/// <remarks>
/// <para>
/// A redirect protects the next request, never the one that arrived: a credential sent in clear text is already on the
/// wire, and nothing here recovers it. What this exists for is the operator who has just configured a certificate, whose
/// clients would otherwise meet a refused connection or an unreadable handshake error — an outage, as far as anything
/// looking at it can tell. Telling a client where the endpoint is does not make clear-text access safe, so the listener
/// serves nothing but this.
/// </para>
/// <para>
/// "Nothing but this" is a property of the ordering as much as of the code. Registered ahead of every other middleware
/// and every mapped route, so a request that arrived on a redirect listener is answered before it reaches endpoint
/// isolation, CORS, authentication, the client-certificate check, the rate limiter, or routing. Every path is answered
/// alike — the MCP route, the administrative routes, the protected-resource metadata document, the probes, and an unmapped
/// path are indistinguishable here, because none of them is served on this socket.
/// </para>
/// <para>
/// <c>308</c> rather than <c>301</c> or <c>302</c>, because a client must re-send the request it made. The MCP transport
/// is a <c>POST</c> and the administrative write routes carry bodies; the older codes permit a client to turn either into
/// a <c>GET</c>, which would arrive over TLS as a request nobody made and fail as a routing error rather than as a
/// redirect that worked.
/// </para>
/// </remarks>
internal static class ClearTextRedirectToHttps
{
    /// <summary>The scheme's own port, which is left out of a redirect rather than written as <c>:443</c>.</summary>
    private const int DefaultHttpsPort = 443;

    /// <summary>Builds the address a request should have been sent to.</summary>
    /// <param name="targets">The listeners that redirect, and the domains each publishes.</param>
    /// <param name="localPort">The TCP port the connection was accepted on.</param>
    /// <param name="host">The host the request asked for, whose port is the clear-text one the client wrote and is discarded.</param>
    /// <param name="path">The request path, preserved so the redirect reaches the same resource.</param>
    /// <param name="query">The request query, preserved for the same reason.</param>
    /// <returns>The absolute HTTPS address, or <see langword="null" /> when this listener's surface publishes no such domain.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="targets" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The host is redirected to itself rather than to a configured target, so several domains sharing one listener each
    /// reach their own. A host header naming a domain the surface does not publish resolves to nothing: it is a name the
    /// client chose, and rewriting it to a domain this deployment does publish would answer a request nobody made with an
    /// identity nobody asked for.
    /// </remarks>
    internal static string? ResolveLocation(
        ClearTextRedirectTargets targets,
        int localPort,
        HostString host,
        PathString path,
        QueryString query)
    {
        ArgumentNullException.ThrowIfNull(targets);

        if (string.IsNullOrEmpty(host.Host)
            || targets.PublishedHttpsPortFor(localPort, host.Host) is not { } httpsPort)
        {
            return null;
        }

        // Built through the framework's own composer rather than by concatenation, so the path and query are written back
        // exactly as they were parsed and an IPv6 literal host keeps its brackets.
        return UriHelper.BuildAbsolute(
            Uri.UriSchemeHttps,
            httpsPort == DefaultHttpsPort ? new HostString(host.Host) : new HostString(host.Host, httpsPort),
            path: path,
            query: query);
    }

    /// <summary>Redirects every request that arrived on a clear-text listener, and lets every other request through.</summary>
    /// <param name="app">The application pipeline being composed.</param>
    /// <param name="targets">The listeners that redirect, and the domains each publishes.</param>
    /// <returns>The same application instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// A request whose host names no published domain is answered <c>400</c>. It is the client's own claim about where it
    /// was connecting that cannot be honored, which is a malformed request rather than a missing resource — and unlike a
    /// <c>404</c> it does not invite a caller to read the answer as "try another path on this port".
    /// </remarks>
    internal static IApplicationBuilder UseClearTextRedirectToHttps(
        this IApplicationBuilder app,
        ClearTextRedirectTargets targets)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(targets);

        return app.Use(async (context, next) =>
        {
            if (!targets.RedirectsOnly(context.Connection.LocalPort))
            {
                await next(context);

                return;
            }

            var request = context.Request;
            var location = ResolveLocation(
                targets,
                context.Connection.LocalPort,
                request.Host,
                request.Path,
                request.QueryString);

            if (location is null)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;

                return;
            }

            context.Response.StatusCode = StatusCodes.Status308PermanentRedirect;
            context.Response.Headers.Location = location;
        });
    }
}
