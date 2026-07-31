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

    /// <summary>
    /// The requirement is a convention carried by the route rather than a check written into a handler, so what has to
    /// be established is which requests that route actually exposes. The transport is stateless, and the SDK maps the
    /// post alone for it: the get that would open a server stream and the delete that would end a session are not
    /// routes at all, which routing answers before authorization is ever consulted. There is therefore no second way
    /// into the protocol surface to protect — and the one way in refuses the method that returns mail.
    /// </summary>
    [Fact]
    public async Task McpEndpoint_TheStatelessTransportsOnlyVerb_RefusesAToolCallCarryingNoCredential()
    {
        // Arrange
        using var client = await this.ComposedHostClientAsync();
        using var openStream = McpRequest(HttpMethod.Get, jsonRpcPayload: null);
        using var endSession = McpRequest(HttpMethod.Delete, jsonRpcPayload: null);
        using var callTool = McpRequest(HttpMethod.Post, ToolCallPayload());

        // Act
        using var streamAttempt = await client.SendAsync(openStream, TestContext.Current.CancellationToken);
        using var sessionAttempt = await client.SendAsync(endSession, TestContext.Current.CancellationToken);
        using var toolCallRefusal = await client.SendAsync(callTool, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [HttpStatusCode.MethodNotAllowed, HttpStatusCode.MethodNotAllowed],
            [.. new[] { streamAttempt, sessionAttempt }.Select(attempt => attempt.StatusCode)]);
        Assert.Equal(HttpStatusCode.Unauthorized, toolCallRefusal.StatusCode);
        Assert.Equal("Bearer", Assert.Single(toolCallRefusal.Headers.WwwAuthenticate).Scheme);
        Assert.Empty(await toolCallRefusal.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
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
    /// What the limiter does with a partition is unit-tested; what only a composed host shows is that a limiter is on
    /// this route at all. Two of its parts are invisible from a unit test and are asserted together here rather than in
    /// tests of their own: that the route carries the policy, and that one client's exhausted capacity is not another's,
    /// which is the whole point of partitioning and would look identical from inside the process if it were broken.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The burst is dispatched together rather than one request after another, so what the limiter sees is a burst
    /// however fast the host answers. Sending them in sequence against a slow machine would let capacity replenish
    /// between them and leave the test passing because nothing was ever over the limit.
    /// </para>
    /// <para>
    /// Two limiters can answer <c>429</c> on this route, so the refusals are checked for the one signal only the client
    /// bucket produces: a <c>Retry-After</c>, which it can compute because it knows when the next replenishment lands
    /// and which a concurrency refusal never carries. The topology also raises its concurrency ceiling well above this
    /// burst, so the process-wide limiter is not merely distinguishable here but cannot have refused anything at all.
    /// Without both, this test would pass on concurrency refusals alone even if the route carried no policy.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task McpEndpoint_AClientBurstingPastItsCapacity_IsRefusedWithoutSpendingAnotherClients()
    {
        // Arrange
        using var client = await this.ComposedHostClientAsync();
        var burstSize = OrchestrationContract.McpRateLimitTokenCapacity * 3;

        // Act
        var burst = await Task.WhenAll(Enumerable
            .Range(0, burstSize)
            .Select(_ => this.AnswerToAsync(client, OrchestrationContract.McpExpendableApiKey)));

        using var afterTheBurst = ListToolsRequest(OrchestrationContract.McpApiKey);
        afterTheBurst.Headers.Add("Origin", OrchestrationContract.McpPermittedOrigin);
        using var otherClient = await client.SendAsync(afterTheBurst, TestContext.Current.CancellationToken);

        // Assert
        var refusals = burst.Where(answer => answer.StatusCode == HttpStatusCode.TooManyRequests).ToArray();

        Assert.NotEmpty(refusals);
        Assert.All(refusals, refusal => Assert.Empty(refusal.Body));
        Assert.All(refusals, refusal => Assert.Equal("no-store", refusal.CacheControl));
        Assert.All(refusals, refusal => Assert.NotNull(refusal.RetryAfter));
        Assert.Equal(HttpStatusCode.OK, otherClient.StatusCode);
        Assert.Contains(
            ToolListedByTheProtocolSurface,
            await otherClient.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
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
        var request = McpRequest(HttpMethod.Post, new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/list",
        });

        if (apiKey is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        return request;
    }

    /// <summary>Builds the invocation of a tool that reads mail, which is the request whose refusal the design exists for.</summary>
    /// <remarks>
    /// The arguments are the ones a caller would send. A refusal that happens before the endpoint runs cannot depend on
    /// them, and the point of sending a well-formed call is that the refusal is not explained by malformedness either.
    /// </remarks>
    private static object ToolCallPayload() => new
    {
        jsonrpc = "2.0",
        id = 1,
        method = "tools/call",
        @params = new
        {
            name = ToolListedByTheProtocolSurface,
            arguments = new { accountId = "any-account", folder = "INBOX" },
        },
    };

    /// <summary>Addresses one verb of the MCP route, accepting both content types the transport may reply with.</summary>
    /// <remarks>
    /// The payload is declared as <see cref="object" /> so its runtime shape is what gets serialized, which keeps each
    /// caller's request readable at the call site instead of behind a parameter list describing JSON-RPC.
    /// </remarks>
    private static HttpRequestMessage McpRequest(HttpMethod verb, object? jsonRpcPayload)
    {
        var request = new HttpRequestMessage(verb, new Uri("/mcp", UriKind.Relative))
        {
            Content = jsonRpcPayload is null ? null : JsonContent.Create(jsonRpcPayload),
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        return request;
    }

    private async Task<HttpClient> ComposedHostClientAsync() => new()
    {
        BaseAddress = await this.orchestration.StartMailMcpHostAsync(TestContext.Current.CancellationToken),
    };

    /// <summary>Sends one tool listing and reads everything a burst is judged on before the response is released.</summary>
    /// <remarks>
    /// The response is disposed here rather than handed back, so a burst of them cannot hold connections open while the
    /// rest of the assertions run. What the caller keeps is the small record below, which is why the body is read now.
    /// </remarks>
    private async Task<McpAnswer> AnswerToAsync(HttpClient client, string apiKey)
    {
        using var request = ListToolsRequest(apiKey);
        request.Headers.Add("Origin", OrchestrationContract.McpPermittedOrigin);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        return new McpAnswer(
            response.StatusCode,
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            response.Headers.CacheControl?.ToString(),
            response.Headers.RetryAfter?.ToString());
    }

    /// <summary>What one request in a burst is judged on, read before its response was released.</summary>
    /// <remarks><c>RetryAfter</c> is what says which limiter refused: only the per-client bucket knows when capacity returns.</remarks>
    private sealed record McpAnswer(HttpStatusCode StatusCode, string Body, string? CacheControl, string? RetryAfter);
}
