// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Host.Security.Transport;
using MailFathom.IntegrationTests.Orchestration;
using MailFathom.Mcp;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace MailFathom.IntegrationTests.Hosting;

/// <summary>Drives requests through the pipeline a started host actually serves them through.</summary>
/// <remarks>
/// <para>
/// The order of middleware is not a property of the code that adds it. Minimal hosting wraps whatever the application
/// composed — routing runs first, endpoint execution last, and an authentication or authorization middleware of the
/// framework's own is inserted ahead of everything the application added unless the application added that one itself,
/// on the application rather than inside a branch. So the only place the real order exists is a host that has started,
/// which is what <see cref="InProcessComposedHost" /> reaches without opening a socket.
/// </para>
/// <para>
/// The class joins the composed-host collection for the reason every other host-starting class does: what it starts is
/// a whole MailFathom, and this suite runs those after the tests that own the orchestrated database and mailbox
/// exclusively. It reaches neither of them itself — the workers that would are removed before the container is built —
/// so it needs no fixture, only the ordering.
/// </para>
/// <para>
/// What that costs is the defect these tests were written for. Authentication placed in a branch left the framework
/// inserting its own copy in front of <c>UseForwardedHeaders</c>, so every request behind a TLS-terminating proxy was
/// authenticated while its scheme still read <c>http</c> and every access token was refused unread.
/// </para>
/// </remarks>
[Collection(ComposedHostCollectionDefinition.Name)]
public sealed class ComposedPipelineOrderTests
{
    private const int McpPort = 8080;

    private const int AdminPort = 8082;

    private const int HealthPort = 8081;

    private const string McpKeyName = "workstation";

    private const string McpKey = "not-a-real-mcp-key";

    private const string SecondMcpKeyName = "laptop";

    private const string SecondMcpKey = "not-a-real-second-mcp-key";

    private const string AdminKeyName = "operator";

    private const string AdminKey = "not-a-real-admin-key";

    private const string SecondAdminKeyName = "deputy";

    private const string SecondAdminKey = "not-a-real-second-admin-key";

    private const string Issuer = "https://sso.example.test";

    private const string AuthorizationServerName = "workforce";

    private const string McpProtectedResourceMetadataPath = "/.well-known/oauth-protected-resource/mcp";

    private const string AdminProtectedResourceMetadataPath = "/.well-known/oauth-protected-resource/api/admin";

    private const string AdminSessionRoute = "/api/admin/session";

    /// <summary>Both surfaces authenticating, which is the shape where the two must not reach each other.</summary>
    public static TheoryData<bool, bool> AuthenticationCombinations => new()
    {
        { true, true },
        { true, false },
        { false, true },
        { false, false },
    };

    /// <summary>
    /// The regression. A request arrives over clear text from a proxy that terminated TLS, and by the time MCP token
    /// validation sees it the scheme must be the one the client used. With authentication in a branch the framework
    /// inserted its own middleware ahead of <c>UseForwardedHeaders</c>, this event ran against <c>http</c>, and the
    /// refusal that follows is indistinguishable from the challenge an anonymous request receives.
    /// </summary>
    [Fact]
    public async Task Compose_ATokenForwardedByATlsTerminatingProxy_ReachesTokenValidationAsHttps()
    {
        // Arrange
        ForwardedRequestState? observed = null;

        await using var host = await InProcessComposedHost.StartAsync(
            McpServedWithOAuth(),
            TestContext.Current.CancellationToken,
            builder => builder.Services.PostConfigure<JwtBearerOptions>(
                TransportSurface.Mcp.OAuthSchemeNameFor(AuthorizationServerName),
                jwtOptions =>
                {
                    var refuseAClearTextToken = jwtOptions.Events!.OnMessageReceived;

                    jwtOptions.Events.OnMessageReceived = async context =>
                    {
                        await refuseAClearTextToken(context);

                        observed = ForwardedRequestState.Of(context);

                        // Stops the handler before it would retrieve the authorization server's metadata, which is a
                        // network call this test neither needs nor may make. What the assertion is about has already
                        // happened: the production event above has run and recorded its verdict in the state read here.
                        context.NoResult();
                    };
                }));

        // Act
        await host.SendAsync(
            HttpMethods.Post,
            McpEndpointRoute.Path,
            McpPort,
            (ForwardedHeadersDefaults.XForwardedProtoHeaderName, "https"),
            (HeaderNames.Authorization, $"Bearer {ATokenClaiming(Issuer)}"));

        // Assert
        Assert.NotNull(observed);
        Assert.Equal("https", observed.Scheme);
        Assert.True(observed.IsHttps);
        Assert.Equal("http", observed.OriginalProtocol);
        Assert.Equal(string.Empty, observed.ForwardedProtocol);
        Assert.False(observed.RefusedAsClearText);
    }

