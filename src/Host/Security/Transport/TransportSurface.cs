// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Common.ClientAssertions;
using MailFathom.Host.Api;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.ApiKeys;
using MailFathom.Mcp;

namespace MailFathom.Host.Security.Transport;

/// <summary>One separately protected transport surface, and the names the authentication framework knows it by.</summary>
/// <remarks>
/// <para>
/// A surface is a set of routes with one credential policy in front of it. The MCP endpoint is one, and it is the reason
/// this type exists: everything that decides whether a request is authenticated used to name that endpoint in a
/// constant, so a second surface could not have been protected without a second copy of the same code. Passing the
/// surface instead makes the schemes, the policy, and the keys parameters of a registration rather than properties of
/// the process.
/// </para>
/// <para>
/// The names below are what keeps two surfaces from reaching each other. Each registers its own routing scheme, and each
/// endpoint requires an authorization policy naming only that scheme, so a credential the other surface accepts is never
/// consulted for this one. That isolation is by scheme name rather than by an explicit check, which is what makes it
/// hold for every route on the surface instead of for the routes somebody remembered.
/// </para>
/// <para>
/// The authentication names are internal handles: nothing published, persisted, or configured reads them. A challenge
/// names <see cref="ApiKeyAuthentication.Realm" /> rather than a scheme, and a client is never told which scheme judged
/// it. Every one of them is derived from <see cref="Name" /> rather than stated per surface so that adding one cannot
/// accidentally reuse another's, which would silently merge the two surfaces' policies. The assertion audience is the
/// exception and says why it is one.
/// </para>
/// <para>
/// It is a closed enumeration for the reason <see cref="Hosting.HealthProbe" /> is one: a surface is a decision about
/// what the host serves, not a value a caller constructs. Being a struct, <see langword="default" /> is reachable and is
/// not a surface; it reports itself through <see cref="IsSpecified" /> and refuses to answer for a name.
/// </para>
/// </remarks>
internal readonly record struct TransportSurface
{
    private readonly string? name;
    private readonly string? routePrefix;
    private readonly string? clientAssertionAudience;
    private readonly string[]? furtherRoutePrefixes;

    private TransportSurface(
        string name,
        string routePrefix,
        string clientAssertionAudience,
        string[]? furtherRoutePrefixes = null)
    {
        this.name = name;
        this.routePrefix = routePrefix;
        this.clientAssertionAudience = clientAssertionAudience;
        this.furtherRoutePrefixes = furtherRoutePrefixes;
    }

    /// <summary>Gets the surface serving the MCP protocol.</summary>
    /// <remarks>
    /// It serves the attachment download route as well as the protocol route. The two carry opposite credential
    /// policies — one requires whatever the endpoint configured, the other admits a signed capability and nothing else —
    /// and they are still one surface, because what a surface bounds is the process's own capacity rather than a
    /// caller's authority: both read the same mailbox, hold the same response streams open, and are enabled and
    /// disabled together.
    /// </remarks>
    internal static TransportSurface Mcp { get; } = new(
        "Mcp",
        McpEndpointRoute.Path,
        ClientAssertion.McpAudience,
        [EmailAttachmentDownloadEndpoint.RoutePrefix]);

    /// <summary>Gets the surface serving the administrative API the <c>mfctl</c> command reaches.</summary>
    /// <remarks>Separate from <see cref="Mcp" /> because reading a mailbox and administering the service that reads it are different authorities, and a credential provisioned for one authenticates nothing on the other.</remarks>
    internal static TransportSurface Admin { get; } = new(
        "Admin",
        AdminEndpointOptions.RoutePrefix,
        ClientAssertion.AdminAudience);

    /// <summary>Gets whether this value names a surface rather than the unusable struct default.</summary>
    internal bool IsSpecified => this.name is not null;

    /// <summary>Gets the surface's name, which every scheme and policy name below is composed from.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a surface.</exception>
    internal string Name => this.name
        ?? throw new InvalidOperationException("The value is the default of the struct and names no transport surface.");

    /// <summary>Gets the path every route on this surface is served beneath.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a surface.</exception>
    /// <remarks>
    /// Both prefixes are constants published by the surfaces themselves rather than settings, so this restates neither:
    /// it is where a control that has to recognize a surface from a request alone — the process-wide rate limiter, which
    /// rides on one application-wide limiter and must exclude everything that is not the surface it bounds — reads the
    /// same constant the routes were mapped from.
    /// </remarks>
    internal string RoutePrefix => this.routePrefix
        ?? throw new InvalidOperationException("The value is the default of the struct and names no transport surface.");

    /// <summary>Reports whether a request path is one this surface serves.</summary>
    /// <param name="path">The path the request arrived at.</param>
    /// <returns><see langword="true" /> when the path is beneath one of this surface's prefixes.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a surface.</exception>
    /// <remarks>
    /// A surface may serve routes under more than one prefix, so recognizing one from a request is asking this rather
    /// than comparing against <see cref="RoutePrefix" />. The difference is not cosmetic: the process-wide rate limiter
    /// rides on one application-wide limiter and gives whatever it does not recognize no limiter at all, so a route this
    /// method failed to claim would be served with no concurrency bound rather than with a wrong one.
    /// </remarks>
    internal bool Serves(PathString path) =>
        path.StartsWithSegments(this.RoutePrefix)
        || Array.Exists(
            this.furtherRoutePrefixes ?? [],
            prefix => path.StartsWithSegments(prefix));

    /// <summary>Gets the name this surface's rate-limiting policy is registered under.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a surface.</exception>
    /// <remarks>
    /// A named policy resolves to exactly one limiter, so two surfaces cannot share one and the name is what keeps their
    /// per-caller buckets apart. Unlike the four names above it is not purely internal: the built-in
    /// <c>Microsoft.AspNetCore.RateLimiting</c> metrics tag a rejection with it, which is how an operator reads which
    /// endpoint refused a request.
    /// </remarks>
    internal string RateLimitingPolicyName => $"MailFathom:{this.Name}:RateLimiting";

    /// <summary>Gets the name this surface's request-timeout policy is registered under.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a surface.</exception>
    /// <remarks>
    /// A named policy keeps the two surfaces' ceilings apart for the same reason the rate-limiting name does, and it is
    /// what lets one endpoint carry a ceiling while the other is served without one — a default policy would apply to
    /// every route in the process, including the probes, which must keep answering while an endpoint is refusing.
    /// </remarks>
    internal string RequestTimeoutPolicyName => $"MailFathom:{this.Name}:RequestTimeout";

    /// <summary>Gets the scheme that decides which credential a request presented and forwards it to the handler that judges it.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a surface.</exception>
    internal string RoutingSchemeName => $"MailFathom:{this.Name}:Transport";

    /// <summary>Gets the scheme comparing a presented credential against this surface's configured API keys.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a surface.</exception>
    internal string ApiKeySchemeName => $"MailFathom:{this.Name}:ApiKey";

    /// <summary>Gets the scheme verifying a signed assertion against this surface's configured client public keys.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a surface.</exception>
    internal string ClientAssertionSchemeName => $"MailFathom:{this.Name}:ClientAssertion";

    /// <summary>Gets the audience an assertion presented here must name.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a surface.</exception>
    /// <remarks>
    /// Unlike the scheme names it is not derived from <see cref="Name" />: it is a value a client writes into a
    /// credential, so it is published by <see cref="ClientAssertion" /> where the command that mints one can read the
    /// same constant. Carrying it as a field rather than composing it means a surface added later has to choose an
    /// audience rather than silently receive one, which is what stops two surfaces from ever sharing it.
    /// </remarks>
    internal string ClientAssertionAudience => this.clientAssertionAudience
        ?? throw new InvalidOperationException("The value is the default of the struct and names no transport surface.");

    /// <summary>Gets the name this surface's authorization requirement is registered under.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a surface.</exception>
    internal string AccessPolicyName => $"MailFathom:{this.Name}:Access";

    /// <summary>Names the scheme that validates tokens from one of this surface's configured authorization servers.</summary>
    /// <param name="authorizationServerName">The operator's name for the profile.</param>
    /// <returns>The scheme name.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="authorizationServerName" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a surface.</exception>
    /// <remarks>
    /// The surface's name precedes the server's, so two surfaces configuring one authorization server under the same
    /// operator-chosen name still register two schemes with two key sets. Sharing one would let either surface's
    /// configuration decide what the other trusts.
    /// </remarks>
    internal string OAuthSchemeNameFor(string authorizationServerName)
    {
        ArgumentNullException.ThrowIfNull(authorizationServerName);

        return $"MailFathom:{this.Name}:OAuth:{authorizationServerName}";
    }

    /// <inheritdoc />
    /// <remarks>The name, because that is what a diagnostic about a surface is read by.</remarks>
    public override string ToString() => this.name ?? "(unspecified)";
}
