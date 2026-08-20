// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Failures;
using MailFathom.Mcp.Tools.Results;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools.Drafts;

/// <summary>Covers what <c>delete_draft</c> tells a caller about the copy of the draft in the owner's own folder.</summary>
/// <remarks>
/// The settling pass reports in a vocabulary of its own — eight outcomes and a divergence beside them — and the caller
/// asks one question: will the owner still see that message. This is where the two meet, so every shape the pass can
/// hand over is read here rather than only the one a deployment mapping no drafts folder produces. It is driven from a
/// filing result rather than through the tool because what makes a copy unreachable is a mail server: producing one
/// belongs to <c>MailDraftFilerTests</c>, which covers each divergence against a substituted mailbox, and to the
/// integration suite against a real one.
/// </remarks>
public sealed class DeleteDraftToolResultTests
{
    private static readonly MailDraftId Draft =
        MailDraftId.Create(Guid.CreateVersion7(new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero)));

    /// <summary>A copy the removal could not take out is a message the owner still sees, whatever put it out of reach.</summary>
    [Theory]
    [InlineData(MailDraftDivergenceReason.AppendOutcomeUnknown)]
    [InlineData(MailDraftDivergenceReason.PlacementUnreported)]
    [InlineData(MailDraftDivergenceReason.DestinationChanged)]
    [InlineData(MailDraftDivergenceReason.FolderRecreated)]
    public void From_ADiscardedDraftWhoseCopyWasLeftStanding_PublishesItAsACopyLeftBehind(
        MailDraftDivergenceReason divergence)
    {
        // Arrange
        var settled = new MailDraftFilingResult(
            Draft,
            MailDraftFilingOutcome.Discarded,
            Failure: null,
            divergence);

        // Act
        var published = DeleteDraftToolResult.From(settled);

        // Assert
        Assert.Equal(Draft.ToString(), published.DraftId);
        Assert.Equal(DeletedDraftState.CopyLeftBehind, published.State);
    }

    /// <summary>A discard that left nothing standing is the plain answer, and it is the only ending that gets it.</summary>
    [Fact]
    public void From_ADiscardedDraftWithNothingLeftStanding_PublishesItAsDeleted()
    {
        // Arrange
        var settled = new MailDraftFilingResult(
            Draft,
            MailDraftFilingOutcome.Discarded,
            Failure: null,
            Divergence: null);

        // Act
        var published = DeleteDraftToolResult.From(settled);

        // Assert
        Assert.Equal(Draft.ToString(), published.DraftId);
        Assert.Equal(DeletedDraftState.Deleted, published.State);
    }

    /// <summary>Every other ending is the pass's own word for something unfinished, and says only that to a caller.</summary>
    [Theory]
    [InlineData(MailDraftFilingOutcome.AlreadySettled)]
    [InlineData(MailDraftFilingOutcome.Filed)]
    [InlineData(MailDraftFilingOutcome.Replaced)]
    [InlineData(MailDraftFilingOutcome.DestinationUnavailable)]
    [InlineData(MailDraftFilingOutcome.Diverged)]
    [InlineData(MailDraftFilingOutcome.OutcomeUnknown)]
    [InlineData(MailDraftFilingOutcome.Failed)]
    public void From_AnEndingThatDiscardedNothing_PublishesTheRemovalAsPending(MailDraftFilingOutcome outcome)
    {
        // Arrange
        var settled = new MailDraftFilingResult(
            Draft,
            outcome,
            MailFathomErrorCode.OutgoingEmailFilingFailedUnexpectedly,
            MailDraftDivergenceReason.DestinationChanged);

        // Act
        var published = DeleteDraftToolResult.From(settled);

        // Assert
        Assert.Equal(Draft.ToString(), published.DraftId);
        Assert.Equal(DeletedDraftState.Pending, published.State);
    }
}
