// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Domain.Failures;
using MailFathom.Mcp.Failures;
using MailFathom.Mcp.Tools.Outgoing;
using MailFathom.Mcp.Tools.Results;
using MailFathom.Mcp.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools.Drafts;

/// <summary>Covers what the <c>send_draft</c> tool owns: promoting one draft and answering with the send it became.</summary>
/// <remarks>
/// The answer is the send's own rather than a draft's, because what the promotion produces is an ordinary outgoing
/// record — and the caller has to read <c>queued</c> rather than anything that could be reported as a delivery. What no
/// argument here decides is the message: everything about it is the draft's.
/// </remarks>
public sealed class SendDraftToolTests
{
    private const string Recipient = "anna@example.test";

    /// <summary>Nothing has been transmitted when the answer is produced, which is the one thing the result has to say.</summary>
    [Fact]
    public async Task SendDraftAsync_AnAddressedDraft_QueuesTheRecordItBecameRatherThanADelivery()
    {
        // Arrange
        var deployment = new DraftedMailDeployment();
        var saved = await SavedDraftAsync(deployment, [Recipient]);

        // Act
        var result = await deployment.SendTool.SendDraftAsync(
            saved.DraftId,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SendEmailState.Queued, result.State);
        Assert.Equal(DraftedMailDeployment.ServedAccount, result.AccountId);
        Assert.Equal(1, result.RecipientCount);
        Assert.True(Guid.TryParse(result.OutgoingEmailId, out _));
    }

    /// <summary>The draft is the identity, so promoting it twice answers with the record the first call wrote.</summary>
    [Fact]
    public async Task SendDraftAsync_TheSameDraftTwice_AnswersWithOneRecordRatherThanQueueingASecondMessage()
    {
        // Arrange
        var deployment = new DraftedMailDeployment();
        var saved = await SavedDraftAsync(deployment, [Recipient]);

        // Act
        var first = await deployment.SendTool.SendDraftAsync(
            saved.DraftId,
            TestContext.Current.CancellationToken);
        var second = await deployment.SendTool.SendDraftAsync(
            saved.DraftId,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(first.OutgoingEmailId, second.OutgoingEmailId);
    }

    /// <summary>The draft stands until its message has actually been delivered, so a promotion leaves it where it is.</summary>
    [Fact]
    public async Task SendDraftAsync_ADraftItQueued_LeavesTheDraftNamingTheSendItBecame()
    {
        // Arrange
        var deployment = new DraftedMailDeployment();
        var saved = await SavedDraftAsync(deployment, [Recipient]);

        // Act
        var result = await deployment.SendTool.SendDraftAsync(
            saved.DraftId,
            TestContext.Current.CancellationToken);

        // Assert
        var promoted = Assert.Single(deployment.Drafts.Drafts);
        Assert.Equal(result.OutgoingEmailId, promoted.PromotedTo?.ToString());
        Assert.False(promoted.IsDiscarded);
    }

    /// <summary>A draft addressed to nobody is an ordinary draft and no message, so the refusal is about the draft.</summary>
    [Fact]
    public async Task SendDraftAsync_ADraftAddressedToNobody_IsRefusedAndQueuesNothing()
    {
        // Arrange
        var deployment = new DraftedMailDeployment();
        var saved = await SavedDraftAsync(deployment, recipients: null);

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => deployment.SendTool.SendDraftAsync(
                saved.DraftId,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailDraftNotAddressed, refusal.ErrorCode);
        Assert.Null(Assert.Single(deployment.Drafts.Drafts).PromotedTo);
    }

    /// <summary>A draft this deployment does not hold is refused in the shape every unknown draft identifier produces.</summary>
    [Theory]
    [InlineData("not-a-draft")]
    [InlineData("2f2f8a53-2f0e-7a3f-9d0b-2b0c2c9a0f11")]
    public async Task SendDraftAsync_ADraftThisDeploymentDoesNotHold_IsRefusedAsADraftItDoesNotHold(string draftId)
    {
        // Arrange
        var deployment = new DraftedMailDeployment();

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => deployment.SendTool.SendDraftAsync(draftId, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailDraftNotFound, refusal.ErrorCode);
    }

    /// <summary>Every refusal these tools raise is one a caller reads, rather than one the boundary collapses.</summary>
    /// <remarks>
    /// It is the claim the four draft tools rest on and the one nothing else here would catch. The reporter publishes a
    /// failure whose code belongs to the boundary category and answers every other with the undiagnosed code, so a
    /// refusal allocated anywhere else reaches a client as "something went wrong" — and a caller told that about an
    /// unaddressed draft cannot know the remedy is <c>update_draft</c>. The codes are asserted rather than the
    /// messages, because the category is what the boundary reads.
    /// </remarks>
    [Fact]
    public async Task SendDraftAsync_EveryRefusalItRaises_IsOneTheBoundaryPublishesRatherThanCollapses()
    {
        // Arrange
        var deployment = new DraftedMailDeployment();
        var saved = await SavedDraftAsync(deployment, recipients: null);

        // Act
        var unaddressed = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => deployment.SendTool.SendDraftAsync(saved.DraftId, TestContext.Current.CancellationToken));
        var unknown = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => deployment.SendTool.SendDraftAsync("not-a-draft", TestContext.Current.CancellationToken));

        // Assert
        Assert.True(McpToolFailure.CanDescribeToClient(unaddressed), unaddressed.ErrorCode.ToString());
        Assert.True(McpToolFailure.CanDescribeToClient(unknown), unknown.ErrorCode.ToString());
    }

    /// <summary>Writes one draft to promote, which is the arrangement every test here begins from.</summary>
    private static Task<SaveDraftToolResult> SavedDraftAsync(
        DraftedMailDeployment deployment,
        IReadOnlyList<string>? recipients) =>
        deployment.SaveTool.SaveDraftAsync(
            "Shall we?",
            DraftedMailDeployment.ServedAccount,
            "Lunch on Thursday",
            recipients,
            cancellationToken: TestContext.Current.CancellationToken);
}
