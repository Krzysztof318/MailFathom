// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Access.Grants;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Mcp;
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
/// A request an authentication scheme validated is a caller, named by what this deployment configured or recorded and
/// holding what <see cref="HeldOnAMailSurface" /> or the administrator's entry says. The credential's claims were
/// written when it was judged, so nothing here learns which scheme was involved.
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
/// <b>On the two mail-serving surfaces a caller holds what its user holds</b>, computed by
/// <see cref="UserGrantResolver" /> from the roles assigned to the user directly and through their groups, narrowed to
/// the mail half and then to whatever the credential admitting it named — its own list, and under
/// <c>PermissionsFromTokenScopes</c> a token's scopes as well, which the scheme already applied to that list. Reading
/// the user's grant is a database read where nothing is remembered, and this type answers synchronously, so
/// <see cref="ResolveGrantAsync" /> reads it once per request ahead of every route and attaches it to the request. A
/// request it was never read for is answered as one whose user holds nothing, so a pipeline that skipped the read
/// refuses rather than admits.
/// </para>
/// <para>
/// A request that authenticated nothing is a caller only where the surface it reached configures no credential at all.
/// On a mail-serving surface that caller acts for the user the deployment serves and holds what that user holds, with
/// no credential to narrow it; on the administrative surface it holds everything that surface publishes, which the
/// startup report states. Where the surface does configure a credential, a request that authenticated nothing is none
/// of the three.
/// </para>
/// <para>
/// A path the attachment download route serves is withheld from that grant on both postures, before either surface is
/// asked. It is served by the MCP surface and authorized by a signed capability alone, so the grant would otherwise
/// admit it out of the transport instead of out of the verified ticket — a second way in, and one holding on the
/// permissive posture only, which is the worst shape a protection can take.
/// </para>
/// <para>
/// Whose mail a caller is acting on is decided here too, and it is decided by the surface rather than by the credential.
/// The MCP and client surfaces serve one person their own mail, so a caller either reaches them acting for the user
/// this deployment serves or is not admitted; the administrative surface serves the deployment rather than a person, so
/// a caller there acts for no user and every user-scoped use case refuses it. That is the whole of the distinction
/// between an ordinary caller and the deployment administrator, and it holds on both postures, because it is the path
/// that decides it rather than what the request carried.
/// </para>
/// <para>
/// One credential answers that question for itself. A credential recorded for a user is that user's own, so a
/// principal carrying <see cref="TransportCallerUser" />'s claim acts for the user the credential named rather than
/// for whoever the deployment serves. Every other credential names nobody, so the surface decides as above and the
/// user comes from the startup gate. The two are read apart rather than merged, because widening a credential that
/// does name a user to the deployment's user would be the one mistake this distinction exists to prevent.
/// </para>
/// </remarks>
internal sealed class TransportAuthorizedPrincipalSource : IAuthorizedPrincipalSource
{
    private readonly IHttpContextAccessor httpContextAccessor;
    private readonly IDeploymentUserSource deploymentUser;
    private readonly UserGrantResolver grants;
    private readonly McpEndpointOptions mcpEndpointSettings;
    private readonly AdminEndpointOptions adminEndpointSettings;
    private readonly ClientEndpointOptions clientEndpointSettings;

    /// <summary>Initializes the adapter over the request being served, if there is one.</summary>
    /// <param name="httpContextAccessor">Reports the request this scope belongs to, or nothing outside one.</param>
    /// <param name="deploymentUser">Names the user a caller on a mail-serving surface is admitted to act for.</param>
    /// <param name="grants">Computes what a user holds.</param>
    /// <param name="mcpEndpointSettings">The MCP endpoint settings startup was composed from.</param>
    /// <param name="adminEndpointSettings">The administrative endpoint settings startup was composed from.</param>
    /// <param name="clientEndpointSettings">The client endpoint settings startup was composed from.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    /// <remarks>The settings are the startup snapshot the schemes were registered from, which is the same one the startup report states the posture out of; reading a reloaded value here would answer for a posture no scheme was composed against.</remarks>
    public TransportAuthorizedPrincipalSource(
        IHttpContextAccessor httpContextAccessor,
        IDeploymentUserSource deploymentUser,
        UserGrantResolver grants,
        IOptions<McpEndpointOptions> mcpEndpointSettings,
        IOptions<AdminEndpointOptions> adminEndpointSettings,
        IOptions<ClientEndpointOptions> clientEndpointSettings)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        ArgumentNullException.ThrowIfNull(deploymentUser);
        ArgumentNullException.ThrowIfNull(grants);
        ArgumentNullException.ThrowIfNull(mcpEndpointSettings);
        ArgumentNullException.ThrowIfNull(adminEndpointSettings);
        ArgumentNullException.ThrowIfNull(clientEndpointSettings);

