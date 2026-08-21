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
/// The wait in the middle is the deployment, not this class being careful. Every host registers the outbox delivery
/// worker, and it answers the signal a send raises, so a record queued by a tool call is claimed within milliseconds
/// and cannot be withdrawn while that claim stands. What this topology's submission host gives the attempt is a name
/// that does not resolve, which defers the send rather than ending it: the stage moves back to recorded, the lease is
/// released, and the failure that ended the attempt is written onto the record. So the class waits for that failure to
/// appear and withdraws inside the retry delay the topology stretches past the length of a run, which is a window the
/// deployment guarantees rather than one this class hopes for.
/// </para>
/// <para>
/// Every call spends a credential from a bucket this collection shares. The rate limit is what a sibling class
/// measures, so this class makes the fewest calls the sequence needs — the last exists because a withdrawal reported in
/// an answer and a withdrawal that is durable are different claims — and bounds the reads it waits over.
/// </para>
/// </remarks>
[Collection(ComposedHostCollectionDefinition.Name)]
public sealed class ComposedOutgoingMailToolContractTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>The one person the queued message is addressed to, who never receives it because it is withdrawn.</summary>
    private static readonly string[] QueuedTo = ["reader@mailfathom.test"];

    /// <summary>How long the class waits between reads while the deployment's own attempt on the send is still running.</summary>
    private static readonly TimeSpan DeferralReadInterval = TimeSpan.FromSeconds(1);

    /// <summary>How many reads that wait may take, which bounds both how long the class waits and what it spends.</summary>
    /// <remarks>
    /// An attempt against a name that does not resolve ends in the time a resolver takes to answer, so the ceiling is
    /// reached only by a run where nothing is attempted at all — which is a defect in the arrangement rather than a
    /// slow machine, and is reported as one.
    /// </remarks>
    private const int DeferralReadCeiling = 20;

    /// <summary>The identifier the withdrawal is called under, past every read the wait above may have made.</summary>
    private const int WithdrawalCallId = DeferralReadCeiling + 2;

    /// <summary>The identifier the read that proves the withdrawal durable is called under.</summary>
    private const int SettledCallId = DeferralReadCeiling + 3;

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

        var readBody = await ReadUntilDeferredAsync(client, outgoingEmailId, cancellationToken);

        using var cancelRequest = McpToolCall.Of("cancel_outgoing_email", new { outgoingEmailId }, id: WithdrawalCallId);
        using var cancelResponse = await client.SendAsync(cancelRequest, cancellationToken);
        var cancelBody = await cancelResponse.Content.ReadAsStringAsync(cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);

        using var readMessage = JsonDocument.Parse(McpToolCall.MessageIn(readBody));
        using var cancelMessage = JsonDocument.Parse(McpToolCall.MessageIn(cancelBody));

        var read = RecordIn(readMessage, readBody);
        var withdrawn = RecordIn(cancelMessage, cancelBody);

        // The identity the send answered with is what the other two calls name, which is the contract between them.
        Assert.Equal(outgoingEmailId, read.GetProperty("outgoingEmailId").GetString());
        Assert.Equal(outgoingEmailId, withdrawn.GetProperty("outgoingEmailId").GetString());
        Assert.Equal(OrchestrationContract.ServedMailAccountId, read.GetProperty("accountId").GetString());

        // Queued rather than sent, with the one attempt this deployment made on the record: the submission host it
        // offered the message to does not resolve, so nothing reached a mail server and the send stands exactly where a
        // message that has not begun to leave stands.
        Assert.Equal("queued", read.GetProperty("state").GetString());
        Assert.Equal(1, read.GetProperty("attemptCount").GetInt32());

        // The recipients are the ones the send named, with nothing said about them yet and nobody else listed.
        var recipient = Assert.Single(read.GetProperty("recipients").EnumerateArray());
        Assert.Equal(QueuedTo[0], recipient.GetProperty("address").GetString());
        Assert.Equal("to", recipient.GetProperty("header").GetString());
        Assert.Equal("pending", recipient.GetProperty("state").GetString());

        // Withdrawn, in the state's own published spelling, and durably rather than only in the answer.
        Assert.Equal("cancelled", withdrawn.GetProperty("state").GetString());

        using var reReadRequest = McpToolCall.Of("get_outgoing_email", new { outgoingEmailId }, id: SettledCallId);
        using var reReadResponse = await client.SendAsync(reReadRequest, cancellationToken);
        var reReadBody = await reReadResponse.Content.ReadAsStringAsync(cancellationToken);

        using var reReadMessage = JsonDocument.Parse(McpToolCall.MessageIn(reReadBody));
        Assert.Equal("cancelled", RecordIn(reReadMessage, reReadBody).GetProperty("state").GetString());
    }

    /// <summary>Reads the send back until the deployment's own delivery attempt has ended, and answers with that read.</summary>
    /// <param name="client">The endpoint client the whole sequence is spent on.</param>
    /// <param name="outgoingEmailId">The identity the send answered with.</param>
    /// <param name="cancellationToken">Stops the wait when the test does.</param>
    /// <returns>The body of the read that found the ended attempt on the record.</returns>
    /// <remarks>
    /// The failure code is what says the attempt ended rather than that it began. A claim raises the attempt count
    /// before the message is offered anywhere and holds a lease no withdrawal may write through, so a wait on the count
    /// alone would answer while the record was still claimed and leave the withdrawal below racing the same lease it
    /// was written to avoid. The code is written by the deferral itself, in the commit that releases that lease and
    /// moves the stage back, so a record carrying one is a record a withdrawal can be written to.
    /// </remarks>
    private static async Task<string> ReadUntilDeferredAsync(
        HttpClient client,
        string? outgoingEmailId,
        CancellationToken cancellationToken)
    {
        for (var read = 1; ; read++)
        {
            using var request = McpToolCall.Of("get_outgoing_email", new { outgoingEmailId }, id: read + 1);
            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using (var message = JsonDocument.Parse(McpToolCall.MessageIn(body)))
            {
                if (RecordIn(message, body).TryGetProperty("failureCode", out var failureCode)
                    && failureCode.ValueKind is not JsonValueKind.Null)
                {
                    return body;
                }
            }

            Assert.True(
                read < DeferralReadCeiling,
                $"The deployment recorded no ended delivery attempt on the send over {DeferralReadCeiling} reads, so "
                + $"the outbox worker this class waits for is not running: {body}");

            await Task.Delay(DeferralReadInterval, cancellationToken);
        }
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