    /// <summary>
    /// The same request without the forwarded header is the deployment the refusal exists for, and it must still be
    /// refused. Asserting it beside the case above is what keeps the fix from being "stop refusing clear-text tokens".
    /// </summary>
    [Fact]
    public async Task Compose_ATokenOverAnUnforwardedClearTextRequest_IsStillRefusedUnread()
    {
        // Arrange
        ForwardedRequestState? observed = null;

        await using var host = await InProcessComposedHost.StartAsync(
            McpServedWithOAuth(),
            TestContext.Current.CancellationToken,
            builder => builder.Services.PostConfigure<JwtBearerOptions>(
                TransportSurface.Mcp.OAuthSchemeNameFor(AuthorizationServerName),
                jwtOptions =>
                {
                    var refuseAClearTextToken = jwtOptions.Events!.OnMessageReceived;

                    jwtOptions.Events.OnMessageReceived = async context =>
                    {
                        await refuseAClearTextToken(context);

                        observed = ForwardedRequestState.Of(context);

                        context.NoResult();
                    };
                }));

        // Act
        await host.SendAsync(
            HttpMethods.Post,
            McpEndpointRoute.Path,
            McpPort,
            (HeaderNames.Authorization, $"Bearer {ATokenClaiming(Issuer)}"));

        // Assert
        Assert.NotNull(observed);
        Assert.Equal("http", observed.Scheme);
        Assert.False(observed.IsHttps);
        Assert.True(observed.RefusedAsClearText);
    }

    /// <summary>
    /// Whichever surfaces authenticate, an MCP request reaches MCP schemes and nothing else. The isolation is by scheme
    /// name rather than by a check, so the only way to see it is to record which schemes were asked — a credential
    /// compared against the wrong surface's keys is refused exactly as an absent one is.
    /// </summary>
    [Theory]
    [MemberData(nameof(AuthenticationCombinations))]
    public async Task Compose_AnMcpRequest_ReachesNoAdministrativeScheme(bool mcpAuthenticates, bool adminAuthenticates)
    {
        // Arrange
        await using var host = await InProcessComposedHost.StartAsync(
            BothSurfacesServed(mcpAuthenticates, adminAuthenticates),
            TestContext.Current.CancellationToken);

        // Act
        await host.SendAsync(
            HttpMethods.Post,
            McpEndpointRoute.Path,
            McpPort,
            (HeaderNames.Authorization, $"Bearer {McpKey}"));

        // Assert
        Assert.DoesNotContain(
            host.AuthenticatedSchemes.Asked,
            scheme => scheme.StartsWith($"MailFathom:{TransportSurface.Admin.Name}:", StringComparison.Ordinal));
    }

    /// <summary>And the same in the other direction: an administrative credential is never offered to MCP's handlers.</summary>
    [Theory]
    [MemberData(nameof(AuthenticationCombinations))]
    public async Task Compose_AnAdministrativeRequest_ReachesNoMcpScheme(bool mcpAuthenticates, bool adminAuthenticates)
    {
        // Arrange
        await using var host = await InProcessComposedHost.StartAsync(
            BothSurfacesServed(mcpAuthenticates, adminAuthenticates),
            TestContext.Current.CancellationToken);

        // Act
        await host.SendAsync(
            HttpMethods.Get,
            AdminSessionRoute,
            AdminPort,
            (HeaderNames.Authorization, $"Bearer {AdminKey}"));

        // Assert
        Assert.DoesNotContain(
            host.AuthenticatedSchemes.Asked,
            scheme => scheme.StartsWith($"MailFathom:{TransportSurface.Mcp.Name}:", StringComparison.Ordinal));
    }