        this.httpContextAccessor = httpContextAccessor;
        this.deploymentUser = deploymentUser;
        this.grants = grants;
        this.mcpEndpointSettings = mcpEndpointSettings.Value;
        this.adminEndpointSettings = adminEndpointSettings.Value;
        this.clientEndpointSettings = clientEndpointSettings.Value;
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

    /// <summary>Reads what the user the request acts for holds, and attaches it to the request.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A task that completes once the grant is attached, or at once where the request acts for no user.</returns>
    /// <remarks>
    /// Attached to the request rather than kept on this instance, because a request may be served from more than one
    /// scope — the MCP server runs a tool call in a scope of its own — and every scope answers from the request.
    /// Where the user cannot be named — the deployment serves several and the credential named none — nothing is read
    /// and <see cref="Current" /> raises the same refusal it raised before, which the route reports as unattributable.
    /// </remarks>
    internal async Task ResolveGrantAsync(CancellationToken cancellationToken)
    {
        if (this.httpContextAccessor.HttpContext is not { } context)
        {
            return;
        }

        UserId? user;

        try
        {
            user = this.UserActedForBy(context);
        }
        catch (DeploymentUserUnresolvedException)
        {
            return;
        }

        if (user is { } served)
        {
            TransportGrant.Attach(context, await this.grants.ResolveAsync(served, cancellationToken));
        }
    }

    /// <summary>Reports what a caller on a mail-serving surface holds: its user's grant, narrowed to the mail half and to what admitted it.</summary>
    /// <param name="userGrant">What the user holds, or <see langword="null" /> where it was never read for this request.</param>
    /// <param name="narrowing">The names the credential admitting the caller keeps, already narrowed by a token's scopes where the entry says so.</param>
    /// <returns>The grant the caller holds.</returns>
    /// <remarks>
    /// The surface's half is a narrowing of its own: a role may list administrative names beside mail ones, because the
    /// person administering an organization also reads their own mail, and a mail-serving surface reads only the half it
    /// guards. Every narrowing keeps names and leaves the scope each one was assigned at alone.
    /// </remarks>
    internal static ScopedGrant HeldOnAMailSurface(ScopedGrant? userGrant, IEnumerable<MailFathomPermission> narrowing) =>
        (userGrant ?? ScopedGrant.None)
            .NarrowedTo(MailFathomPermission.PublishedFor(ProtectedSurface.Mail))
            .NarrowedTo(narrowing);

    private AuthorizedPrincipal? FromTransport()
    {
        if (this.httpContextAccessor.HttpContext is not { } context)
        {
            return AuthorizedPrincipal.Process;
        }

        var path = context.Request.Path;
        var userGrant = TransportGrant.AttachedTo(context);

        return TransportCallerIdentity.NameOf(context.User) is { } identity
            ? this.AdmittedCallerOn(
                path,
                identity,
                TransportGrant.PermissionsCarriedBy(context.User),
                TransportCallerUser.CarriedBy(context.User),
                userGrant)
            : this.UnnarrowedCallerOn(path, userGrant);
    }

    /// <summary>Names the user a request acts for, where it reached a mail-serving surface under a caller.</summary>
    /// <remarks>It decides by the same rules <see cref="FromTransport" /> answers by, so the grant read for a request is the grant of the user its principal acts for.</remarks>
    private UserId? UserActedForBy(HttpContext context)
    {
        var path = context.Request.Path;

        if (!ServesOneUsersMail(path) || ReachedOnlyUnderACapability(path))
        {
            return null;
        }

        if (TransportCallerIdentity.NameOf(context.User) is not null)
        {
            return TransportCallerUser.CarriedBy(context.User) ?? this.deploymentUser.User;
        }

        return this.RequiresAuthentication(path) ? null : this.deploymentUser.User;
    }

