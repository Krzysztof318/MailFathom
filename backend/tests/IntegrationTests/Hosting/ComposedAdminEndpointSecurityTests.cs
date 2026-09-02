// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MailFathom.AppHost;
using MailFathom.IntegrationTests.Orchestration;
using Xunit;

namespace MailFathom.IntegrationTests.Hosting;

/// <summary>Proves that the controls in front of the administrative endpoint run before its routes answer.</summary>
/// <remarks>
/// <para>
/// Which credentials authenticate and how they are compared is unit-tested against <c>ApiKeyAuthenticator</c>, and how
/// the isolation predicate matches a path is unit-tested against <c>SurfaceIsolation</c>; neither is repeated
/// here. What only a composed host can establish is the part those tests structurally cannot see: that the endpoint has
/// a listener of its own at all, that the authorization requirement is attached to the route group rather than merely
/// registered in a container, and that a refusal happens before the session handler produces anything. Every assertion
/// below is about that, which is why each one reads the body for the product name the handler would have returned.
/// </para>
/// <para>
/// The last test is the one nothing else in either suite can reach. Each endpoint's keys are correct in isolation, and
/// the fault this suite exists to catch is a composition in which they are not separate — the scheme collision found in
/// review rather than by a test was exactly that. Presenting each surface's credential to the other is what makes the
/// separation observable from where a caller stands.
/// </para>
/// <para>
/// Nothing here carries <c>[RequiresIntegrationCoverage]</c>, for the reason
/// <see cref="ComposedMcpEndpointSecurityTests" /> states: the classes exercised are either unit-covered already or
/// belong to <c>Host</c>, which is outside the coverage denominator.
/// </para>
/// </remarks>
[Collection(ComposedHostCollectionDefinition.Name)]
public sealed class ComposedAdminEndpointSecurityTests
{
    /// <summary>The route a client reaches once it has been handed a credential, and the cheapest thing the surface answers.</summary>
    private const string SessionRoute = "/api/admin/session";

    /// <summary>The product name the session handler reports, which is what distinguishes its answer from a refusal in front of it.</summary>
    private const string ServiceNamedByTheHandler = "MailFathom";

    private readonly MailFathomOrchestrationFixture orchestration;

    /// <summary>Initializes the tests against the assembly's orchestration.</summary>
    /// <param name="orchestration">The orchestration fixture, which starts the host on first request.</param>
    public ComposedAdminEndpointSecurityTests(MailFathomOrchestrationFixture orchestration) =>
        this.orchestration = orchestration;