    /// <summary>
    /// A protected MCP request is authenticated by the one middleware the pipeline runs, through the application's
    /// default scheme forwarding to the MCP surface. That forwarding is what puts an identity in front of the
    /// per-caller limiter, so the recorded chain is the thing to assert rather than the response.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Compose_AProtectedMcpRequest_IsPreAuthenticatedThroughTheApplicationDefault(bool adminAuthenticates)
    {
        // Arrange
        await using var host = await InProcessComposedHost.StartAsync(
            BothSurfacesServed(mcpAuthenticates: true, adminAuthenticates),
            TestContext.Current.CancellationToken);

        // Act
        await host.SendAsync(
            HttpMethods.Post,
            McpEndpointRoute.Path,
            McpPort,
            (HeaderNames.Authorization, $"Bearer {McpKey}"));

        // Assert
        Assert.Equal(DefaultTransportAuthentication.SchemeName, host.AuthenticatedSchemes.Asked[0]);
        Assert.Contains(TransportSurface.Mcp.RoutingSchemeName, host.AuthenticatedSchemes.Asked);
    }

    /// <summary>
    /// An administrative request reaches the same middleware and is deliberately left anonymous by it. Its credential
    /// is judged during authorization, which runs behind the limiter so that key guessing spends capacity — and that
    /// ordering is the whole reason the administrative surface is not pre-authenticated here.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Compose_AnAdministrativeRequest_IsNotPreAuthenticatedByTheApplicationDefault(bool mcpAuthenticates)
    {
        // Arrange
        await using var host = await InProcessComposedHost.StartAsync(
            BothSurfacesServed(mcpAuthenticates, adminAuthenticates: true),
            TestContext.Current.CancellationToken);

        // Act
        await host.SendAsync(
            HttpMethods.Get,
            AdminSessionRoute,
            AdminPort,
            (HeaderNames.Authorization, $"Bearer {AdminKey}"));

        // Assert: the root middleware ran, and what it ran authenticated nobody — the surface's own scheme is reached
        // only afterwards, by authorization.
        var asked = host.AuthenticatedSchemes.Asked;

        Assert.Equal(DefaultTransportAuthentication.SchemeName, asked[0]);
        Assert.Contains(TransportSurface.Admin.RoutingSchemeName, asked);
        Assert.True(
            asked.ToList().IndexOf(TransportSurface.Admin.RoutingSchemeName) > 0,
            "The administrative surface's scheme must be reached by authorization rather than by the root middleware.");
    }

    /// <summary>
    /// The administrative endpoint alone authenticating is the case the root call exists for beyond the MCP surface:
    /// the authentication services are registered, so minimal hosting would insert a middleware of its own ahead of
    /// forwarded-header processing unless the application added one. The scheme that one runs must still authenticate
    /// nobody, which is what leaves the administrative credential to authorization.
    /// </summary>
    [Fact]
    public async Task Compose_AdministrativeAuthenticationAlone_StillRunsTheApplicationDefaultAtTheRoot()
    {
        // Arrange
        await using var host = await InProcessComposedHost.StartAsync(
            BothSurfacesServed(mcpAuthenticates: false, adminAuthenticates: true),
            TestContext.Current.CancellationToken);

        // Act
        await host.SendAsync(HttpMethods.Post, McpEndpointRoute.Path, McpPort);

        // Assert: an MCP request under this shape is authenticated by the root middleware and by nothing else, and what
        // that middleware runs is the application's own scheme rather than the administrative surface's.
        Assert.Equal([DefaultTransportAuthentication.SchemeName], host.AuthenticatedSchemes.Asked);
    }

    /// <summary>A deployment where neither surface authenticates composes no authentication at all, so the pipeline has none to run.</summary>
    [Fact]
    public async Task Compose_NeitherSurfaceAuthenticating_ComposesNoAuthenticationAtAll()
    {
        // Arrange
        await using var host = await InProcessComposedHost.StartAsync(
            BothSurfacesServed(mcpAuthenticates: false, adminAuthenticates: false),
            TestContext.Current.CancellationToken);

        // Act
        var response = await host.SendAsync(HttpMethods.Post, McpEndpointRoute.Path, McpPort);

        // Assert
        Assert.NotEqual(StatusCodes.Status401Unauthorized, response.StatusCode);
        Assert.Empty(host.AuthenticatedSchemes.Asked);
    }

