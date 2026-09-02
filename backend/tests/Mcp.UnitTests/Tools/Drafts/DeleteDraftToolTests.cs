// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Domain.Failures;
using MailFathom.Mcp.Tools.Drafts;
using MailFathom.Mcp.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools.Drafts;

/// <summary>Covers what the <c>delete_draft</c> tool owns: naming a draft this deployment wrote and giving it up.</summary>
/// <remarks>
/// The identifier is the whole of what a caller may reach with, and it names a record MailFathom wrote — so a message
/// the owner drafted in their own mail client is not refused by a check but by there being nothing here that names it.
/// A second call meets the same answer, which is what makes asking twice safe.
/// </remarks>
public sealed class DeleteDraftToolTests
{
    /// <summary>The draft is gone and the identifier that named it names nothing afterwards.</summary>
    [Fact]
    public async Task DeleteDraftAsync_ADraftThisDeploymentHolds_GivesItUpAndHoldsNothingForIt()
    {
        // Arrange
        var deployment = new DraftedMailDeployment();
        var saved = await deployment.SaveTool.SaveDraftAsync(
            "Shall we?",
            DraftedMailDeployment.ServedAccount,
            "Lunch",
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var result = await deployment.DeleteTool.DeleteDraftAsync(
            saved.DraftId,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(saved.DraftId, result.DraftId);
        Assert.Equal(DeletedDraftState.Deleted, result.State);
        Assert.Empty(deployment.Drafts.Drafts);
    }

    /// <summary>Asking twice is safe, and the second call meets the answer a draft nobody holds gets.</summary>
    [Fact]
    public async Task DeleteDraftAsync_ADraftAlreadyGivenUp_IsRefusedAsADraftThisDeploymentDoesNotHold()
    {
        // Arrange
        var deployment = new DraftedMailDeployment();
        var saved = await deployment.SaveTool.SaveDraftAsync(
            "Shall we?",
            DraftedMailDeployment.ServedAccount,
            "Lunch",
            cancellationToken: TestContext.Current.CancellationToken);

        await deployment.DeleteTool.DeleteDraftAsync(saved.DraftId, TestContext.Current.CancellationToken);

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => deployment.DeleteTool.DeleteDraftAsync(
                saved.DraftId,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailDraftNotFound, refusal.ErrorCode);
    }

    /// <summary>A draft this deployment did not write is unreachable, and text that names no draft meets the same answer.</summary>
    [Theory]
    [InlineData("not-a-draft")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task DeleteDraftAsync_ADraftThisDeploymentDidNotWrite_IsRefusedAsADraftItDoesNotHold(string draftId)
    {
        // Arrange
        var deployment = new DraftedMailDeployment();

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => deployment.DeleteTool.DeleteDraftAsync(draftId, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailDraftNotFound, refusal.ErrorCode);
    }
}
