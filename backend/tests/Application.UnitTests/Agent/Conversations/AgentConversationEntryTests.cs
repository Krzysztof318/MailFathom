// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Discovery.Presentation;
using Xunit;

namespace MailFathom.Application.UnitTests.Agent.Conversations;

/// <summary>Covers what each kind of entry refuses to be written as.</summary>
/// <remarks>
/// The division between a block and an offer is the one that carries weight: the store tells them apart by the kind
/// column alone, so a block the reader acts on written as a reading would be an offer nothing could ever answer, and a
/// reading written as an offer would be a control drawn over something with no control.
/// </remarks>
public sealed class AgentConversationEntryTests
{
    [Fact]
    public void Constructor_ABlockTheReaderActsOnComposedAsAReading_IsRefused()
    {
        // Arrange
        var message = AgentMessageId.New();

        // Act, Assert
        Assert.Throws<ArgumentException>(() => new AgentBlockComposed(message, AgentConversationExample.Actionable()));
    }

    [Fact]
    public void Constructor_ABlockTheReaderOnlyReadsProposedAsAnOffer_IsRefused()
    {
        // Arrange
        var message = AgentMessageId.New();

        // Act, Assert
        Assert.Throws<ArgumentException>(() => new AgentActionProposed(message, AgentConversationExample.Reading()));
    }

    /// <summary>A turn says something, and the struct default is what saying nothing looks like at compile time.</summary>
    /// <remarks>
    /// Refused where it is written rather than where it is stored: unchecked, the unspecified default travels as far as
    /// the serializer, which raises a <c>JsonException</c> out of the store — a failure naming neither the entry nor the
    /// member, a long way from whoever composed it.
    /// </remarks>
    [Fact]
    public void Constructor_AMessageSayingNothing_IsRefused()
    {
        // Arrange
        var message = AgentMessageId.New();

        // Act, Assert
        Assert.Throws<ArgumentException>(
            () => new AgentMessageWritten(message, AgentMessageAuthor.Person, default, AgentMessageScope.Mailbox()));
    }

    [Fact]
    public void Constructor_ARunReportingNothing_IsRefused()
    {
        // Arrange
        var message = AgentMessageId.New();

        // Act, Assert
        Assert.Throws<ArgumentException>(() => new AgentStatusReported(message, default));
    }

    [Fact]
    public void Constructor_AMessageWrittenByAnAuthorTheSetDoesNotDeclare_IsRefused()
    {
        // Arrange
        var message = AgentMessageId.New();

        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AgentMessageWritten(message, (AgentMessageAuthor)42, PresentationText.Create("Anything."), null));
    }

    /// <summary>A source is a reference, so the one value the language always admits is the one nothing can cite.</summary>
    [Fact]
    public void Constructor_ACitationOfNothing_IsRefused()
    {
        // Arrange
        var message = AgentMessageId.New();

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new AgentCitationDeclared(message, null!));
    }

    /// <summary>Pending is the state of an offer nothing has been recorded against, so recording it records nothing.</summary>
    [Fact]
    public void Constructor_AnOfferMovedToPending_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => new AgentProposalResolved(1, AgentProposalState.Pending));
    }

    [Theory]
    [InlineData(AgentProposalState.Accepted)]
    [InlineData(AgentProposalState.Failed)]
    [InlineData(AgentProposalState.Declined)]
    public void Constructor_AnOfferMovedToARecordableState_CarriesIt(AgentProposalState state)
    {
        // Act
        var resolved = new AgentProposalResolved(4, state);

        // Assert
        Assert.Equal(state, resolved.State);
        Assert.Equal(4, resolved.ProposedAt);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void Constructor_AnOfferAnsweredAtAPlaceNothingWasWrittenAt_IsRefused(long proposedAt)
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AgentProposalResolved(proposedAt, AgentProposalState.Declined));
    }

    [Fact]
    public void Constructor_AnAnswerEndingInAnOutcomeTheSetDoesNotDeclare_IsRefused()
    {
        // Arrange
        var message = AgentMessageId.New();

        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgentAnswerEnded(message, (AgentAnswerOutcome)42));
    }

    /// <summary>The three the store reads off an entry to decide what it may do to the conversation.</summary>
    [Fact]
    public void ComposedInto_TheEntriesAnAnswerIsMadeOf_NameTheAnswerTheyBelongTo()
    {
        // Arrange
        var message = AgentMessageId.New();

        // Act
        AgentConversationEntry[] entries =
        [
            new AgentStatusReported(message, PresentationText.Create("reading the attachments")),
            new AgentBlockComposed(message, AgentConversationExample.Reading()),
            new AgentActionProposed(message, AgentConversationExample.Actionable()),
            new AgentAnswerEnded(message, AgentAnswerOutcome.Completed),
        ];

        // Assert
        Assert.All(entries, entry => Assert.Equal(message, entry.ComposedInto));
    }

    [Fact]
    public void ComposedInto_AQuestionAndAnAnswerToAnOffer_NameNoAnswer()
    {
        // Arrange
        var question = AgentConversationExample.Question(AgentMessageId.New(), "Where did we land on the price?");

        // Act, Assert
        Assert.Null(question.ComposedInto);
        Assert.Null(new AgentProposalResolved(2, AgentProposalState.Accepted).ComposedInto);
    }

    [Fact]
    public void OpensTheAnswer_AnAnswerBeginning_IsTheOnlyEntryThatOpensOne()
    {
        // Arrange
        var message = AgentMessageId.New();

        // Act, Assert
        Assert.True(new AgentAnswerStarted(message).OpensTheAnswer);
        Assert.False(new AgentAnswerEnded(message, AgentAnswerOutcome.Stopped).OpensTheAnswer);
        Assert.False(AgentConversationExample.Note(message, "The run was stopped.").OpensTheAnswer);
    }

    [Fact]
    public void EndsTheAnswer_AnAnswerEnding_IsTheOnlyEntryThatEndsOne()
    {
        // Arrange
        var message = AgentMessageId.New();

        // Act, Assert
        Assert.True(new AgentAnswerEnded(message, AgentAnswerOutcome.Stopped).EndsTheAnswer);
        Assert.False(new AgentAnswerStarted(message).EndsTheAnswer);
        Assert.False(new AgentBlockComposed(message, AgentConversationExample.Reading()).EndsTheAnswer);
    }
}
