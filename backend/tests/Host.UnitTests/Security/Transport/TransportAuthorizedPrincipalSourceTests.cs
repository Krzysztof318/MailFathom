// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Claims;
using MailFathom.Application.Access;
using MailFathom.Application.Access.Grants;
using MailFathom.Domain.Access;
using MailFathom.Host.Api;
using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Hosting.Startup;
using MailFathom.Host.Mcp;
using MailFathom.Host.Observability.ClientTelemetry;
using MailFathom.Host.Security.ApiKeys;
using MailFathom.Host.Security.Transport;
using MailFathom.Mcp;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.Transport;

/// <summary>Covers the one adapter that turns what the transport established into what the application layer reads.</summary>
/// <remarks>
/// Three answers are asserted, one per principal kind the application layer models, and so is a request that
/// authenticated nothing, which is decided by what the surface it reached configures: a caller holding what the served
/// user holds where it configures no entry, and none of the three where it configures one. The download route is
/// withheld from the permissive half of that before either surface is asked, because a principal appearing out of the
/// transport there would be a second and weaker way into an attachment than the signature the route verifies. On a
/// mail-serving surface a caller holds its user's grant kept to the mail half and to what its credential names, so an
/// arrangement here states the user's grant beside the credential and resolves it as the pipeline does.
/// </remarks>
public sealed class TransportAuthorizedPrincipalSourceTests
{
    private const string ConfiguredKeyName = "mcp-key";

    private static readonly UserId DefaultAdministrator = UserId.Create(Guid.Parse("0197c0de-0000-7000-8000-00000000a001"));

    [Fact]
    public async Task Current_AnAuthenticatedRequest_ReportsTheCallerAndTheGrantItsEntryResolvedTo()
    {
        // Arrange
        var context = RequestBy(AuthenticatedCallerHolding(MailFathomPermission.MailRead));
        var source = SourceOver(context);

        // Act
        var principal = await ResolvedAsync(source);

        // Assert
        Assert.NotNull(principal);
        Assert.Equal(AuthorizedPrincipalKind.Caller, principal.Kind);
        Assert.Equal(ConfiguredKeyName, principal.Identity);
        Assert.Equal([MailFathomPermission.MailRead], principal.Permissions);
    }

    /// <summary>An entry an operator emptied admits a caller that holds nothing, which is a caller rather than nobody.</summary>
    [Fact]
    public async Task Current_AnAuthenticatedRequestWhoseEntryGrantedNothing_ReportsACallerHoldingNothing()
    {
        // Arrange
        var context = RequestBy(AuthenticatedCallerHolding());
        var source = SourceOver(context);

        // Act
        var principal = await ResolvedAsync(source);

        // Assert
        Assert.Equal(AuthorizedPrincipalKind.Caller, principal?.Kind);
        Assert.Empty(principal?.Permissions ?? new HashSet<MailFathomPermission>());
    }

    /// <summary>
    /// A request nothing authenticated, on a surface that configures a credential, is none of the three kinds: what
    /// reached the surface without presenting what the surface asks for was admitted by nothing. Every surface is
    /// stated, because each reads its own settings and one arm answering for another would go unnoticed.
    /// </summary>
    [Theory]
    [InlineData(McpEndpointRoute.Path, true, false, false)]
    [InlineData(AdminEndpointOptions.RoutePrefix + "/session", false, true, false)]
    [InlineData(ClientEndpointOptions.RoutePrefix + "/session", false, false, true)]
    public void Current_ARequestThatAuthenticatedNothingWhereTheSurfaceConfiguresACredential_ReportsNoPrincipal(
        string path,
        bool mcpConfiguresACredential,
        bool adminConfiguresACredential,
        bool clientConfiguresACredential)
    {
        // Arrange
        var source = SourceOver(
            RequestTo(path),
            mcpConfiguresACredential,
            adminConfiguresACredential,
            clientConfiguresACredential);

        // Act & Assert
        Assert.Null(source.Current);
    }

