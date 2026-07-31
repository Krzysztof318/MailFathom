// Copyright © 2026 Krzysztof Kasprowicz

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MailMcp.AppHost;
using MailMcp.IntegrationTests.Orchestration;
using Xunit;

namespace MailMcp.IntegrationTests.Hosting;

/// <summary>Proves that the controls in front of the MCP endpoint run before the protocol surface answers.</summary>
/// <remarks>
/// <para>
/// Which credentials authenticate, how they are compared, and which origins are served are all unit-tested against
/// <c>McpApiKeyAuthenticator</c> and <c>McpOriginPolicy</c>, and none of that is repeated here. What only a composed
/// host can establish is the part those tests cannot see: that the checks are wired into the request pipeline ahead of
/// the endpoint at all, and that a refusal happens before the protocol surface produces anything. Every assertion below
/// is about that ordering, which is why each one reads the body for a tool name the surface would have listed.
/// </para>
/// <para>
/// Nothing here carries <c>[RequiresIntegrationCoverage]</c>. The marker records that a class's verification lives in
/// this suite because a unit test cannot reach it; the classes exercised here are either unit-covered already or belong
/// to <c>Host</c>, which is outside the coverage denominator. Marking them would move well-covered code out of the
/// measurement it is passing.
/// </para>
/// </remarks>
[Collection(ComposedHostCollectionDefinition.Name)]
public sealed class ComposedMcpEndpointSecurityTests
{
    private const string ToolListedByTheProtocolSurface = "list_emails";

    private readonly MailMcpOrchestrationFixture orchestration;

    /// <summary>Initializes the tests against the assembly's orchestration.</summary>
    /// <param name="orchestration">The orchestration fixture, which starts the host on first request.</param>
    public ComposedMcpEndpointSecurityTests(MailMcpOrchestrationFixture orchestration) =>
        this.orchestration = orchestration;

    [Fact]
    public async Task McpEndpoint_RequestCarryingNoCredential_IsRefusedBeforeTheProtocolSurfaceAnswers()
    {
        // Arrange
        using var client = await this.ComposedHostClientAsync();

        // Act
        using var request = ListToolsRequest(apiKey: null);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
        Assert.DoesNotContain(
            ToolListedByTheProtocolSurface,
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The two requests differ only in whether a credential was presented, and a client that could tell them apart
    /// would learn that some key exists. This is the one claim of the design that a caller can actually check, so it is
    /// checked from where a caller stands.
    /// </summary>
    [Fact]
    public async Task McpEndpoint_RequestCarryingAnUnrecognizedCredential_IsRefusedIdenticallyToOneCarryingNone()
    {
        // Arrange
        using var client = await this.ComposedHostClientAsync();

        // Act
        using var anonymousRequest = ListToolsRequest(apiKey: null);
        using var withoutCredential = await client.SendAsync(anonymousRequest, TestContext.Current.CancellationToken);
        using var unrecognizedRequest = ListToolsRequest("a-key-this-deployment-never-configured");
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

    [Fact]
    public async Task McpEndpoint_RequestCarryingTheConfiguredKeyFromAServedOrigin_ReachesTheProtocolSurface()
    {
        // Arrange
        using var client = await this.ComposedHostClientAsync();
        using var request = ListToolsRequest(OrchestrationContract.McpApiKey);
        request.Headers.Add("Origin", OrchestrationContract.McpPermittedOrigin);

        // Act
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            ToolListedByTheProtocolSurface,
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
    }

    /// <summary>A valid credential is deliberately presented, so what the refusal proves is the origin check rather than the absence of one.</summary>
    [Fact]
    public async Task McpEndpoint_RequestFromAnOriginTheDeploymentDoesNotServe_IsRefusedEvenWithTheConfiguredKey()
    {
        // Arrange
        using var client = await this.ComposedHostClientAsync();
        using var request = ListToolsRequest(OrchestrationContract.McpApiKey);
        request.Headers.Add("Origin", "https://attacker.mailmcp.test");

        // Act
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain(
            ToolListedByTheProtocolSurface,
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The requirement is carried by the MCP route rather than by a fallback policy, and the difference is not visible
    /// from configuration. A probe has no credential to present, so a readiness response that started asking for one
    /// would take the deployment out of rotation rather than protect anything.
    /// </summary>
    [Fact]
    public async Task ReadinessResponse_WithNoCredential_StillAnswersBecauseOnlyTheMcpRouteRequiresOne()
    {
        // Arrange
        using var client = await this.ComposedHostClientAsync();

        // Act
        using var response = await client.GetAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Builds a JSON-RPC request for the tool listing, optionally presenting a bearer credential.</summary>
    /// <remarks>
    /// The tool listing is the cheapest thing the protocol surface will answer, and it names a tool, so one response
    /// body distinguishes "the surface answered" from "something in front of it did". Both content types the Streamable
    /// HTTP transport may reply with are accepted, because which one it chooses is not what these tests are about.
    /// </remarks>
    private static HttpRequestMessage ListToolsRequest(string? apiKey)
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

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        if (apiKey is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        return request;
    }

    private async Task<HttpClient> ComposedHostClientAsync() => new()
    {
        BaseAddress = await this.orchestration.StartMailMcpHostAsync(TestContext.Current.CancellationToken),
    };
}
