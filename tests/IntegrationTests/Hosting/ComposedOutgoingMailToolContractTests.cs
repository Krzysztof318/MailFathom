// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Text.Json;
using MailFathom.AppHost;
using MailFathom.IntegrationTests.Orchestration;
using Xunit;

namespace MailFathom.IntegrationTests.Hosting;

/// <summary>Proves that a caller can queue a message over the real transport, read back what became of it, and stop it.</summary>
/// <remarks>
/// <para>
/// Nothing below the transport can prove this trio. The identifier one call answers with has to be accepted by the
/// next, the record has to be found in the database this deployment actually wrote it to, and the withdrawal has to
/// reach the same row through a statement PostgreSQL evaluates — a substitute for any of those would assert the
/// arrangement rather than the contract.
/// </para>
/// <para>
/// The scoping is the part that most needs a real endpoint: the principal a record is written under comes from
/// whatever the transport admitted, so a test that composed the use cases in process would be stating the principal it
/// then asserts. Here the same credential queues and reads, and the record is found because the deployment admitted
/// the same caller twice.
/// </para>
/// <para>
/// Four calls, and four credentials spent from a bucket this collection shares. The rate limit is what a sibling class
/// measures, so this class makes the fewest calls the sequence needs — the fourth exists because a withdrawal reported
/// in an answer and a withdrawal that is durable are different claims — and asserts everything else about what came
/// back.
/// </para>
/// </remarks>
[Collection(ComposedHostCollectionDefinition.Name)]
public sealed class ComposedOutgoingMailToolContractTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>The one person the queued message is addressed to, who never receives it because it is withdrawn.</summary>
    private static readonly string[] QueuedTo = ["reader@mailfathom.test"];

    /// <summary>The whole path a client takes to queue a message, ask what became of it, and stop it before it leaves.</summary>
    /// <remarks>
    /// One message carries all three calls deliberately. What the class is about is that the identity travels between
    /// them, so splitting them across records would prove each call in isolation and the contract between them not at
    /// all.
    /// </remarks>
    [Fact]
    public async Task CallTool_AQueuedMessage_IsReadBackAndThenWithdrawnBeforeItLeaves()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        using var client = await orchestration.OpenMcpEndpointClientAsync(cancellationToken);
        using var sendRequest = McpToolCall.Of(
            "send_email",
            new
            {
                account = OrchestrationContract.ServedMailAccountId,
                to = QueuedTo,
                subject = "outgoing-mail-tool-contract",
                plainTextBody = "This one is withdrawn before it leaves.",
                idempotencyKey = "outgoing-mail-tool-contract-send",
            },
            id: 1);

        // Act
        using var sendResponse = await client.SendAsync(sendRequest, cancellationToken);
        var sendBody = await sendResponse.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, sendResponse.StatusCode);

        using var sendMessage = JsonDocument.Parse(McpToolCall.MessageIn(sendBody));
        var queued = RecordIn(sendMessage, sendBody);
        var outgoingEmailId = queued.GetProperty("outgoingEmailId").GetString();

        using var readRequest = McpToolCall.Of("get_outgoing_email", new { outgoingEmailId }, id: 2);
        using var readResponse = await client.SendAsync(readRequest, cancellationToken);
        var readBody = await readResponse.Content.ReadAsStringAsync(cancellationToken);

        using var cancelRequest = McpToolCall.Of("cancel_outgoing_email", new { outgoingEmailId }, id: 3);
        using var cancelResponse = await client.SendAsync(cancelRequest, cancellationToken);
        var cancelBody = await cancelResponse.Content.ReadAsStringAsync(cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);

        using var readMessage = JsonDocument.Parse(McpToolCall.MessageIn(readBody));
        using var cancelMessage = JsonDocument.Parse(McpToolCall.MessageIn(cancelBody));

        var read = RecordIn(readMessage, readBody);
        var withdrawn = RecordIn(cancelMessage, cancelBody);

        // The identity the send answered with is what the other two calls name, which is the contract between them.
        Assert.Equal(outgoingEmailId, read.GetProperty("outgoingEmailId").GetString());
        Assert.Equal(outgoingEmailId, withdrawn.GetProperty("outgoingEmailId").GetString());
        Assert.Equal(OrchestrationContract.ServedMailAccountId, read.GetProperty("accountId").GetString());

        // Queued rather than sent: this host runs no delivery pass, so nothing has been offered to a mail server.
        Assert.Equal("queued", read.GetProperty("state").GetString());
        Assert.Equal(0, read.GetProperty("attemptCount").GetInt32());

        // The recipients are the ones the send named, with nothing said about them yet and nobody else listed.
        var recipient = Assert.Single(read.GetProperty("recipients").EnumerateArray());
        Assert.Equal(QueuedTo[0], recipient.GetProperty("address").GetString());
        Assert.Equal("to", recipient.GetProperty("header").GetString());
        Assert.Equal("pending", recipient.GetProperty("state").GetString());

        // Withdrawn, in the state's own published spelling, and durably rather than only in the answer.
        Assert.Equal("cancelled", withdrawn.GetProperty("state").GetString());

        using var reReadRequest = McpToolCall.Of("get_outgoing_email", new { outgoingEmailId }, id: 4);
        using var reReadResponse = await client.SendAsync(reReadRequest, cancellationToken);
        var reReadBody = await reReadResponse.Content.ReadAsStringAsync(cancellationToken);

        using var reReadMessage = JsonDocument.Parse(McpToolCall.MessageIn(reReadBody));
        Assert.Equal("cancelled", RecordIn(reReadMessage, reReadBody).GetProperty("state").GetString());
    }

    /// <summary>Reads the structured record out of a successful tool call, failing on a call that was answered as an error.</summary>
    /// <remarks>
    /// A protocol-level error is reported inside a successful result rather than as a status code, so a call that
    /// failed would otherwise read as one that answered with no fields.
    /// </remarks>
    private static JsonElement RecordIn(JsonDocument message, string body)
    {
        var result = message.RootElement.GetProperty("result");

        Assert.False(
            result.TryGetProperty("isError", out var isError) && isError.GetBoolean(),
            $"The tool call was answered as an error: {body}");

        return result.GetProperty("structuredContent");
    }
}
