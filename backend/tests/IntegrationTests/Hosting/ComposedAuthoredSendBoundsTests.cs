// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Net;
using System.Text.Json;
using MailFathom.AppHost;
using MailFathom.Domain.Failures;
using MailFathom.IntegrationTests.Orchestration;
using Xunit;

namespace MailFathom.IntegrationTests.Hosting;

/// <summary>Proves that the bounds an authored send meets are reached over the transport a caller actually uses.</summary>
/// <remarks>
/// <para>
/// The use case is where both refusals live and the unit suite is where each is proven at its boundary. What only a
/// started host can establish is that the deployment's own configuration reaches them at all: the recipient policy an
/// operator wrote and the per-caller ceiling they set are bound during composition, judged inside the use case the tool
/// calls, and answered to the client as the coded refusal the boundary publishes. A substitute for any part of that
/// would assert the arrangement rather than the path.
/// </para>
/// <para>
/// Neither call writes anything: a refusal is raised before the outgoing record is created, so nothing is queued, the
/// caller's period is charged nothing, and no test that runs after these observes a mailbox this class changed.
/// </para>
/// <para>
/// Both refusals are reached in one message rather than by exhausting anything, which is what keeps them independent of
/// the order this collection ran in. The policy refusal names an organization only this class addresses, and the
/// ceiling refusal names more people in one message than one caller is permitted at all.
/// </para>
/// </remarks>
[Collection(ComposedHostCollectionDefinition.Name)]
public sealed class ComposedAuthoredSendBoundsTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>The people one message names, which is one more than the ceiling permits one caller for a whole period.</summary>
    private static readonly string[] MoreRecipientsThanTheCeilingPermits = [.. Enumerable
        .Range(0, OrchestrationContract.ComposedHostCallerRecipientCeiling + 1)
        .Select(position => string.Create(
            CultureInfo.InvariantCulture,
            $"ceiling{position}@mailfathom.test"))];

    /// <summary>A message the deployment may never write to is refused on the surface a caller reaches, not below it.</summary>
    [Fact]
    public async Task CallTool_RecipientThePolicyRefuses_IsAnsweredAsTheCodedRefusalAndQueuesNothing()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await orchestration.OpenMcpEndpointClientAsync(cancellationToken);
        using var request = SendCall(
            [$"stranger@{OrchestrationContract.ComposedHostRefusedRecipientDomain}"],
            idempotencyKey: "authored-send-bounds-policy",
            id: 1);

        // Act
        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            MailFathomErrorCode.OutgoingRecipientRefusedByPolicy.ToString(),
            RefusalIn(body),
            StringComparison.Ordinal);
    }

    /// <summary>One caller's own ceiling is counted apart from the deployment's, and the refusal says which was reached.</summary>
    [Fact]
    public async Task CallTool_MoreRecipientsThanOneCallerMayReach_IsAnsweredAsTheCeilingRefusal()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await orchestration.OpenMcpEndpointClientAsync(cancellationToken);
        using var request = SendCall(
            MoreRecipientsThanTheCeilingPermits,
            idempotencyKey: "authored-send-bounds-ceiling",
            id: 2);

        // Act
        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var refusal = RefusalIn(body);
        Assert.Contains(MailFathomErrorCode.OutgoingMailCeilingReached.ToString(), refusal, StringComparison.Ordinal);
        Assert.Contains("one caller", refusal, StringComparison.Ordinal);
    }

    private static HttpRequestMessage SendCall(IReadOnlyList<string> recipients, string idempotencyKey, int id) =>
        McpToolCall.Of(
            "send_email",
            new
            {
                account = OrchestrationContract.ServedMailAccountId,
                to = recipients,
                subject = "authored-send-bounds",
                plainTextBody = "This message is refused before anything is written down.",
                idempotencyKey,
            },
            id);

    /// <summary>Reads the refusal text out of a tool call the boundary answered as an error.</summary>
    /// <remarks>
    /// A refusal is reported inside a successful result rather than as a status code, so a call that was somehow queued
    /// would otherwise read as one that answered with no text at all.
    /// </remarks>
    private static string RefusalIn(string body)
    {
        using var message = JsonDocument.Parse(McpToolCall.MessageIn(body));
        var result = message.RootElement.GetProperty("result");

        Assert.True(
            result.TryGetProperty("isError", out var isError) && isError.GetBoolean(),
            $"The tool call was not answered as an error: {body}");

        return result.GetProperty("content")[0].GetProperty("text").GetString()!;
    }
}
