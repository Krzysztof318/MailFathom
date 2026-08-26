// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Text;
using System.Text.Json;
using MailFathom.AppHost;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Emails;
using MailFathom.IntegrationTests.Orchestration;
using MailFathom.IntegrationTests.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Hosting;

/// <summary>Proves that answering stored mail over the real transport queues the record the tool contract publishes.</summary>
/// <remarks>
/// <para>
/// A reply and a forward are the two calls whose arguments say least about what is sent: the caller names an email and
/// what it wrote, and the account, the addressing, the subject, the threading, the quotation, and a forward's files are
/// all read out of the stored copy. Nothing below the transport can prove that path end to end — the anchor has to
/// resolve against the database this deployment actually holds, through the folder mapping the started host's own
/// configuration declares, and the answer has to become a durable outgoing record under the account that email was
/// stored from. A substitute for any of those would assert the arrangement rather than the contract.
/// </para>
/// <para>
/// <b>What a call produces is a record, not a sent message.</b> The account this host configures submits to a host in
/// the reserved testing domain that resolves nowhere, so the delivery pass behind these calls reaches no mail server
/// and defers what it claimed. What each call answers with is the record as the call committed it, which is exactly
/// what the tool descriptions promise a caller — a durable record in <c>queued</c> — and what the sibling classes could
/// never establish.
/// </para>
/// <para>
/// Two calls, and two credentials spent from a bucket this collection shares. The rate limit is what a sibling class
/// measures, so this class makes one call per tool and asserts everything else about what came back.
/// </para>
/// </remarks>
[Collection(ComposedHostCollectionDefinition.Name)]
public sealed class ComposedMailResponseToolContractTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>The folder this class seeds into, which is the one the composed host's own configuration maps.</summary>
    private const string FolderAlias = OrchestrationContract.ComposedHostReadableFolderAlias;

    private const string AnsweredSubject = "mail-response-tool-contract-seeded";

    private const string AnsweredAuthorAddress = "correspondent@mailfathom.test";

    /// <summary>The one person the forward is sent to, whom the answered message never named.</summary>
    private static readonly string[] ForwardedTo = ["reader@mailfathom.test"];

    /// <summary>The whole path a client takes to answer stored mail, for each of the two ways of answering it.</summary>
    /// <remarks>
    /// The two calls share one seeded email deliberately: both answer the same message, so a difference between what
    /// they queue is a difference between the acts rather than between two arrangements. The identifiers the calls
    /// carry differ, because two answers to one message under one key would be one message.
    /// </remarks>
    [Fact]
    public async Task CallTool_AnsweringSeededMail_QueuesTheRecordEachToolContractPublishes()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var answeredEmailId = await this.SeedOneAnsweredMessageAsync(cancellationToken);

        using var client = await orchestration.OpenMcpEndpointClientAsync(cancellationToken);
        using var replyRequest = McpToolCall.Of(
            "reply_to_email",
            new
            {
                storedEmailId = answeredEmailId.Value.ToString(),
                audience = "senderOnly",
                plainTextBody = "Thank you, noted.",
                idempotencyKey = "mail-response-tool-contract-reply",
            },
            id: 1);
        using var forwardRequest = McpToolCall.Of(
            "forward_email",
            new
            {
                storedEmailId = answeredEmailId.Value.ToString(),
                to = ForwardedTo,
                plainTextBody = "Passing this on.",
                idempotencyKey = "mail-response-tool-contract-forward",
            },
            id: 2);

        // Act
        using var replyResponse = await client.SendAsync(replyRequest, cancellationToken);
        var replyBody = await replyResponse.Content.ReadAsStringAsync(cancellationToken);
        using var forwardResponse = await client.SendAsync(forwardRequest, cancellationToken);
        var forwardBody = await forwardResponse.Content.ReadAsStringAsync(cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, replyResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, forwardResponse.StatusCode);

        using var replyMessage = JsonDocument.Parse(McpToolCall.MessageIn(replyBody));
        using var forwardMessage = JsonDocument.Parse(McpToolCall.MessageIn(forwardBody));

        var reply = QueuedRecordIn(replyMessage, replyBody);
        var forward = QueuedRecordIn(forwardMessage, forwardBody);

        // The account is the one the answered email was stored from rather than anything the calls named, which is what
        // keeps a reply on the mailbox the correspondent has heard from.
        Assert.Equal(OrchestrationContract.ServedMailAccountId, reply.GetProperty("accountId").GetString());
        Assert.Equal(OrchestrationContract.ServedMailAccountId, forward.GetProperty("accountId").GetString());

        // Queued rather than sent, in the spelling the surface publishes: the answer is the record as the call
        // committed it, and no submission server has been spoken to for it.
        Assert.Equal("queued", reply.GetProperty("state").GetString());
        Assert.Equal("queued", forward.GetProperty("state").GetString());

        // The reply is addressed by the message it answers and the forward only by what the call named, which is the
        // difference between the two acts and the whole reason the anchor exists.
        Assert.Equal(1, reply.GetProperty("recipientCount").GetInt32());
        Assert.Equal(1, forward.GetProperty("recipientCount").GetInt32());

        // Two answers to one message are two records, since the calls carried different identities.
        Assert.NotEqual(
            reply.GetProperty("outgoingEmailId").GetString(),
            forward.GetProperty("outgoingEmailId").GetString());
        Assert.True(Guid.TryParse(reply.GetProperty("outgoingEmailId").GetString(), out _));
        Assert.True(reply.GetProperty("queuedAt").TryGetDateTimeOffset(out _));
    }

    /// <summary>Reads the structured record out of a successful tool call, failing on a call that was answered as an error.</summary>
    /// <remarks>
    /// A protocol-level error is reported inside a successful result rather than as a status code, so a call that
    /// failed would otherwise read as one that answered with no fields.
    /// </remarks>
    private static JsonElement QueuedRecordIn(JsonDocument message, string body)
    {
        var result = message.RootElement.GetProperty("result");

        Assert.False(
            result.TryGetProperty("isError", out var isError) && isError.GetBoolean(),
            $"The tool call was answered as an error: {body}");

        return result.GetProperty("structuredContent");
    }

    /// <summary>Stores one message and its raw MIME, which is what an answer to it is derived from.</summary>
    /// <remarks>
    /// The content is stored as well as the metadata, unlike the sibling class's seed: an answer quotes the message it
    /// answers, so an email whose bytes this deployment does not hold is refused rather than answered.
    /// </remarks>
    private async Task<StoredEmailId> SeedOneAnsweredMessageAsync(CancellationToken cancellationToken)
    {
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, uid: 9811);
        var rawMime = AnsweredRawMime();
        StoredEmailId? storedEmailId = null;

        var commitResult = await services.CommitAsync(
            async (scope, session, token) =>
            {
                storedEmailId = await scope.GetRequiredService<IEmailMetadataRepository>().UpsertMetadataAsync(
                    session, SyntheticMailAccount.Owner,
                    SyntheticEmail.RemoteMetadataOf(occurrenceId, AnsweredSubject, rawMime.Length),
                    SyntheticEmail.ExtractionFrom(
                        occurrenceId,
                        AnsweredSubject,
                        "The quarterly report is ready for review.",
                        AnsweredAuthorAddress,
                        SyntheticEmail.ReceivedAt,
                        OrchestrationContract.ComposedHostSendingAddress),
                    StoredEmailContentAvailability.Available,
                    token);

                await scope.GetRequiredService<IEmailContentStore>().SaveContentAsync(
                    session,
                    storedEmailId.Value,
                    occurrenceId,
                    PlacedEmailContent.InDatabase(rawMime),
                    token);
            },
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        return storedEmailId!.Value;
    }

    /// <summary>Builds the bytes the answered message is stored as, which the answer's quotation is rendered from.</summary>
    /// <remarks>
    /// Written out rather than padded to a length, because what an answer needs from these bytes is a message that
    /// parses: a <c>From</c> to address the reply to, a <c>Message-ID</c> to thread it by, and a body to quote.
    /// </remarks>
    private static ReadOnlyMemory<byte> AnsweredRawMime() => Encoding.ASCII.GetBytes(
        $"""
        From: {AnsweredAuthorAddress}
        To: {OrchestrationContract.ComposedHostSendingAddress}
        Subject: {AnsweredSubject}
        Message-ID: <{AnsweredSubject}@mailfathom.test>
        Date: Mon, 4 May 2026 08:30:00 +0000
        MIME-Version: 1.0
        Content-Type: text/plain; charset=us-ascii

        The quarterly report is ready for review.
        """.ReplaceLineEndings("\r\n")).AsMemory();
}