    [Fact]
    public async Task AdminEndpoint_RequestCarryingNoCredential_IsRefusedBeforeTheSessionHandlerAnswers()
    {
        // Arrange
        using var client = await this.orchestration.OpenAdminEndpointClientAsync(TestContext.Current.CancellationToken);

        // Act
        using var request = SessionRequest(apiKey: null);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
        Assert.DoesNotContain(
            ServiceNamedByTheHandler,
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The two requests differ only in whether a credential was presented, and a client that could tell them apart
    /// would learn that some key exists. This is the one claim of the design that a caller can actually check, so it is
    /// checked from where a caller stands.
    /// </summary>
    [Fact]
    public async Task AdminEndpoint_RequestCarryingAnUnrecognizedCredential_IsRefusedIdenticallyToOneCarryingNone()
    {
        // Arrange
        using var client = await this.orchestration.OpenAdminEndpointClientAsync(TestContext.Current.CancellationToken);

        // Act
        using var anonymousRequest = SessionRequest(apiKey: null);
        using var withoutCredential = await client.SendAsync(anonymousRequest, TestContext.Current.CancellationToken);
        using var unrecognizedRequest = SessionRequest("a-key-this-deployment-never-configured");
        using var withUnrecognizedCredential = await client.SendAsync(
            unrecognizedRequest,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(withoutCredential.StatusCode, withUnrecognizedCredential.StatusCode);
        Assert.Equal(
            withoutCredential.Headers.WwwAuthenticate.ToString(),
            withUnrecognizedCredential.Headers.WwwAuthenticate.ToString());
        Assert.Equal(
            await withoutCredential.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            await withUnrecognizedCredential.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The body is what <c>mfctl login</c> reads to decide that a credential it has just been handed is one this
    /// deployment accepts, so all three of its parts are asserted: the product, so a client can tell it reached
    /// MailFathom rather than something else answering the port; the running version; and the deployment's own name for
    /// the credential that authenticated, which is what proves the principal reached the handler rather than the route
    /// merely admitting the request.
    /// </summary>
    [Fact]
    public async Task AdminEndpoint_RequestCarryingTheConfiguredKey_ReachesTheSessionHandler()
    {
        // Arrange
        using var client = await this.orchestration.OpenAdminEndpointClientAsync(TestContext.Current.CancellationToken);
        using var request = SessionRequest(OrchestrationContract.AdminApiKey);

        // Act
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var session = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(ServiceNamedByTheHandler, session.RootElement.GetProperty("service").GetString());
        Assert.Equal(OrchestrationContract.AdminApiKeyName, session.RootElement.GetProperty("credential").GetString());

        // A version rather than the exact number, because what the composed host establishes is that the handler read
        // the running assembly's stamp: the fallback it reports when a build stamped nothing is the word "unknown", and
        // a number is what says it did not have to reach for it.
        Assert.Matches(@"^\d+\.\d+\.\d+", session.RootElement.GetProperty("version").GetString());
    }

    /// <summary>
    /// Neither surface's key authenticates the other's routes, which is the whole reason the administrative endpoint is
    /// a second listener with a section of its own rather than more routes on the MCP one. Both directions are asserted
    /// in one test because they are one claim, and a composition that broke the separation would break it in whichever
    /// direction happened to be checked.
    /// </summary>
    [Fact]
    public async Task EachEndpoint_PresentedWithTheOthersConfiguredKey_RefusesItLikeAnyUnrecognizedCredential()
    {
        // Arrange
        using var adminClient = await this.orchestration.OpenAdminEndpointClientAsync(TestContext.Current.CancellationToken);
        using var mcpClient = await this.orchestration.OpenMcpEndpointClientAsync(TestContext.Current.CancellationToken);

        // Act
        using var mcpKeyOnAdminEndpoint = SessionRequest(OrchestrationContract.McpApiKey);
        using var administrativeRefusal = await adminClient.SendAsync(
            mcpKeyOnAdminEndpoint,
            TestContext.Current.CancellationToken);

        using var adminKeyOnMcpEndpoint = ListToolsRequest(OrchestrationContract.AdminApiKey);
        using var protocolRefusal = await mcpClient.SendAsync(
            adminKeyOnMcpEndpoint,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, administrativeRefusal.StatusCode);
        Assert.DoesNotContain(
            ServiceNamedByTheHandler,
            await administrativeRefusal.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Unauthorized, protocolRefusal.StatusCode);
        Assert.Equal("Bearer", Assert.Single(protocolRefusal.Headers.WwwAuthenticate).Scheme);
    }

    /// <summary>
    /// What no unit test can reach: that the filter reading each route's published permission is attached to the group
    /// the mapping builds, rather than merely written. An endpoint filter is not endpoint metadata, so nothing about it
    /// is readable off a built endpoint — deleting the line that attaches it leaves every route serving any admitted
    /// credential. Both directions are one claim and are asserted together: the same credential reaches the route its
    /// one permission publishes and is refused the route another does.
    /// </summary>
    [Fact]
    public async Task AdminEndpoint_ACredentialGrantedOneAdministrativePermission_ReachesThatRouteAndIsRefusedAnother()
    {
        // Arrange
        using var client = await this.orchestration.OpenAdminEndpointClientAsync(TestContext.Current.CancellationToken);

        // Act
        using var permittedRequest = AuthenticatedGet("/api/admin/rules", OrchestrationContract.AdminNarrowedApiKey);
        using var permitted = await client.SendAsync(permittedRequest, TestContext.Current.CancellationToken);

        using var refusedRequest = AuthenticatedGet("/api/admin/contacts", OrchestrationContract.AdminNarrowedApiKey);
        using var refused = await client.SendAsync(refusedRequest, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, permitted.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        using var problem = JsonDocument.Parse(
            await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal("mailfathom.admin.audit.read", problem.RootElement.GetProperty("permission").GetString());
        Assert.False(problem.RootElement.TryGetProperty("contacts", out _));
    }

    /// <summary>
    /// The one route published under no permission, reached by a credential the rest of the surface refuses, reporting
    /// back exactly what that credential holds. It is what <c>mfctl status</c> prints, and the only way an operator
    /// learns a grant without reading the deployment's own configuration.
    /// </summary>
    [Fact]
    public async Task AdminEndpoint_TheSessionRoute_ReportsTheGrantTheCredentialWasAdmittedUnder()
    {
        // Arrange
        using var client = await this.orchestration.OpenAdminEndpointClientAsync(TestContext.Current.CancellationToken);
        using var request = SessionRequest(OrchestrationContract.AdminNarrowedApiKey);

        // Act
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var session = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(
            OrchestrationContract.AdminNarrowedApiKeyName,
            session.RootElement.GetProperty("credential").GetString());
        Assert.Equal(
            [OrchestrationContract.AdminNarrowedPermission],
            session.RootElement.GetProperty("permissions").EnumerateArray().Select(name => name.GetString()));
    }

    /// <summary>Builds a bearer-authenticated read of one administrative route.</summary>
    private static HttpRequestMessage AuthenticatedGet(string route, string apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(route, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        return request;
    }

    /// <summary>Builds a request for the session route, optionally presenting a bearer credential.</summary>
    private static HttpRequestMessage SessionRequest(string? apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(SessionRoute, UriKind.Relative));

        if (apiKey is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        return request;
    }

    /// <summary>Builds the MCP tool listing, which is the cheapest thing that surface answers a credential for.</summary>
    /// <remarks>
    /// The origin is one the deployment serves, deliberately: two controls guard this route, and a request refused for
    /// its origin would answer nothing about the credential it presented. Both content types the Streamable HTTP
    /// transport may reply with are accepted, because which one it chooses is not what this test is about.
    /// </remarks>
    private static HttpRequestMessage ListToolsRequest(string apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/mcp", UriKind.Relative))
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "tools/list",
            }),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Add("Origin", OrchestrationContract.McpPermittedOrigin);

        return request;
    }
}