    /// <summary>
    /// The MCP surface serves the download route beside the protocol route, so the served user's grant would reach it
    /// wherever the deployment configures no MCP credential — a second and weaker way into an attachment than the
    /// signature the route verifies for itself, and one holding on that posture alone. The transport therefore answers
    /// nothing for it under either, and the redeemed ticket the route states for itself remains the only thing that
    /// authorizes the download.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Current_ARequestToTheDownloadRoute_ReportsNoPrincipalWhateverTheMcpSurfaceConfigures(
        bool mcpConfiguresACredential)
    {
        // Arrange
        var source = SourceOver(
            RequestTo(McpAttachmentDownloadEndpoint.RoutePrefix + "/a-capability-somebody-presented"),
            mcpConfiguresACredential);

        // Act & Assert
        Assert.Null(await ResolvedAsync(source));
    }

    /// <summary>
    /// ADR 0012 leaves a surface with no <c>Authentication</c> entry serving its callers, because there is no entry for
    /// a credential to hang on. On a mail-serving surface that caller acts for the served user and holds what that
    /// user's roles grant of the mail half; reporting no principal here would have a use case refuse every call on a
    /// deployment whose own record says it serves them.
    /// </summary>
    [Theory]
    [InlineData(McpEndpointRoute.Path)]
    [InlineData(ClientEndpointOptions.RoutePrefix + "/session")]
    public async Task Current_ARequestOnAMailSurfaceConfiguringNoCredential_ReportsACallerHoldingWhatTheServedUserHolds(
        string path)
    {
        // Arrange
        var source = SourceOver(RequestTo(path));
        var userGrant = ScopedGrant.Of([
            (MailFathomPermission.MailRead, AssignmentScope.User(SyntheticUser.Deployment)),
            (MailFathomPermission.AdminRead, AssignmentScope.Deployment),
        ]);

        // Act
        var principal = await ResolvedAsync(source, userGrant);

        // Assert
        Assert.Equal(AuthorizedPrincipalKind.Caller, principal?.Kind);
        Assert.Equal(TransportCallerIdentity.AnonymousCaller, principal?.Identity);
        Assert.Equal([MailFathomPermission.MailRead], principal?.Permissions);
    }

    /// <summary>An administrative surface authenticating nobody serves its callers as the default administrator, holding what that user's roles grant on the administrative half.</summary>
    [Fact]
    public async Task Current_ARequestOnTheAdministrativeSurfaceConfiguringNoCredential_ActsAsTheDefaultAdministrator()
    {
        // Arrange
        var source = SourceOver(RequestTo(AdminEndpointOptions.RoutePrefix + "/session"), defaultAdministrator: DefaultAdministrator);

        // Act
        var principal = await ResolvedAsync(
            source,
            ScopedGrant.AtDeployment([.. MailFathomPermission.PublishedFor(ProtectedSurface.Administration), MailFathomPermission.MailRead]));

        // Assert
        Assert.NotNull(principal);
        Assert.Equal(AuthorizedPrincipalKind.Caller, principal.Kind);
        Assert.Equal(TransportCallerIdentity.OfAdministrator(DefaultAdministrator, credential: null), principal.Identity);
        Assert.Null(principal.User);
        Assert.Equal(MailFathomPermission.PublishedFor(ProtectedSurface.Administration).ToHashSet(), principal.Permissions);
    }

    /// <summary>Removing the default administrator is final, so a surface authenticating nobody then serves nobody rather than a caller holding the whole surface.</summary>
    [Fact]
    public async Task Current_ARequestOnTheAdministrativeSurfaceAfterTheDefaultAdministratorWasRemoved_ReportsNoPrincipal()
    {
        // Arrange
        var source = SourceOver(RequestTo(AdminEndpointOptions.RoutePrefix + "/session"));

        // Act & Assert
        Assert.Null(await ResolvedAsync(source));
    }

    /// <summary>
    /// An administrator is named by the user and the credential that admitted them, which is what an audit record carries
    /// and what an operator revokes, and holds their roles' administrative grant kept to what the credential names.
    /// </summary>
    [Fact]
    public async Task Current_AnAdministratorsCredential_NamesTheUserAndCredentialAndKeepsTheGrantToWhatItNames()
    {
        // Arrange
        var credential = Guid.Parse("0197c0de-0000-7000-8000-00000000c0de");
        var caller = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ApiKeyAuthentication.ApiKeyNameClaimType, ConfiguredKeyName),
                TransportCallerUser.ClaimFor(SyntheticUser.Another),
                TransportCallerCredential.ClaimFor(credential),
                .. TransportGrant.ClaimsFor([MailFathomPermission.AdminRead, MailFathomPermission.MailRead]),
            ],
            "test",
            ApiKeyAuthentication.ApiKeyNameClaimType,
            ApiKeyAuthentication.RoleClaimType));
        var source = SourceOver(
            RequestBy(caller, AdminEndpointOptions.RoutePrefix + "/session"),
            adminConfiguresACredential: true,
            defaultAdministrator: DefaultAdministrator);

        // Act
        var principal = await ResolvedAsync(
            source,
            ScopedGrant.AtDeployment([MailFathomPermission.AdminRead, MailFathomPermission.AdminCredentialsWrite, MailFathomPermission.MailRead]));

        // Assert
        Assert.NotNull(principal);
        Assert.Equal(TransportCallerIdentity.OfAdministrator(SyntheticUser.Another, credential), principal.Identity);
        Assert.Null(principal.User);
        Assert.Equal([MailFathomPermission.AdminRead], principal.Permissions);
    }

    /// <summary>
    /// What a caller on a mail-serving surface holds is its user's grant kept to what its credential names: a name the
    /// credential lists and the user does not hold grants nothing, a name the user holds and the credential omits is
    /// withheld, and each name kept stays at the scope its assignment gave it.
    /// </summary>
    [Fact]
    public async Task Current_AUsersCredential_HoldsTheUsersGrantKeptToWhatTheCredentialNames()
    {
        // Arrange
        var userScope = AssignmentScope.User(SyntheticUser.Another);
        var source = SourceOver(
            RequestBy(
                AuthenticatedUserHolding(SyntheticUser.Another, MailFathomPermission.MailRead, MailFathomPermission.MailSend),
                McpEndpointRoute.Path),
            mcpConfiguresACredential: true);
        var userGrant = ScopedGrant.Of([
            (MailFathomPermission.MailRead, userScope),
            (MailFathomPermission.MailAsk, userScope),
        ]);

        // Act
        var principal = await ResolvedAsync(source, userGrant);

        // Assert
        Assert.NotNull(principal);
        Assert.Equal([MailFathomPermission.MailRead], principal.Permissions);
        Assert.Equal([userScope], principal.Grant.ScopesOf(MailFathomPermission.MailRead));
    }

    /// <summary>
    /// A credential naming nothing is admitted kept to every name the mail half publishes, so what its caller holds is
    /// exactly its user's grant: a mail name no role carries — which is what a permission a later release publishes is
    /// until somebody writes it into one — is not held, and the session route reports that grant rather than the
    /// credential's own list.
    /// </summary>
    [Fact]
    public async Task Current_ACredentialNamingNothing_HoldsOnlyWhatItsUsersRolesGrantAndTheSessionReportsThat()
    {
        // Arrange
        var source = SourceOver(
            RequestBy(
                AuthenticatedUserHolding(SyntheticUser.Another, [.. MailFathomPermission.PublishedFor(ProtectedSurface.Mail)]),
                ClientEndpointOptions.RoutePrefix + ClientApiEndpoints.SessionRoute),
            clientConfiguresACredential: true);
        var userGrant = ScopedGrant.Of([
            (MailFathomPermission.MailRead, AssignmentScope.User(SyntheticUser.Another)),
            (MailFathomPermission.MailAsk, AssignmentScope.User(SyntheticUser.Another)),
        ]);

        // Act
        var principal = await ResolvedAsync(source, userGrant);
        var session = ClientSessionResponse.For(principal, forwardsTelemetry: false, ClientTelemetryLevel.Info);

        // Assert
        Assert.NotNull(principal);
        Assert.DoesNotContain(MailFathomPermission.MailSend, principal.Permissions);
        Assert.Equal([MailFathomPermission.MailRead.Name, MailFathomPermission.MailAsk.Name], session.Permissions);
    }

    /// <summary>A mail-serving surface reads the mail half of a grant, so a role carrying administrative names grants nothing there by them, whatever the credential lists.</summary>
    [Fact]
    public async Task Current_AUsersCredentialWhoseUserHoldsAdministrativeNames_HoldsOnlyTheMailHalfOnAMailSurface()
    {
        // Arrange
        var source = SourceOver(
            RequestBy(
                AuthenticatedUserHolding(SyntheticUser.Another, MailFathomPermission.MailRead, MailFathomPermission.AdminRead),
                ClientEndpointOptions.RoutePrefix + "/session"),
            clientConfiguresACredential: true);

        // Act
        var principal = await ResolvedAsync(source, ScopedGrant.AtDeployment([MailFathomPermission.MailRead, MailFathomPermission.AdminRead]));

        // Assert
        Assert.Equal([MailFathomPermission.MailRead], principal?.Permissions);
    }

    /// <summary>The grant read is the grant of the user the caller acts for: the credential's own user rather than the one the deployment serves.</summary>
    [Fact]
    public async Task ResolveGrantAsync_AUsersCredential_ReadsTheGrantOfThatUser()
    {
        // Arrange
        var store = StoreHolding(ScopedGrant.None);
        var source = SourceOver(
            RequestBy(AuthenticatedUserHolding(SyntheticUser.Another, MailFathomPermission.MailRead), McpEndpointRoute.Path),
            mcpConfiguresACredential: true);

        // Act
        await source.ResolveGrantAsync(GrantsOver(store), TestContext.Current.CancellationToken);

        // Assert
        await store.Received(1).ReadGrantOfAsync(SyntheticUser.Another, Arg.Any<CancellationToken>());
        await store.DidNotReceive().ReadGrantOfAsync(SyntheticUser.Deployment, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The grant is read once per request and every scope of that request answers from it — the MCP server runs a tool
    /// call in a scope of its own, so a source composed there has to see what the pipeline's source read.
    /// </summary>
    [Fact]
    public async Task Current_ASecondSourceOverARequestWhoseGrantWasRead_HoldsTheGrantTheFirstRead()
    {
        // Arrange
        var request = RequestBy(AuthenticatedUserHolding(SyntheticUser.Another, MailFathomPermission.MailRead), McpEndpointRoute.Path);
        var pipeline = SourceOver(request, mcpConfiguresACredential: true);
        var toolCall = SourceOver(request, mcpConfiguresACredential: true);

        // Act
        await pipeline.ResolveGrantAsync(
            GrantsOver(StoreHolding(ScopedGrant.AtDeployment([MailFathomPermission.MailRead]))),
            TestContext.Current.CancellationToken);
        var principal = toolCall.Current;

        // Assert
        Assert.Equal([MailFathomPermission.MailRead], principal?.Permissions);
    }

    /// <summary>
    /// A request whose user's grant was never read — a branch of the pipeline that bypassed the read — is answered as
    /// one whose user holds nothing, whatever its credential names, so skipping the read refuses rather than admits.
    /// </summary>
    [Fact]
    public void Current_AMailSurfaceRequestWhoseGrantWasNeverRead_ReportsACallerHoldingNothing()
    {
        // Arrange
        var source = SourceOver(
            RequestBy(AuthenticatedUserHolding(SyntheticUser.Another, MailFathomPermission.MailRead), McpEndpointRoute.Path),
            mcpConfiguresACredential: true);

        // Act
        var principal = source.Current;

        // Assert
        Assert.Equal(AuthorizedPrincipalKind.Caller, principal?.Kind);
        Assert.Empty(principal?.Permissions ?? new HashSet<MailFathomPermission>());
    }

    /// <summary>
    /// Where the deployment serves several users and the caller named none, there is no user whose grant could be read.
    /// The read attaches nothing and leaves the principal to raise the refusal the route reports as unattributable.
    /// </summary>
    [Fact]
    public async Task ResolveGrantAsync_NoSoleUserToActFor_ReadsNothingAndLeavesThePrincipalToRefuse()
    {
        // Arrange
        var store = StoreHolding(ScopedGrant.None);
        var source = SourceOver(RequestTo(McpEndpointRoute.Path), deploymentUserResolves: false);

        // Act
        await source.ResolveGrantAsync(GrantsOver(store), TestContext.Current.CancellationToken);

        // Assert
        await store.DidNotReceiveWithAnyArgs().ReadGrantOfAsync(default, TestContext.Current.CancellationToken);
        Assert.Throws<DeploymentUserUnresolvedException>(() => source.Current);
    }

    /// <summary>A request on the administrative surface acts for no user, so no user's grant is read for it.</summary>
    [Fact]
    public async Task ResolveGrantAsync_ARequestOnTheAdministrativeSurface_ReadsNoGrant()
    {
        // Arrange
        var store = StoreHolding(ScopedGrant.None);
        var source = SourceOver(
            RequestBy(AuthenticatedCallerHolding(MailFathomPermission.AdminRead), AdminEndpointOptions.RoutePrefix + "/session"),
            adminConfiguresACredential: true);

        // Act
        await source.ResolveGrantAsync(GrantsOver(store), TestContext.Current.CancellationToken);

        // Assert
        await store.DidNotReceiveWithAnyArgs().ReadGrantOfAsync(default, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A surface that serves one user's mail admits its caller for that user, and the administrative surface does
    /// not. That is the whole of the second axis at the transport boundary: the deployment administrator is admitted
    /// to a deployment rather than to somebody's mailbox, so a caller-scoped read refuses them instead of answering.
    /// </summary>
    [Theory]
    [InlineData(McpEndpointRoute.Path)]
    [InlineData(ClientEndpointOptions.RoutePrefix + "/session")]
    public void Current_AnAuthenticatedRequestOnASurfaceServingOneUsersMail_CarriesThatUser(string path)
    {
        // Arrange
        var source = SourceOver(
            RequestBy(AuthenticatedCallerHolding(MailFathomPermission.MailRead), path),
            mcpConfiguresACredential: true,
            adminConfiguresACredential: true,
            clientConfiguresACredential: true);

        // Act
        var principal = source.Current;

        // Assert
        Assert.NotNull(principal);
        Assert.Equal(AuthorizedPrincipalKind.Caller, principal.Kind);
        Assert.Equal(SyntheticUser.Deployment, principal.User);
    }

    /// <summary>
    /// The same split holds for the surface an operator left open, which is the posture a first run is served under.
    /// A caller admitted by the absence of a credential is still admitted to one user's mail and to no other's.
    /// </summary>
    [Theory]
    [InlineData(McpEndpointRoute.Path)]
    [InlineData(ClientEndpointOptions.RoutePrefix + "/session")]
    public void Current_ARequestOnAMailSurfaceConfiguringNoCredential_CarriesTheUserTheDeploymentServes(string path)
    {
        // Arrange
        var source = SourceOver(RequestTo(path));

        // Act
        var principal = source.Current;

        // Assert
        Assert.NotNull(principal);
        Assert.Equal(AuthorizedPrincipalKind.Caller, principal.Kind);
        Assert.Equal(SyntheticUser.Deployment, principal.User);
    }

    /// <summary>A path neither surface serves is nobody's, so the posture of either endpoint decides nothing about it.</summary>
    [Fact]
    public async Task Current_ARequestToAPathNeitherSurfaceServes_ReportsNoPrincipal()
    {
        // Arrange
        var source = SourceOver(RequestTo("/health"));

        // Act & Assert
        Assert.Null(await ResolvedAsync(source));
    }

    /// <summary>
    /// Work reached outside a request in this process is work no caller asked for, which is exactly what the process
    /// identity names. Saying so is what lets a use case that runs without a caller admit it by name.
    /// </summary>
    [Fact]
    public async Task Current_NoRequestAtAll_ReportsTheProcessIdentity()
    {
        // Arrange
        var source = SourceOver(context: null);

        // Act & Assert
        Assert.Same(AuthorizedPrincipal.Process, await ResolvedAsync(source));
    }

    /// <summary>A route that verified a capability states it for itself, and that statement is what the use case behind it reads.</summary>
    [Fact]
    public void Current_ACapabilityStatedByARoute_ReportsItOverWhateverTheTransportSaid()
    {
        // Arrange
        var source = SourceOver(RequestBy(AuthenticatedCallerHolding(MailFathomPermission.MailRead)));
        var capability = AuthorizedPrincipal.SignedCapability(SyntheticUser.Deployment, "/mcp/attachments/an-object/0");

        // Act
        source.Assume(capability);

        // Assert
        Assert.Same(capability, source.Current);
    }

    /// <summary>A password authenticates one user, so the credential's own user is what the caller acts for rather than the one the deployment was configured with.</summary>
    [Theory]
    [InlineData(McpEndpointRoute.Path)]
    [InlineData(ClientEndpointOptions.RoutePrefix + "/session")]
    public void Current_ARequestAuthenticatedByAUsersCredential_ActsForThatUserRatherThanTheDeploymentsUser(string path)
    {
        // Arrange
        var source = SourceOver(
            RequestBy(AuthenticatedUserHolding(SyntheticUser.Another, MailFathomPermission.MailRead), path),
            mcpConfiguresACredential: true,
            adminConfiguresACredential: true,
            clientConfiguresACredential: true);

        // Act
        var principal = source.Current;

        // Assert
        Assert.NotNull(principal);
        Assert.Equal(AuthorizedPrincipalKind.Caller, principal.Kind);
        Assert.Equal(SyntheticUser.Another, principal.User);
    }

    /// <summary>The user an administrator's credential names is who acts rather than whose mail is read, so the principal acts for no user's mailbox.</summary>
    [Fact]
    public void Current_AUsersCredentialOnASurfaceServingNoOnesMail_ActsForNoUser()
    {
        // Arrange
        var source = SourceOver(
            RequestBy(
                AuthenticatedUserHolding(SyntheticUser.Another, MailFathomPermission.AdminRead),
                AdminEndpointOptions.RoutePrefix + "/session"),
            mcpConfiguresACredential: true,
            adminConfiguresACredential: true,
            clientConfiguresACredential: true);

        // Act
        var principal = source.Current;

        // Assert
        Assert.NotNull(principal);
        Assert.Null(principal.User);
    }

    /// <summary>Composes the adapter over one request, and over endpoints that configure a credential or do not.</summary>
    /// <remarks>Every endpoint defaults to configuring none, which is the posture whose grant the tests above are about; a test that needs the ordinary posture says so.</remarks>
    private static TransportAuthorizedPrincipalSource SourceOver(
        HttpContext? context,
        bool mcpConfiguresACredential = false,
        bool adminConfiguresACredential = false,
        bool clientConfiguresACredential = false,
        bool deploymentUserResolves = true,
        UserId? defaultAdministrator = null)
    {
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns(context);

        var mcpEndpoint = new McpEndpointOptions();
        var adminEndpoint = new AdminEndpointOptions();
        var clientEndpoint = new ClientEndpointOptions();
        if (mcpConfiguresACredential)
        {
            mcpEndpoint.Authentication.Add(new UserFacingAuthenticationOptions());
        }

        if (adminConfiguresACredential)
        {
            adminEndpoint.Authentication.Add(new UserFacingAuthenticationOptions());
        }

        if (clientConfiguresACredential)
        {
            clientEndpoint.Authentication.Add(new UserFacingAuthenticationOptions());
        }

        RecordedDefaultAdministrator recorded = new();
        recorded.Record(defaultAdministrator);

        var deploymentUser = Substitute.For<IDeploymentUserSource>();
        if (deploymentUserResolves)
        {
            deploymentUser.User.Returns(SyntheticUser.Deployment);
        }
        else
        {
            deploymentUser.User.Returns(_ => throw DeploymentUserUnresolvedException.NoSoleUserToActFor());
        }

        return new TransportAuthorizedPrincipalSource(
            httpContextAccessor,
            deploymentUser,
            Options.Create(mcpEndpoint),
            Options.Create(adminEndpoint),
            Options.Create(clientEndpoint),
            recorded);
    }

    /// <summary>Resolves the grant as the pipeline does ahead of every route, then reads the principal.</summary>
    /// <remarks>The user holds the whole mail half unless a test states otherwise.</remarks>
    private static async Task<AuthorizedPrincipal?> ResolvedAsync(
        TransportAuthorizedPrincipalSource source,
        ScopedGrant? userGrant = null)
    {
        await source.ResolveGrantAsync(
            GrantsOver(StoreHolding(userGrant ?? ScopedGrant.AtDeployment(MailFathomPermission.PublishedFor(ProtectedSurface.Mail)))),
            TestContext.Current.CancellationToken);

        return source.Current;
    }

    private static UserGrantResolver GrantsOver(IGrantStore store) => new(store, new UserGrantCache());

    private static IGrantStore StoreHolding(ScopedGrant grant)
    {
        var store = Substitute.For<IGrantStore>();
        store.ReadGrantOfAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>()).Returns(grant);

        return store;
    }

    private static DefaultHttpContext RequestBy(ClaimsPrincipal caller) => new() { User = caller };

    private static DefaultHttpContext RequestBy(ClaimsPrincipal caller, string path) =>
        new() { User = caller, Request = { Path = path } };

    private static DefaultHttpContext RequestTo(string path) => new() { Request = { Path = path } };

    /// <summary>Composes the principal an API key scheme produces, which names the entry and carries the names it keeps.</summary>
    private static ClaimsPrincipal AuthenticatedCallerHolding(params MailFathomPermission[] granted) =>
        new(new ClaimsIdentity(
            [
                new Claim(ApiKeyAuthentication.ApiKeyNameClaimType, ConfiguredKeyName),
                .. TransportGrant.ClaimsFor(granted),
            ],
            "test",
            ApiKeyAuthentication.ApiKeyNameClaimType,
            ApiKeyAuthentication.RoleClaimType));

    /// <summary>Composes the principal the password scheme produces, which names the user the credential belongs to beside the names it keeps.</summary>
    private static ClaimsPrincipal AuthenticatedUserHolding(UserId user, params MailFathomPermission[] granted) =>
        new(new ClaimsIdentity(
            [
                new Claim(ApiKeyAuthentication.ApiKeyNameClaimType, ConfiguredKeyName),
                TransportCallerUser.ClaimFor(user),
                .. TransportGrant.ClaimsFor(granted),
            ],
            "test",
            ApiKeyAuthentication.ApiKeyNameClaimType,
            ApiKeyAuthentication.RoleClaimType));
}
