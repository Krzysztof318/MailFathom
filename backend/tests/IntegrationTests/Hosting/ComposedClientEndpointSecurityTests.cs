// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Host.Security.Transport;
using MailFathom.IntegrationTests.Orchestration;
using MailFathom.Mcp;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace MailFathom.IntegrationTests.Hosting;

/// <summary>Proves what a started host serves on the client surface, and what it refuses to serve there.</summary>
/// <remarks>
/// <para>
/// Which credentials authenticate, how a grant is read, and how the isolation predicate matches a path are all unit
/// covered and none of it is repeated here. What only a started host establishes is the part those tests structurally
/// cannot see: that the surface has a listener of its own, that a deployment which enabled no client endpoint serves
/// nothing at its prefix on any port, that the requirement is attached to the route group rather than merely registered
/// in a container, and that each surface's credentials never reach another surface's handlers.
/// </para>
/// <para>
/// The isolation claim is stated in both directions deliberately. Each surface's keys are correct in isolation, and the
/// fault worth catching is a composition in which they are not separate — a scheme name collision resolves cleanly and
/// serves the wrong deployment. Presenting one surface's credential to another is what makes the separation observable
/// from where a caller stands.
/// </para>
/// <para>
/// It joins the composed-host collection for that collection's ordering rather than for its fixture, exactly as
/// <see cref="ComposedPipelineOrderTests" /> does: <see cref="InProcessComposedHost" /> reaches neither the orchestrated
/// database nor the orchestrated mailbox, and a shape per test is what a process the app model configured once cannot
/// be asked for. Nothing here carries <c>[RequiresIntegrationCoverage]</c>, because the classes it exercises belong to
/// <c>Host</c>, which is outside the coverage denominator.
/// </para>
/// </remarks>
[Collection(ComposedHostCollectionDefinition.Name)]
public sealed class ComposedClientEndpointSecurityTests
{
    private const int McpPort = 8080;

    private const int AdminPort = 8082;

    private const int ClientPort = 8084;

    private const int HealthPort = 8081;

    private const string ClientSessionRoute = "/api/client/session";

    private const string ClientAccountsRoute = "/api/client/accounts";

    private const string ClientFoldersRoute = "/api/client/folders";

    private const string AdminSessionRoute = "/api/admin/session";

    private const string ClientProtectedResourceMetadataPath = "/.well-known/oauth-protected-resource/api/client";

    private const string ClientKeyName = "desktop-client";

    private const string ClientKey = "not-a-real-client-key";

    private const string NarrowedClientKeyName = "narrowed-client";

    private const string NarrowedClientKey = "not-a-real-narrowed-client-key";

    private const string McpKeyName = "workstation";

    private const string McpKey = "not-a-real-mcp-key";

    private const string AdminKeyName = "operator";

    private const string AdminKey = "not-a-real-admin-key";

    private const string PageOrigin = "https://client.example.test";

    private const string AuthorizationServerName = "workforce";

    /// <summary>The ports a client path could arrive on when the surface is not served, which is every listener the shape opens.</summary>
    public static TheoryData<int> PortsOfTheOtherSurfaces => [McpPort, AdminPort];

    /// <summary>
    /// The default every deployment upgrades into. Nothing answers at the client prefix, on any listener the process
    /// opened, so an upgrade opens no new door onto a mailbox and a deployment has to state that it wants one.
    /// </summary>
    [Theory]
    [MemberData(nameof(PortsOfTheOtherSurfaces))]
    public async Task ClientEndpoint_ADeploymentThatDidNotEnableIt_ServesNothingAtItsPrefix(int localPort)
    {
        // Arrange
        await using var host = await InProcessComposedHost.StartAsync(
            OtherSurfacesServed(),
            TestContext.Current.CancellationToken);

        // Act
        var response = await host.SendAsync(HttpMethods.Get, ClientSessionRoute, localPort);

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
    }

