// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Text.Json;
using MailFathom.AppHost;
using MailFathom.IntegrationTests.Orchestration;
using Xunit;

namespace MailFathom.IntegrationTests.Hosting;

/// <summary>Proves that a caller can write a draft over the real transport, edit it, send it, and be refused afterwards.</summary>
/// <remarks>
/// <para>
/// Nothing below the transport can prove this sequence. A draft and its message cross one transaction against the
/// database this deployment actually wrote them to, the identifier one call answers with has to be accepted by the
/// next three, and the promotion has to produce an ordinary outgoing record in the same schema a send writes — a
/// substitute for any of those would assert the arrangement rather than the contract.
/// </para>
/// <para>
/// The version is what makes an edit legible here rather than in a substitute: it is read back from the row the
/// revision wrote, so a deployment that appended a second draft instead of replacing the first would answer with a
/// version that never advanced.
/// </para>
/// <para>
/// Four calls, and four credentials spent from a bucket this collection shares. They are one chain deliberately: what
/// this class is about is that the draft's identity travels between the tools, so splitting them would prove each call
/// in isolation and the contract between them not at all. The fourth is the refusal, which is the one answer a caller
/// gets for every draft it may not act on and is worth reading over the wire because it arrives inside a successful
/// result rather than as a status code.
/// </para>
/// </remarks>
[Collection(ComposedHostCollectionDefinition.Name)]
public sealed class ComposedDraftToolContractTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>The one person the draft ends up addressed to, who never receives it because nothing here delivers.</summary>
    private static readonly string[] EditedTo = ["reader@mailfathom.test"];

    /// <summary>The two people the first version is addressed to, so that an edit visibly narrows the addressing.</summary>
    private static readonly string[] SavedTo = ["reader@mailfathom.test", "second-reader@mailfathom.test"];

    /// <summary>The whole path a client takes to write a draft, replace it, send it, and meet the refusal afterwards.</summary>
    [Fact]
    public async Task CallTool_ADraftThisDeploymentHolds_IsEditedInPlaceThenSentAndUnreachableAfterwards()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        using var client = await orchestration.OpenMcpEndpointClientAsync(cancellationToken);
        using var saveRequest = McpToolCall.Of(
            "save_draft",
            new
            {
                account = OrchestrationContract.ServedMailAccountId,
                subject = "draft-tool-contract",
                plainTextBody = "This one is written and not sent.",
                to = SavedTo,
            },
            id: 1);

        // Act
        using var saveResponse = await client.SendAsync(saveRequest, cancellationToken);
        var saveBody = await saveResponse.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, saveResponse.StatusCode);

        using var saveMessage = JsonDocument.Parse(McpToolCall.MessageIn(saveBody));
        var saved = RecordIn(saveMessage, saveBody);
        var draftId = saved.GetProperty("draftId").GetString();

        using var updateRequest = McpToolCall.Of(
            "update_draft",
            new
            {
                draftId,
                account = OrchestrationContract.ServedMailAccountId,
                subject = "draft-tool-contract",
                plainTextBody = "This one is written, edited, and then sent.",
                to = EditedTo,
            },
            id: 2);

        using var updateResponse = await client.SendAsync(updateRequest, cancellationToken);
        var updateBody = await updateResponse.Content.ReadAsStringAsync(cancellationToken);

        using var sendRequest = McpToolCall.Of("send_draft", new { draftId }, id: 3);
        using var sendResponse = await client.SendAsync(sendRequest, cancellationToken);
        var sendBody = await sendResponse.Content.ReadAsStringAsync(cancellationToken);

        using var deleteRequest = McpToolCall.Of("delete_draft", new { draftId }, id: 4);
        using var deleteResponse = await client.SendAsync(deleteRequest, cancellationToken);
        var deleteBody = await deleteResponse.Content.ReadAsStringAsync(cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, sendResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        using var updateMessage = JsonDocument.Parse(McpToolCall.MessageIn(updateBody));
        using var sendMessage = JsonDocument.Parse(McpToolCall.MessageIn(sendBody));
        using var deleteMessage = JsonDocument.Parse(McpToolCall.MessageIn(deleteBody));

        var updated = RecordIn(updateMessage, updateBody);
        var queued = RecordIn(sendMessage, sendBody);

        // The account is the one the deployment serves, and the draft is held rather than filed: this account maps no
        // folder to the drafts role, so there is nowhere to put a copy and the draft is kept here alone.
        Assert.Equal(OrchestrationContract.ServedMailAccountId, saved.GetProperty("accountId").GetString());
        Assert.Equal("held", saved.GetProperty("state").GetString());
        Assert.Equal(1, saved.GetProperty("revision").GetInt32());
        Assert.Equal(SavedTo.Length, saved.GetProperty("recipientCount").GetInt32());

        // One draft rather than two: the identity does not change and the version advances, which is what an edit is.
        Assert.Equal(draftId, updated.GetProperty("draftId").GetString());
        Assert.Equal(2, updated.GetProperty("revision").GetInt32());

        // An edit states the whole message, so the recipient the second call left out is no longer addressed.
        Assert.Equal(EditedTo.Length, updated.GetProperty("recipientCount").GetInt32());

        // The promotion answers with the send's own record, queued rather than sent: the answer is that record as
        // the call committed it, and the submission host this deployment would offer it to resolves nowhere.
        Assert.Equal(OrchestrationContract.ServedMailAccountId, queued.GetProperty("accountId").GetString());
        Assert.Equal("queued", queued.GetProperty("state").GetString());
        Assert.Equal(EditedTo.Length, queued.GetProperty("recipientCount").GetInt32());
        Assert.True(Guid.TryParse(queued.GetProperty("outgoingEmailId").GetString(), out _));

        // A promoted draft is refused rather than given up, in the shape every unreachable draft is refused in.
        var refusal = deleteMessage.RootElement.GetProperty("result");

        Assert.True(
            refusal.GetProperty("isError").GetBoolean(),
            $"Deleting a promoted draft was answered as a success: {deleteBody}");
        Assert.Contains(
            "53008",
            refusal.GetProperty("content")[0].GetProperty("text").GetString(),
            StringComparison.Ordinal);
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
