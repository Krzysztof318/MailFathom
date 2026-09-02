// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MailFathom.AppHost;
using MailFathom.IntegrationTests.Orchestration;
using Xunit;

namespace MailFathom.IntegrationTests.Hosting;

/// <summary>Proves that a client narrowing its own view of the surface reaches the protocol endpoint and is served it.</summary>
/// <remarks>
/// <para>
/// What only a composed host can establish is that the header survives the path a request actually takes — the
/// listener, the credential check, the CORS policy, the transport — and is read by the listing the endpoint answers
/// with. Every rule below it is unit-tested against the filters themselves: that the intersection narrows and never
/// widens, that an unknown value is ignored, and that a call to a tool outside the published set is refused. None of
/// that is repeated here.
/// </para>
/// <para>
/// The deployment this runs against names no category, which is the unset configuration every other test in this suite
/// depends on: the listing without the header is what proves that, and the listing with it is the narrowing. Narrowing
/// the host's own configuration would take tools away from every other composed-host test, so the configured half is
/// proven over the composed registration in the unit suite instead.
/// </para>
/// <para>
/// Nothing here carries <c>[RequiresIntegrationCoverage]</c>, for the reason the endpoint security tests give: the
/// classes exercised are unit-covered already, and marking them would move well-covered code out of the measurement it
/// is passing.
/// </para>
/// </remarks>
[Collection(ComposedHostCollectionDefinition.Name)]
public sealed class ComposedMcpToolCategoryTests
{
    /// <summary>The header a client narrows with, written out rather than read from the host assembly, which this suite does not reference.</summary>
    private const string ToolCategoryHeaderName = "MailFathom-Tool-Categories";

    private const string MailboxTool = "list_emails";

    private const string ContactTool = "list_contacts";

    private readonly MailFathomOrchestrationFixture orchestration;

    /// <summary>Initializes the tests against the assembly's orchestration.</summary>
    /// <param name="orchestration">The orchestration fixture, which starts the host on first request.</param>
    public ComposedMcpToolCategoryTests(MailFathomOrchestrationFixture orchestration) =>
        this.orchestration = orchestration;

    /// <summary>A deployment naming no category publishes every one of them, which is what makes the narrowing below visible as one.</summary>
    [Fact]
    public async Task ListTools_ARequestNamingNoCategory_IsServedToolsFromMoreThanOneCategory()
    {
        // Arrange
        using var client = await this.orchestration.OpenMcpEndpointClientAsync(TestContext.Current.CancellationToken);

        // Act
        var listed = await ListedToolsAsync(client, requestedCategories: null);

        // Assert
        Assert.Contains(MailboxTool, listed);
        Assert.Contains(ContactTool, listed);
    }

    [Fact]
    public async Task ListTools_ARequestNamingOneCategory_IsServedThatCategoryAlone()
    {
        // Arrange
        using var client = await this.orchestration.OpenMcpEndpointClientAsync(TestContext.Current.CancellationToken);

        // Act
        var listed = await ListedToolsAsync(client, requestedCategories: "mailbox");

        // Assert
        Assert.Contains(MailboxTool, listed);
        Assert.DoesNotContain(ContactTool, listed);
    }

    /// <summary>A narrowed session is not a listing that lies: the tools it left out answer nothing either.</summary>
    [Fact]
    public async Task CallTool_AToolTheRequestNarrowedAway_IsAnsweredAsAnUnknownTool()
    {
        // Arrange
        using var client = await this.orchestration.OpenMcpEndpointClientAsync(TestContext.Current.CancellationToken);
        using var request = McpToolCall.Of(ContactTool, new { });
        request.Headers.Add(ToolCategoryHeaderName, "mailbox");

        // Act
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var message = McpToolCall.MessageIn(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("Unknown tool", message, StringComparison.Ordinal);
    }

    /// <summary>Asks the endpoint for its listing and reads the names out of the answer.</summary>
    private static async Task<IReadOnlyList<string>> ListedToolsAsync(HttpClient client, string? requestedCategories)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/mcp", UriKind.Relative))
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "tools/list",
            }),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", OrchestrationContract.McpApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        if (requestedCategories is not null)
        {
            request.Headers.Add(ToolCategoryHeaderName, requestedCategories);
        }

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        var message = McpToolCall.MessageIn(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        using var answer = JsonDocument.Parse(message);

        return
        [
            .. answer.RootElement
                .GetProperty("result")
                .GetProperty("tools")
                .EnumerateArray()
                .Select(tool => tool.GetProperty("name").GetString() ?? string.Empty),
        ];
    }
}