    /// <summary>Describes a caller a scheme validated, acting for the user its credential named or the one the surface it reached serves.</summary>
    /// <remarks>
    /// The two mail-serving surfaces answer one person about their own mail, so a caller admitted there is admitted for
    /// a user: the one their credential named where it named one, and otherwise the one this deployment serves. The
    /// administrative surface answers for the deployment, so a caller admitted there acts for no user and is refused
    /// by every use case scoped to one — which is what makes the deployment administrator a principal rather than a
    /// grant, and why a credential naming a user does not turn one into a user's caller there. An administrator holds
    /// what its configured entry granted, over the whole deployment. A path neither surface serves is neither, and a
    /// caller cannot reach one: the routes this host maps all belong to a surface.
    /// </remarks>
    private AuthorizedPrincipal AdmittedCallerOn(
        PathString path,
        string identity,
        IEnumerable<MailFathomPermission> narrowing,
        UserId? credentialUser,
        ScopedGrant? userGrant) =>
        ServesOneUsersMail(path)
            ? AuthorizedPrincipal.CallerActingFor(
                credentialUser ?? this.deploymentUser.User,
                identity,
                HeldOnAMailSurface(userGrant, narrowing))
            : AuthorizedPrincipal.Caller(identity, narrowing);

    /// <summary>Reports whether a surface answers one person about their own mail rather than answering for the deployment.</summary>
    private static bool ServesOneUsersMail(PathString path) =>
        TransportSurface.Client.Serves(path) || TransportSurface.Mcp.Serves(path);

    /// <summary>Reports whether the surface serving a path admits only a caller that authenticated.</summary>
    /// <remarks>The two prefixed surfaces are asked first because theirs are the narrower paths.</remarks>
    private bool RequiresAuthentication(PathString path)
    {
        if (TransportSurface.Admin.Serves(path))
        {
            return this.adminEndpointSettings.RequiresAuthentication;
        }

        if (TransportSurface.Client.Serves(path))
        {
            return this.clientEndpointSettings.RequiresAuthentication;
        }

        return !TransportSurface.Mcp.Serves(path) || this.mcpEndpointSettings.RequiresAuthentication;
    }

    /// <summary>Describes the caller a surface admits where it configures no credential for one to be told apart by.</summary>
    /// <remarks>
    /// There is no credential for a narrowing to hang on, so the caller holds the surface's whole half of what it would
    /// otherwise be narrowed from. A surface that does configure a credential answers nothing here: a request that
    /// reached it without authenticating was admitted by nothing.
    /// </remarks>
    private AuthorizedPrincipal? UnnarrowedCallerOn(PathString path, ScopedGrant? userGrant)
    {
        if (ReachedOnlyUnderACapability(path) || this.RequiresAuthentication(path))
        {
            return null;
        }

        var surface = TransportSurface.Admin.Serves(path) ? TransportSurface.Admin
            : TransportSurface.Client.Serves(path) ? TransportSurface.Client
            : TransportSurface.Mcp;

        return this.AdmittedCallerOn(
            path,
            TransportCallerIdentity.AnonymousCaller,
            MailFathomPermission.PublishedFor(surface.GrantedSurface),
            credentialUser: null,
            userGrant);
    }

    /// <summary>Reports whether a route admitting a signed capability and nothing else serves the path.</summary>
    /// <remarks>
    /// The MCP surface serves the attachment download route beside the protocol route, so the grant above would reach it
    /// on a deployment configuring no MCP credential and answer a caller holding what the served user holds. That is a
    /// second and weaker way into the route than the signature it verifies for itself, and it would hold on one posture
    /// only. The route states its own principal through <see cref="Assume" /> once the capability is redeemed, so the
    /// transport answers nothing for it on either posture and the ticket remains the only thing that authorizes it.
    /// </remarks>
    private static bool ReachedOnlyUnderACapability(PathString path) =>
        path.StartsWithSegments(McpAttachmentDownloadEndpoint.RoutePrefix);
}
