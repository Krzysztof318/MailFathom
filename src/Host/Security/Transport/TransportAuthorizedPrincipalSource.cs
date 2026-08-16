// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Endpoints;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Security.Transport;

/// <summary>Tells the application layer what admitted the work in hand, from what the transport already established.</summary>
/// <remarks>
/// <para>
/// The one adapter between authentication and authorization, and the only place a <c>ClaimsPrincipal</c> is turned into
/// something the application layer can read. It is registered per scope, which for a served request is that request, so
/// a use case reached twice in one process is answered about the caller that reached it rather than about whichever was
/// last seen.
/// </para>
/// <para>
/// Three answers, matching the three kinds of principal the application layer models:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// A request an authentication scheme validated is a caller, named by what this deployment configured and holding the
/// grant its entry resolved to. The claims were written when the credential was judged, so nothing here re-reads a
/// configuration section and nothing learns which scheme was involved.
/// </description>
/// </item>
/// <item>
/// <description>
/// No request at all is the process's own identity. Work reached outside a request in this process is work no caller
/// asked for — a worker, a scheduled pass, a startup gate — and saying so is what lets a use case that runs without a
/// caller admit it by name instead of meeting a null nobody checked.
/// </description>
/// </item>
/// <item>
/// <description>
/// A capability a route verified is stated onto the scope by that route, through <see cref="Assume" />, and takes
/// precedence over everything above. It is the answer for the attachment download route, which authenticates nobody by
/// design.
/// </description>
/// </item>
/// </list>
/// <para>
/// A request that authenticated nothing is a caller only where the surface it reached configures no credential at all,
/// and that caller holds everything the surface publishes — ADR 0012 decided that posture and the startup report states
/// it, so a use case refusing there would refuse on a deployment whose own record says it grants everything. Where the
/// surface does configure a credential, a request that authenticated nothing is none of the three: the difference
/// matters at the one route reached that way legitimately, where a principal appearing out of the transport instead of
/// out of a verified signature would be a second, weaker way in.
/// </para>
/// </remarks>
internal sealed class TransportAuthorizedPrincipalSource : IAuthorizedPrincipalSource
{
    private readonly IHttpContextAccessor httpContextAccessor;
    private readonly McpEndpointOptions mcpEndpointSettings;
    private readonly AdminEndpointOptions adminEndpointSettings;

    /// <summary>Initializes the adapter over the request being served, if there is one.</summary>
    /// <param name="httpContextAccessor">Reports the request this scope belongs to, or nothing outside one.</param>
    /// <param name="mcpEndpointSettings">The MCP endpoint settings startup was composed from.</param>
    /// <param name="adminEndpointSettings">The administrative endpoint settings startup was composed from.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    /// <remarks>The settings are the startup snapshot the schemes were registered from, which is the same one the startup report states the resolved grant out of; reading a reloaded value here would answer for a posture no scheme was composed against.</remarks>
    public TransportAuthorizedPrincipalSource(
        IHttpContextAccessor httpContextAccessor,
        IOptions<McpEndpointOptions> mcpEndpointSettings,
        IOptions<AdminEndpointOptions> adminEndpointSettings)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        ArgumentNullException.ThrowIfNull(mcpEndpointSettings);
        ArgumentNullException.ThrowIfNull(adminEndpointSettings);

        this.httpContextAccessor = httpContextAccessor;
        this.mcpEndpointSettings = mcpEndpointSettings.Value;
        this.adminEndpointSettings = adminEndpointSettings.Value;
    }

    /// <inheritdoc />
    public AuthorizedPrincipal? Current { get => field ?? this.FromTransport(); private set; }

    /// <summary>States the principal a route established for itself, which nothing about the transport could have told it.</summary>
    /// <param name="principal">What the route verified.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="principal" /> is <see langword="null" />.</exception>
    /// <remarks>Stating one twice in a scope is a route contradicting itself, so the second statement replaces the first rather than being merged with it; nothing in this process states one more than once.</remarks>
    internal void Assume(AuthorizedPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        this.Current = principal;
    }

    private AuthorizedPrincipal? FromTransport()
    {
        if (this.httpContextAccessor.HttpContext is not { } context)
        {
            return AuthorizedPrincipal.Process;
        }

        return TransportCallerIdentity.NameOf(context.User) is { } identity
            ? AuthorizedPrincipal.Caller(identity, TransportGrant.PermissionsCarriedBy(context.User))
            : this.UnnarrowedCallerOn(context.Request.Path);
    }

    /// <summary>Describes the caller a surface admits where it configures no credential for one to be told apart by.</summary>
    /// <remarks>
    /// There is no entry for a grant to hang on, so the grant is the surface's whole half. The administrative surface is
    /// asked first because it is the narrower of the two paths, and a surface that does configure a credential answers
    /// nothing here: a request that reached it without authenticating was admitted by nothing.
    /// </remarks>
    private AuthorizedPrincipal? UnnarrowedCallerOn(PathString path)
    {
        if (TransportSurface.Admin.Serves(path))
        {
            return this.adminEndpointSettings.RequiresAuthentication ? null : WholeSurfaceCaller(TransportSurface.Admin);
        }

        if (TransportSurface.Mcp.Serves(path))
        {
            return this.mcpEndpointSettings.RequiresAuthentication ? null : WholeSurfaceCaller(TransportSurface.Mcp);
        }

        return null;
    }

    private static AuthorizedPrincipal WholeSurfaceCaller(TransportSurface surface) => AuthorizedPrincipal.Caller(
        TransportCallerIdentity.AnonymousCaller,
        MailFathomPermission.PublishedFor(surface.GrantedSurface));
}
