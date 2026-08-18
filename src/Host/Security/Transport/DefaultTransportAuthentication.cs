// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace MailFathom.Host.Security.Transport;

/// <summary>The scheme the one authentication middleware runs, and the surface it establishes an identity for.</summary>
/// <remarks>
/// <para>
/// The pipeline carries one <c>UseAuthentication</c>, on the application itself, and it runs whichever scheme is the
/// application's default. That default cannot be a surface's, because the middleware sees every request: a surface's
/// routing scheme in that position would offer an administrative request's credential to the protocol endpoint's
/// handlers, and which surface held the default would be decided by whichever one registered last. This is the scheme
/// that stands there instead, and what it decides per request is which surface — if any — has to be authenticated
/// before the pipeline reaches its rate limiter.
/// </para>
/// <para>
/// Exactly one surface does, and for one reason: the MCP endpoint's per-caller limit partitions on
/// <c>HttpContext.User</c>, so an identity established behind the limiter would leave every authenticated client
/// counted in the surface's shared anonymous bucket. Everything else reaches
/// <see cref="AuthenticateResult.NoResult" /> — the administrative surface deliberately, because its credential is
/// judged by the authorization middleware and its limiter runs ahead of that so key guessing spends capacity, and the
/// anonymous routes because nothing about them asks who is calling.
/// </para>
/// <para>
/// The one thing this middleware does for every request whatever the decision below is publish what the registered
/// schemes answer as request handlers, which is how the MCP protected resource metadata document is served: that
/// document belongs to a scheme rather than to a route, and the middleware offers every request to every such scheme
/// before it authenticates anything.
/// </para>
/// </remarks>
internal static class DefaultTransportAuthentication
{
    /// <summary>The scheme name, which is the application's default and belongs to no surface.</summary>
    /// <remarks>Composed like every surface's name so the whole authentication vocabulary reads as one, and distinguishable from all of them because no surface is named <c>Default</c> — a surface's own schemes are <c>MailFathom:{Surface}:…</c>.</remarks>
    internal const string SchemeName = "MailFathom:Transport:Default";

    /// <summary>Registers the scheme and makes it the application's default.</summary>
    /// <param name="services">The container to add to.</param>
    /// <returns>The container, so composition reads as one sequence.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Called once from the composition root, after every surface has registered, because the default is one decision
    /// about the application rather than a property a surface brings with it. It is registered whenever any surface is
    /// served with authentication configured, including where that surface is the administrative one alone: the
    /// authentication services exist either way, and minimal hosting inserts an authentication middleware of its own —
    /// ahead of forwarded-header processing — unless the pipeline adds one explicitly.
    /// </remarks>
    internal static IServiceCollection AddDefaultTransportAuthentication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services
            .AddAuthentication(SchemeName)
            .AddScheme<AuthenticationSchemeOptions, DefaultTransportAuthenticationHandler>(
                SchemeName,
                displayName: null,
                schemeOptions => schemeOptions.ForwardDefaultSelector = PreAuthenticatingSchemeFor);

        return services;
    }

    /// <summary>Reports which surface's routing scheme authenticates a request before the pipeline reaches its limiter.</summary>
    /// <param name="context">The request the middleware is about to authenticate.</param>
    /// <returns>The MCP surface's routing scheme where the request is for a protected MCP endpoint, and <see langword="null" /> where nothing is to be authenticated here.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// The answer is read off the endpoint routing already selected, which under minimal hosting has run before the
    /// first line of this application's own pipeline. What it looks for is the authorization requirement the MCP route
    /// carries, so the set of protected MCP endpoints is stated once — where the route is mapped — rather than
    /// restated here as a second list of paths that could fall out of step with it.
    /// </para>
    /// <para>
    /// Every other request is answered by the absence of that requirement rather than by being named: the attachment
    /// download admits a signed capability instead of a credential, the probes and both protected resource metadata
    /// documents answer callers that have none, the administrative routes carry their own surface's requirement, and a
    /// request matching no route carries no metadata at all. An MCP endpoint served without authentication carries no
    /// requirement either, which is what makes an unauthenticated deployment reach the same answer without a second
    /// condition.
    /// </para>
    /// </remarks>
    internal static string? PreAuthenticatingSchemeFor(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return RequiresAccessPolicy(context.GetEndpoint(), TransportSurface.Mcp.AccessPolicyName)
            ? TransportSurface.Mcp.RoutingSchemeName
            : null;
    }

    /// <summary>Reports whether the selected endpoint requires one named authorization policy.</summary>
    private static bool RequiresAccessPolicy(Endpoint? endpoint, string accessPolicyName) =>
        endpoint?.Metadata
            .GetOrderedMetadata<IAuthorizeData>()
            .Any(requirement => string.Equals(requirement.Policy, accessPolicyName, StringComparison.Ordinal))
        ?? false;
}
