// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Domain.Failures;
using MailFathom.Mcp.Tools.Results;
using MailFathom.Mcp.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools.Drafts;

/// <summary>Covers what the <c>update_draft</c> tool owns: naming a draft this deployment holds and replacing its message.</summary>
/// <remarks>
/// An edit is a revision of one draft rather than a second draft, and the identity is what a caller has to be able to
/// rely on: the draft it saved is the draft it edits, sends, or gives up. What a caller may not reach is anything this
/// deployment did not write, which is why a foreign identifier and an identifier that names nothing are one answer.
/// </remarks>
public sealed class UpdateDraftToolTests
{
    private const string Recipient = "anna@example.test";

    /// <summary>The identifier does not change, the version does, and one draft is left rather than two.</summary>
    [Fact]
    public async Task UpdateDraftAsync_ADraftThisDeploymentHolds_ReplacesItsMessageUnderTheSameIdentity()
    {
        // Arrange
        var deployment = new DraftedMailDeployment();
        var saved = await deployment.SaveTool.SaveDraftAsync(
            "Shall we?",
            DraftedMailDeployment.ServedAccount,
            "Lunch",
            [Recipient],
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var updated = await deployment.UpdateTool.UpdateDraftAsync(
            saved.DraftId,
            "Shall we make it Friday?",
            DraftedMailDeployment.ServedAccount,
            "Lunch",
            [Recipient],
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(saved.DraftId, updated.DraftId);
        Assert.Equal(2, updated.Revision);
        Assert.Single(deployment.Drafts.Drafts);
    }

    /// <summary>An edit states the whole message, so a recipient the caller leaves out is no longer addressed.</summary>
    [Fact]
    public async Task UpdateDraftAsync_ARecipientLeftOut_LeavesTheDraftAddressedToWhoTheCallNamed()
    {
        // Arrange
        var deployment = new DraftedMailDeployment();
        var saved = await deployment.SaveTool.SaveDraftAsync(
            "Shall we?",
            DraftedMailDeployment.ServedAccount,
            "Lunch",
            [Recipient, "bartek@example.test"],
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var updated = await deployment.UpdateTool.UpdateDraftAsync(
            saved.DraftId,
            "Shall we?",
            DraftedMailDeployment.ServedAccount,
            "Lunch",
            [Recipient],
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, updated.RecipientCount);
    }

    /// <summary>A draft this deployment did not write is unreachable, which is the answer an identifier naming nothing gets.</summary>
    [Fact]
    public async Task UpdateDraftAsync_ADraftThisDeploymentDoesNotHold_IsRefusedAsADraftItDoesNotHold()
    {
        // Arrange
        var deployment = new DraftedMailDeployment();

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => deployment.UpdateTool.UpdateDraftAsync(
                Guid.CreateVersion7(DraftedMailDeployment.Moment).ToString(),
                "Shall we?",
                DraftedMailDeployment.ServedAccount,
                "Lunch",
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailDraftNotFound, refusal.ErrorCode);
        Assert.Empty(deployment.Drafts.Drafts);
    }

    /// <summary>Text that is no identifier at all is answered exactly as a draft nobody holds, so nothing is learnt by guessing.</summary>
    [Fact]
    public async Task UpdateDraftAsync_TextThatNamesNoDraft_IsRefusedAsADraftThisDeploymentDoesNotHold()
    {
        // Arrange
        var deployment = new DraftedMailDeployment();

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => deployment.UpdateTool.UpdateDraftAsync(
                "not-a-draft",
                "Shall we?",
                DraftedMailDeployment.ServedAccount,
                "Lunch",
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailDraftNotFound, refusal.ErrorCode);
    }

    /// <summary>The draft of one account cannot be edited as another's, so naming a draft is no way to reach a mailbox.</summary>
    [Fact]
    public async Task UpdateDraftAsync_AnAccountThatDoesNotHoldTheDraft_IsRefusedAndLeavesTheDraftAlone()
    {
        // Arrange
        var deployment = new DraftedMailDeployment();
        var saved = await deployment.SaveTool.SaveDraftAsync(
            "Shall we?",
            DraftedMailDeployment.ServedAccount,
            "Lunch",
            [Recipient],
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var refusal = await Assert.ThrowsAsync<MailAccountNotAccessibleException>(
            () => deployment.UpdateTool.UpdateDraftAsync(
                saved.DraftId,
                "Shall we?",
                "another-account",
                "Lunch",
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailAccountNotAccessible, refusal.ErrorCode);
        Assert.Equal(1, Assert.Single(deployment.Drafts.Drafts).Revision);
    }

    /// <summary>The shape rule is the one <c>save_draft</c> states, so an edit describing both shapes is refused too.</summary>
    [Fact]
    public async Task UpdateDraftAsync_AnEditDescribingAnAnswerAndAMessageOfItsOwn_IsRefused()
    {
        // Arrange
        var deployment = new DraftedMailDeployment();
        var saved = await deployment.SaveTool.SaveDraftAsync(
            "Shall we?",
            DraftedMailDeployment.ServedAccount,
            "Lunch",
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => deployment.UpdateTool.UpdateDraftAsync(
                saved.DraftId,
                "Thank you.",
                DraftedMailDeployment.ServedAccount,
                "Lunch",
                answeredEmailId: Guid.CreateVersion7(DraftedMailDeployment.Moment).ToString(),
                answering: DraftedAnswer.SenderOnly,
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.AuthoredMailFieldRefused, refusal.ErrorCode);
        Assert.Equal(1, Assert.Single(deployment.Drafts.Drafts).Revision);
    }
}