    /// <summary>
    /// Every route that admits a caller holding no credential keeps doing so under the one authentication middleware.
    /// Each of them would be broken in a different way by a default scheme that authenticated the whole application:
    /// the download admits a signed capability instead, the probe answers an orchestrator, and each metadata document
    /// is read by a client that is trying to find out where to authenticate.
    /// </summary>
    [Theory]
    [InlineData("/attachments/not-a-real-capability", McpPort)]
    [InlineData("/alive", HealthPort)]
    [InlineData(McpProtectedResourceMetadataPath, McpPort)]
    [InlineData(AdminProtectedResourceMetadataPath, AdminPort)]
    public async Task Compose_AnAnonymousRoute_IsNeitherChallengedNorGivenATransportIdentity(string path, int localPort)
    {
        // Arrange
        await using var host = await InProcessComposedHost.StartAsync(
            BothSurfacesServedWithOAuth(),
            TestContext.Current.CancellationToken);

        // Act: forwarded as HTTPS, because that is how each of these arrives in the deployment this fix is about, and
        // because the metadata document a scheme publishes is served only for the scheme its resource identifier names.
        var response = await host.SendAsync(
            HttpMethods.Get,
            path,
            localPort,
            (ForwardedHeadersDefaults.XForwardedProtoHeaderName, "https"));

        // Assert
        Assert.NotEqual(StatusCodes.Status401Unauthorized, response.StatusCode);
        Assert.DoesNotContain(TransportSurface.Mcp.RoutingSchemeName, host.AuthenticatedSchemes.Asked);
        Assert.DoesNotContain(TransportSurface.Admin.RoutingSchemeName, host.AuthenticatedSchemes.Asked);
    }

    /// <summary>
    /// The MCP protected resource metadata document belongs to an authentication scheme rather than to a route, so it
    /// is published by the one middleware that runs those schemes and by nothing else. The scheme serves it only where
    /// the request reads as the scheme its resource identifier names, which makes this a second reading of the
    /// regression: behind a TLS-terminating proxy the document is published only because forwarded-header processing
    /// runs ahead of authentication.
    /// </summary>
    [Fact]
    public async Task Compose_TheMcpProtectedResourceMetadataDocument_IsStillPublishedByItsAuthenticationScheme()
    {
        // Arrange
        await using var host = await InProcessComposedHost.StartAsync(
            McpServedWithOAuth(),
            TestContext.Current.CancellationToken);

        // Act
        var response = await host.SendAsync(
            HttpMethods.Get,
            McpProtectedResourceMetadataPath,
            McpPort,
            (ForwardedHeadersDefaults.XForwardedProtoHeaderName, "https"));

        // Assert
        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
    }

    /// <summary>
    /// The administrative document is a mapped route rather than a scheme's, and it is mapped outside the group the
    /// requirement was applied to. Its reader is <c>mfctl login</c> holding nothing, so a default scheme that
    /// authenticated the whole application would have turned the one answer that says where to authorize into a
    /// challenge.
    /// </summary>
    [Fact]
    public async Task Compose_TheAdministrativeProtectedResourceMetadataDocument_IsStillServedToACallerHoldingNothing()
    {
        // Arrange
        await using var host = await InProcessComposedHost.StartAsync(
            BothSurfacesServedWithOAuth(),
            TestContext.Current.CancellationToken);

        // Act
        var response = await host.SendAsync(
            HttpMethods.Get,
            AdminProtectedResourceMetadataPath,
            AdminPort,
            (ForwardedHeadersDefaults.XForwardedProtoHeaderName, "https"));

        // Assert
        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
    }