    /// <summary>A surface that is served answers only on the listener that serves it, which is what keeps a mailbox off an operator's port.</summary>
    [Theory]
    [MemberData(nameof(PortsOfTheOtherSurfaces))]
    public async Task ClientEndpoint_ServedBesideTheOthers_AnswersOnItsOwnListenerAndNoOther(int localPort)
    {
        // Arrange
        await using var host = await InProcessComposedHost.StartAsync(
            EverySurfaceServed(),
            TestContext.Current.CancellationToken);

        // Act
        var refused = await host.SendAsync(
            HttpMethods.Get,
            ClientSessionRoute,
            localPort,
            (HeaderNames.Authorization, $"Bearer {ClientKey}"));

        var served = await host.SendAsync(
            HttpMethods.Get,
            ClientSessionRoute,
            ClientPort,
            (HeaderNames.Authorization, $"Bearer {ClientKey}"));

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, refused.StatusCode);
        Assert.Equal(StatusCodes.Status200OK, served.StatusCode);
    }

    [Fact]
    public async Task ClientEndpoint_ARequestCarryingNoCredential_IsRefusedBeforeTheSessionHandlerAnswers()
    {
        // Arrange
        await using var host = await InProcessComposedHost.StartAsync(
            EverySurfaceServed(),
            TestContext.Current.CancellationToken);

        // Act
        var response = await host.SendAsync(HttpMethods.Get, ClientSessionRoute, ClientPort);

        // Assert
        Assert.Equal(StatusCodes.Status401Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ClientEndpoint_ARequestCarryingTheConfiguredKey_ReachesTheSessionHandler()
    {
        // Arrange
        await using var host = await InProcessComposedHost.StartAsync(
            EverySurfaceServed(),
            TestContext.Current.CancellationToken);

        // Act
        var response = await host.SendAsync(
            HttpMethods.Get,
            ClientSessionRoute,
            ClientPort,
            (HeaderNames.Authorization, $"Bearer {ClientKey}"));

        // Assert
        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
    }

    /// <summary>
    /// The mail-reading routes follow their surface exactly as the session route does, and this is the one that returns
    /// something about a mailbox — so a listener an operator opened for agents or for administering the service must not
    /// answer it at all.
    /// </summary>
    [Theory]
    [MemberData(nameof(PortsOfTheOtherSurfaces))]
    public async Task ClientAccountsRoute_ServedBesideTheOthers_AnswersOnItsOwnListenerAndNoOther(int localPort)
    {
        // Arrange
        await using var host = await InProcessComposedHost.StartAsync(
            EverySurfaceServed(),
            TestContext.Current.CancellationToken);

        // Act
        var elsewhere = await host.SendAsync(
            HttpMethods.Get,
            ClientAccountsRoute,
            localPort,
            (HeaderNames.Authorization, $"Bearer {NarrowedClientKey}"));

        var served = await host.SendAsync(
            HttpMethods.Get,
            ClientAccountsRoute,
            ClientPort,
            (HeaderNames.Authorization, $"Bearer {NarrowedClientKey}"));

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, elsewhere.StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, served.StatusCode);
    }

    /// <summary>
    /// The route is served on its own listener and is gated there, which the pair above reads as a refusal rather than
    /// as an absence. The credential is one an operator narrowed to the answering grant alone: it authenticates, it
    /// reaches the session route, and it is refused this one — which is a different answer from the empty collection an
    /// owner with no account receives.
    /// </summary>
    [Fact]
    public async Task ClientAccountsRoute_ACredentialWithoutTheMailboxGrant_IsRefusedRatherThanServedAnEmptyAnswer()
    {
        // Arrange
        await using var host = await InProcessComposedHost.StartAsync(
            EverySurfaceServed(),
            TestContext.Current.CancellationToken);

        // Act
        var session = await host.SendAsync(
            HttpMethods.Get,
            ClientSessionRoute,
            ClientPort,
            (HeaderNames.Authorization, $"Bearer {NarrowedClientKey}"));

        var accounts = await host.SendAsync(
            HttpMethods.Get,
            ClientAccountsRoute,
            ClientPort,
            (HeaderNames.Authorization, $"Bearer {NarrowedClientKey}"));

        // Assert
        Assert.Equal(StatusCodes.Status200OK, session.StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, accounts.StatusCode);
    }

    /// <summary>
    /// The folder tree is the read every other one on this surface takes its scope from, so it follows the surface the
    /// same way: absent on the listeners an operator opened for agents or for administering the service, and gated on
    /// its own.
    /// </summary>
    [Theory]
    [MemberData(nameof(PortsOfTheOtherSurfaces))]
    public async Task ClientFoldersRoute_ServedBesideTheOthers_AnswersOnItsOwnListenerAndNoOther(int localPort)
    {
        // Arrange
        await using var host = await InProcessComposedHost.StartAsync(
            EverySurfaceServed(),
            TestContext.Current.CancellationToken);

        // Act
        var elsewhere = await host.SendAsync(
            HttpMethods.Get,
            ClientFoldersRoute,
            localPort,
            (HeaderNames.Authorization, $"Bearer {NarrowedClientKey}"));

        var served = await host.SendAsync(
            HttpMethods.Get,
            ClientFoldersRoute,
            ClientPort,
            (HeaderNames.Authorization, $"Bearer {NarrowedClientKey}"));

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, elsewhere.StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, served.StatusCode);
    }

    /// <summary>
    /// Naming an owner's folders is the same disclosure as naming their mailboxes, so the credential narrowed to the
    /// answering grant alone is refused the tree exactly as it is refused the mailbox list.
    /// </summary>
    [Fact]
    public async Task ClientFoldersRoute_ACredentialWithoutTheMailboxGrant_IsRefusedRatherThanServedAnEmptyTree()
    {
        // Arrange
        await using var host = await InProcessComposedHost.StartAsync(
            EverySurfaceServed(),
            TestContext.Current.CancellationToken);

        // Act
        var session = await host.SendAsync(
            HttpMethods.Get,
            ClientSessionRoute,
            ClientPort,
            (HeaderNames.Authorization, $"Bearer {NarrowedClientKey}"));

        var folders = await host.SendAsync(
            HttpMethods.Get,
            ClientFoldersRoute,
            ClientPort,
            (HeaderNames.Authorization, $"Bearer {NarrowedClientKey}"));

        // Assert
        Assert.Equal(StatusCodes.Status200OK, session.StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, folders.StatusCode);
    }

    /// <summary>
    /// The claim this class exists for, in the direction a mistake would be worst: a key an operator provisioned for an
    /// agent or for administering the service must buy nothing on the surface that serves a person's mail.
    /// </summary>
    [Theory]
    [InlineData(McpKey)]
    [InlineData(AdminKey)]
    public async Task ClientEndpoint_PresentedWithAnotherSurfacesConfiguredKey_RefusesItLikeAnyUnrecognizedCredential(
        string credential)
    {
        // Arrange
        await using var host = await InProcessComposedHost.StartAsync(
            EverySurfaceServed(),
            TestContext.Current.CancellationToken);

        // Act
        var response = await host.SendAsync(
            HttpMethods.Get,
            ClientSessionRoute,
            ClientPort,
            (HeaderNames.Authorization, $"Bearer {credential}"));

        // Assert
        Assert.Equal(StatusCodes.Status401Unauthorized, response.StatusCode);
    }

    /// <summary>And the other direction: the credential a person signs their mail client in with administers nothing and drives no agent.</summary>
    [Theory]
    [InlineData("GET", AdminSessionRoute, AdminPort)]
    [InlineData("POST", McpEndpointRoute.Path, McpPort)]
    public async Task AnotherSurface_PresentedWithTheClientsConfiguredKey_RefusesItLikeAnyUnrecognizedCredential(
        string method,
        string route,
        int localPort)
    {
        // Arrange
        await using var host = await InProcessComposedHost.StartAsync(
            EverySurfaceServed(),
            TestContext.Current.CancellationToken);

        // Act
        var response = await host.SendAsync(
            method,
            route,
            localPort,
            (HeaderNames.Authorization, $"Bearer {ClientKey}"));

        // Assert
        Assert.Equal(StatusCodes.Status401Unauthorized, response.StatusCode);
    }

    /// <summary>A credential is never even offered to another surface's handlers, which is the separation read from inside the pipeline rather than from the answer.</summary>
    [Fact]
    public async Task ClientEndpoint_ARequestOnItsOwnListener_ReachesNoOtherSurfacesScheme()
    {
        // Arrange
        await using var host = await InProcessComposedHost.StartAsync(
            EverySurfaceServed(),
            TestContext.Current.CancellationToken);

        // Act
        await host.SendAsync(
            HttpMethods.Get,
            ClientSessionRoute,
            ClientPort,
            (HeaderNames.Authorization, $"Bearer {ClientKey}"));

        // Assert
        Assert.DoesNotContain(host.AuthenticatedSchemes.Asked, IsMcpScheme);
        Assert.DoesNotContain(host.AuthenticatedSchemes.Asked, IsAdminScheme);
    }

    [Theory]
    [InlineData("GET", AdminSessionRoute, AdminPort)]
    [InlineData("POST", McpEndpointRoute.Path, McpPort)]
    public async Task AnotherSurfacesRequest_ReachesNoClientScheme(string method, string route, int localPort)
    {
        // Arrange
        await using var host = await InProcessComposedHost.StartAsync(
            EverySurfaceServed(),
            TestContext.Current.CancellationToken);

        // Act
        await host.SendAsync(
            method,
            route,
            localPort,
            (HeaderNames.Authorization, $"Bearer {AdminKey}"));

        // Assert
        Assert.DoesNotContain(host.AuthenticatedSchemes.Asked, IsClientScheme);
    }

    /// <summary>
    /// The document a page reads before it has authenticated anything, which is the whole point of publishing one: its
    /// reader holds nothing yet and is trying to find out where to go and get something.
    /// </summary>
    [Fact]
    public async Task ClientEndpoint_TheProtectedResourceMetadataDocument_IsServedToACallerHoldingNothing()
    {
        // Arrange
        await using var host = await InProcessComposedHost.StartAsync(
            ClientServedWithOAuth(),
            TestContext.Current.CancellationToken);

        // Act
        var response = await host.SendAsync(
            HttpMethods.Get,
            ClientProtectedResourceMetadataPath,
            ClientPort,
            (ForwardedHeadersDefaults.XForwardedProtoHeaderName, "https"));

        // Assert
        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
    }

    /// <summary>The document follows its surface, so a listener that serves no client endpoint does not publish it either.</summary>
    [Fact]
    public async Task ClientEndpoint_TheProtectedResourceMetadataDocument_IsNotServedOnAnotherSurfacesListener()
    {
        // Arrange
        await using var host = await InProcessComposedHost.StartAsync(
            ClientServedWithOAuth(),
            TestContext.Current.CancellationToken);

        // Act
        var response = await host.SendAsync(
            HttpMethods.Get,
            ClientProtectedResourceMetadataPath,
            McpPort,
            (ForwardedHeadersDefaults.XForwardedProtoHeaderName, "https"));

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
    }

    /// <summary>
    /// A preflight this endpoint cannot answer is a WebAssembly head that never starts, and the policy is attached to
    /// the endpoint rather than applied as a default — so nothing but a started host can say whether the browser gets an
    /// answer.
    /// </summary>
    [Fact]
    public async Task ClientEndpoint_APreflightFromTheConfiguredOrigin_IsAnswered()
    {
        // Arrange
        await using var host = await InProcessComposedHost.StartAsync(
            EverySurfaceServed(),
            TestContext.Current.CancellationToken);

        // Act
        var response = await host.SendAsync(
            HttpMethods.Options,
            ClientSessionRoute,
            ClientPort,
            (HeaderNames.Origin, PageOrigin),
            (HeaderNames.AccessControlRequestMethod, HttpMethods.Get),
            (HeaderNames.AccessControlRequestHeaders, HeaderNames.Authorization));

        // Assert
        Assert.Equal(StatusCodes.Status204NoContent, response.StatusCode);
        Assert.Equal(PageOrigin, response.Headers[HeaderNames.AccessControlAllowOrigin].ToString());
    }

    /// <summary>
    /// The local Aspire topology leaves the origin list unstated, which is every origin. A tab opened as
    /// <c>localhost</c> is a different origin from one opened as <c>127.0.0.1</c>, and a host that named only one of
    /// them answered the other's preflight without <c>Access-Control-Allow-Origin</c> — which a browser reads as no
    /// mail rather than as a refused origin.
    /// </summary>
    [Fact]
    public async Task ClientEndpoint_AnUnstatedOriginList_AnswersAPreflightFromLocalhost()
    {
        // Arrange
        await using var host = await InProcessComposedHost.StartAsync(
            ClientServedWithoutANamedOrigin(),
            TestContext.Current.CancellationToken);

        // Act
        var response = await host.SendAsync(
            HttpMethods.Options,
            ClientSessionRoute,
            ClientPort,
            (HeaderNames.Origin, "http://localhost:5000"),
            (HeaderNames.AccessControlRequestMethod, HttpMethods.Get),
            (HeaderNames.AccessControlRequestHeaders, HeaderNames.Authorization));

        // Assert
        Assert.Equal(StatusCodes.Status204NoContent, response.StatusCode);
        Assert.Equal("*", response.Headers[HeaderNames.AccessControlAllowOrigin].ToString());
    }

    /// <summary>The health probes answer whatever the surfaces do, because the limits and the ceilings are attached to routes rather than applied as default policies.</summary>
    [Fact]
    public async Task HealthProbes_BesideAServedClientSurface_KeepAnswering()
    {
        // Arrange
        await using var host = await InProcessComposedHost.StartAsync(
            EverySurfaceServed(),
            TestContext.Current.CancellationToken);

        // Act
        var response = await host.SendAsync(HttpMethods.Get, "/alive", HealthPort);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
    }

    private static bool IsMcpScheme(string schemeName) =>
        schemeName.StartsWith($"MailFathom:{TransportSurface.Mcp.Name}:", StringComparison.Ordinal);

    private static bool IsAdminScheme(string schemeName) =>
        schemeName.StartsWith($"MailFathom:{TransportSurface.Admin.Name}:", StringComparison.Ordinal);

    private static bool IsClientScheme(string schemeName) =>
        schemeName.StartsWith($"MailFathom:{TransportSurface.Client.Name}:", StringComparison.Ordinal);

    /// <summary>The two surfaces that existed before this one, each authenticating, and no client endpoint at all.</summary>
    private static IReadOnlyList<KeyValuePair<string, string?>> OtherSurfacesServed() =>
    [
        new("McpEndpoint:Enabled", "true"),
        new("McpEndpoint:Authentication:0:ApiKey:Name", McpKeyName),
        new("McpEndpoint:Authentication:0:ApiKey:SecretReference", $"plaintext:{McpKey}"),
        new("AdminEndpoint:Enabled", "true"),
        new("AdminEndpoint:Port", AdminPort.ToString(CultureInfo.InvariantCulture)),
        new("AdminEndpoint:Authentication:0:ApiKey:Name", AdminKeyName),
        new("AdminEndpoint:Authentication:0:ApiKey:SecretReference", $"plaintext:{AdminKey}"),
    ];

    /// <summary>
    /// All three surfaces served, each with a key of its own, which is the shape the isolation claims are read across.
    /// The client surface carries a second key an operator narrowed to the answering grant alone, which is how a route
    /// that is gated is told from a route that is absent.
    /// </summary>
    private static IReadOnlyList<KeyValuePair<string, string?>> EverySurfaceServed() =>
    [
        .. OtherSurfacesServed(),
        new("ClientEndpoint:Enabled", "true"),
        new("ClientEndpoint:Port", ClientPort.ToString(CultureInfo.InvariantCulture)),
        new("ClientEndpoint:Cors:AllowedOrigins:0", PageOrigin),
        new("ClientEndpoint:Authentication:0:ApiKey:Name", ClientKeyName),
        new("ClientEndpoint:Authentication:0:ApiKey:SecretReference", $"plaintext:{ClientKey}"),
        new("ClientEndpoint:Authentication:1:ApiKey:Name", NarrowedClientKeyName),
        new("ClientEndpoint:Authentication:1:ApiKey:SecretReference", $"plaintext:{NarrowedClientKey}"),
        new("ClientEndpoint:Authentication:1:Permissions:0", "mailfathom.mail.ask"),
    ];

    /// <summary>The local Aspire shape: the client surface is on, authenticated, and CORS is left at every origin.</summary>
    private static IReadOnlyList<KeyValuePair<string, string?>> ClientServedWithoutANamedOrigin() =>
    [
        .. OtherSurfacesServed(),
        new("ClientEndpoint:Enabled", "true"),
        new("ClientEndpoint:Port", ClientPort.ToString(CultureInfo.InvariantCulture)),
        new("ClientEndpoint:Authentication:0:ApiKey:Name", ClientKeyName),
        new("ClientEndpoint:Authentication:0:ApiKey:SecretReference", $"plaintext:{ClientKey}"),
    ];

    /// <summary>The client surface accepting an access token, which is the shape that publishes its metadata document.</summary>
    private static IReadOnlyList<KeyValuePair<string, string?>> ClientServedWithOAuth() =>
    [
        new("McpEndpoint:Enabled", "true"),
        new("ClientEndpoint:Enabled", "true"),
        new("ClientEndpoint:Port", ClientPort.ToString(CultureInfo.InvariantCulture)),
        new("ClientEndpoint:Authentication:0:OAuth:Resource", "https://mail.example.test/api/client"),
        new("ClientEndpoint:Authentication:0:OAuth:AuthorizationServers:0:Name", AuthorizationServerName),
        new("ClientEndpoint:Authentication:0:OAuth:AuthorizationServers:0:Issuer", "https://sso.example.test"),
        new("ClientEndpoint:Authentication:0:OAuth:AuthorizationServers:0:AuthorizedSubjects:0", "someone"),
    ];
}