    /// <summary>
    /// The MCP endpoint's per-caller bucket counts per credential, which is only possible because authentication runs
    /// ahead of the limiter. Two callers each spending the one token the shape allows is what proves the identity was
    /// there when the partition was chosen; sharing a partition, they would have spent one bucket between them.
    /// </summary>
    [Fact]
    public async Task Compose_TwoAuthenticatedMcpCallers_SpendSeparateRateLimitPartitions()
    {
        // Arrange
        await using var host = await InProcessComposedHost.StartAsync(
            [.. BothSurfacesServed(mcpAuthenticates: true, adminAuthenticates: true), .. OneRequestPerCaller],
            TestContext.Current.CancellationToken);

        // Act
        var first = await host.SendAsync(
            HttpMethods.Post,
            McpEndpointRoute.Path,
            McpPort,
            (HeaderNames.Authorization, $"Bearer {McpKey}"));

        var second = await host.SendAsync(
            HttpMethods.Post,
            McpEndpointRoute.Path,
            McpPort,
            (HeaderNames.Authorization, $"Bearer {SecondMcpKey}"));

        var third = await host.SendAsync(
            HttpMethods.Post,
            McpEndpointRoute.Path,
            McpPort,
            (HeaderNames.Authorization, $"Bearer {McpKey}"));

        // Assert
        Assert.NotEqual(StatusCodes.Status429TooManyRequests, first.StatusCode);
        Assert.NotEqual(StatusCodes.Status429TooManyRequests, second.StatusCode);
        Assert.Equal(StatusCodes.Status429TooManyRequests, third.StatusCode);
    }

    /// <summary>
    /// The administrative endpoint's bucket is the surface's rather than a caller's, because its limiter runs ahead of
    /// the authorization that judges the credential. Two operators sharing one bucket is the documented posture and the
    /// reason it exists: unbounded key guessing is what the limit is against, and a guess must cost the sender capacity
    /// before anything compares it.
    /// </summary>
    [Fact]
    public async Task Compose_TwoAuthenticatedAdministrativeCallers_ShareOneRateLimitPartition()
    {
        // Arrange
        await using var host = await InProcessComposedHost.StartAsync(
            [.. BothSurfacesServed(mcpAuthenticates: true, adminAuthenticates: true), .. OneRequestPerCaller],
            TestContext.Current.CancellationToken);

        // Act
        var first = await host.SendAsync(
            HttpMethods.Get,
            AdminSessionRoute,
            AdminPort,
            (HeaderNames.Authorization, $"Bearer {AdminKey}"));

        var second = await host.SendAsync(
            HttpMethods.Get,
            AdminSessionRoute,
            AdminPort,
            (HeaderNames.Authorization, $"Bearer {SecondAdminKey}"));

        // Assert
        Assert.NotEqual(StatusCodes.Status429TooManyRequests, first.StatusCode);
        Assert.Equal(StatusCodes.Status429TooManyRequests, second.StatusCode);
    }

    /// <summary>One token per caller and an hour before the next one, so no replenishment can land between two requests a test sends back to back.</summary>
    private static IReadOnlyList<KeyValuePair<string, string?>> OneRequestPerCaller =>
    [
        new("McpEndpoint:RateLimiting:TokenCapacity", "1"),
        new("McpEndpoint:RateLimiting:TokensPerReplenishmentPeriod", "1"),
        new("McpEndpoint:RateLimiting:ReplenishmentPeriod", "01:00:00"),
        new("AdminEndpoint:RateLimiting:TokenCapacity", "1"),
        new("AdminEndpoint:RateLimiting:TokensPerReplenishmentPeriod", "1"),
        new("AdminEndpoint:RateLimiting:ReplenishmentPeriod", "01:00:00"),
    ];

    /// <summary>Both surfaces served, each authenticating or not, which is the matrix these tests are written across.</summary>
    private static IReadOnlyList<KeyValuePair<string, string?>> BothSurfacesServed(
        bool mcpAuthenticates,
        bool adminAuthenticates) =>
    [
        new("McpEndpoint:Enabled", "true"),
        new("AdminEndpoint:Enabled", "true"),
        new("AdminEndpoint:Port", "8082"),
        .. mcpAuthenticates
            ?
            [
                new("McpEndpoint:Authentication:0:ApiKey:Name", McpKeyName),
                new("McpEndpoint:Authentication:0:ApiKey:SecretReference", $"plaintext:{McpKey}"),
                new("McpEndpoint:Authentication:1:ApiKey:Name", SecondMcpKeyName),
                new KeyValuePair<string, string?>("McpEndpoint:Authentication:1:ApiKey:SecretReference", $"plaintext:{SecondMcpKey}"),
            ]
            : Array.Empty<KeyValuePair<string, string?>>(),
        .. adminAuthenticates
            ?
            [
                new("AdminEndpoint:Authentication:0:ApiKey:Name", AdminKeyName),
                new("AdminEndpoint:Authentication:0:ApiKey:SecretReference", $"plaintext:{AdminKey}"),
                new("AdminEndpoint:Authentication:1:ApiKey:Name", SecondAdminKeyName),
                new KeyValuePair<string, string?>("AdminEndpoint:Authentication:1:ApiKey:SecretReference", $"plaintext:{SecondAdminKey}"),
            ]
            : Array.Empty<KeyValuePair<string, string?>>(),
    ];

    /// <summary>The MCP surface accepting an access token, which is the shape whose validator the regression reads.</summary>
    private static IReadOnlyList<KeyValuePair<string, string?>> McpServedWithOAuth() =>
    [
        new("McpEndpoint:Enabled", "true"),
        .. McpOAuthEntry,
    ];

    /// <summary>Both surfaces accepting an access token, so both protected resource metadata documents are published.</summary>
    private static IReadOnlyList<KeyValuePair<string, string?>> BothSurfacesServedWithOAuth() =>
    [
        new("McpEndpoint:Enabled", "true"),
        new("AdminEndpoint:Enabled", "true"),
        new("AdminEndpoint:Port", "8082"),
        .. McpOAuthEntry,
        new("AdminEndpoint:Authentication:0:OAuth:Resource", "https://mail.example.test/api/admin"),
        new("AdminEndpoint:Authentication:0:OAuth:AuthorizationServers:0:Name", AuthorizationServerName),
        new("AdminEndpoint:Authentication:0:OAuth:AuthorizationServers:0:Issuer", Issuer),
        new("AdminEndpoint:Authentication:0:OAuth:AuthorizationServers:0:AuthorizedSubjects:0", "someone"),
    ];

    /// <summary>The MCP endpoint's one OAuth entry, stated once because two shapes carry it.</summary>
    private static IReadOnlyList<KeyValuePair<string, string?>> McpOAuthEntry =>
    [
        new("McpEndpoint:Authentication:0:OAuth:Resource", "https://mail.example.test/mcp"),
        new("McpEndpoint:Authentication:0:OAuth:AuthorizationServers:0:Name", AuthorizationServerName),
        new("McpEndpoint:Authentication:0:OAuth:AuthorizationServers:0:Issuer", Issuer),
        new("McpEndpoint:Authentication:0:OAuth:AuthorizationServers:0:AuthorizedSubjects:0", "someone"),
    ];

    /// <summary>Composes a credential that selects one authorization server's validator and is never verified by it.</summary>
    /// <remarks>
    /// Which validator judges a token is read off its unverified <c>iss</c>, and that is all this needs to reach: the
    /// event under test runs before any signature is checked, and the test stops the handler before it would go
    /// looking for a key set.
    /// </remarks>
    private static string ATokenClaiming(string issuer) =>
        $"{Base64Url(@"{""alg"":""RS256"",""typ"":""JWT""}")}.{Base64Url($@"{{""iss"":""{issuer}""}}")}.not-a-signature";

    private static string Base64Url(string json) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>What the request looked like at the moment MCP token validation ran.</summary>
    /// <param name="Scheme">The scheme the request read as.</param>
    /// <param name="IsHttps">Whether the request read as encrypted.</param>
    /// <param name="ForwardedProtocol">What remained of the forwarded protocol header.</param>
    /// <param name="OriginalProtocol">The scheme the request arrived under, which forwarded-header processing writes when it changes one.</param>
    /// <param name="RefusedAsClearText">Whether the production event refused the token without reading it.</param>
    private sealed record ForwardedRequestState(
        string Scheme,
        bool IsHttps,
        string ForwardedProtocol,
        string OriginalProtocol,
        bool RefusedAsClearText)
    {
        /// <summary>Reads the state out of the event's own context.</summary>
        internal static ForwardedRequestState Of(MessageReceivedContext context) => new(
            context.Request.Scheme,
            context.Request.IsHttps,
            context.Request.Headers[ForwardedHeadersDefaults.XForwardedProtoHeaderName].ToString(),
            context.Request.Headers[ForwardedHeadersDefaults.XOriginalProtoHeaderName].ToString(),
            context.Result is not null);
    }
}
