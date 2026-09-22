// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.UnitTests.Discovery.Presentation;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Application.UnitTests.Agent.Conversations;

/// <summary>Covers what the entries of a conversation say when they are read together.</summary>
/// <remarks>
/// This is where the record stops being an order of events and becomes turns somebody reads: which turn a block
/// belongs to, which line of a run stands, and — the one that matters most — where each offer the agent made now
/// stands, which nothing stores and the reading derives.
/// </remarks>
public sealed class AgentConversationTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 9, 21, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Compose_AQuestionAndTheAnswerToIt_ReadsBothTurnsInTheOrderTheyWereWritten()
    {
        // Arrange
        var asked = AgentMessageId.New();
        var answered = AgentMessageId.New();
        var entries = AgentConversationExample.Written(
            AgentConversationExample.Question(asked, "Where did we land on the price?"),
            new AgentAnswerStarted(answered),
            new AgentBlockComposed(answered, AgentConversationExample.Reading()),
            new AgentAnswerEnded(answered, AgentAnswerOutcome.Completed));

        // Act
        var conversation = Compose(entries);

        // Assert
        Assert.Collection(
            conversation.Messages,
            first =>
            {
                Assert.Equal(asked, first.Id);
                Assert.Equal(AgentMessageAuthor.Person, first.Author);
                Assert.Equal("Where did we land on the price?", first.Text?.Value);
                Assert.Equal(AgentScopeKind.Mailbox, first.Scope?.Kind);
                Assert.Equal(1, first.WrittenAt);
            },
            second =>
            {
                Assert.Equal(answered, second.Id);
                Assert.Equal(AgentMessageAuthor.Agent, second.Author);
                Assert.Null(second.Text);
                Assert.Single(second.Blocks);
                Assert.Equal(AgentAnswerOutcome.Completed, second.Outcome);
            });
    }

    /// <summary>A run reports one changing line, so the reading keeps the newest and nothing older.</summary>
    [Fact]
    public void Compose_ARunThatReportedSeveralLines_KeepsTheMostRecentOne()
    {
        // Arrange
        var answered = AgentMessageId.New();
        var entries = AgentConversationExample.Written(
            new AgentAnswerStarted(answered),
            new AgentStatusReported(answered, PresentationText.Create("reading the attachments")),
            new AgentStatusReported(answered, PresentationText.Create("checking the calendar")));

        // Act
        var conversation = Compose(entries);

        // Assert
        Assert.Equal("checking the calendar", Assert.Single(conversation.Messages).Status?.Value);
    }

    /// <summary>An answer still being composed has no outcome, which is how a reader tells it from one that finished.</summary>
    [Fact]
    public void Compose_AnAnswerStillBeingComposed_CarriesNoOutcome()
    {
        // Arrange
        var answered = AgentMessageId.New();
        var entries = AgentConversationExample.Written(
            new AgentAnswerStarted(answered),
            new AgentBlockComposed(answered, AgentConversationExample.Reading()));

        // Act
        var conversation = Compose(entries);

        // Assert
        Assert.Null(Assert.Single(conversation.Messages).Outcome);
    }

    /// <summary>A run somebody stopped keeps everything it had composed, and the record says it was cut short.</summary>
    [Fact]
    public void Compose_ARunSomebodyStopped_KeepsWhatArrivedAndSaysItWasCutShort()
    {
        // Arrange
        var answered = AgentMessageId.New();
        var said = AgentMessageId.New();
        var entries = AgentConversationExample.Written(
            new AgentAnswerStarted(answered),
            new AgentBlockComposed(answered, AgentConversationExample.Reading()),
            new AgentAnswerEnded(answered, AgentAnswerOutcome.Stopped),
            AgentConversationExample.Note(said, "The run was stopped. What arrived stays."));

        // Act
        var conversation = Compose(entries);

        // Assert
        Assert.Collection(
            conversation.Messages,
            answer =>
            {
                Assert.Single(answer.Blocks);
                Assert.Equal(AgentAnswerOutcome.Stopped, answer.Outcome);
            },
            note =>
            {
                Assert.Equal(AgentMessageAuthor.Agent, note.Author);
                Assert.Equal("The run was stopped. What arrived stays.", note.Text?.Value);
                Assert.Empty(note.Blocks);
            });
    }

    /// <summary>The record states a scope on the turn that asked, and every turn after it was written under that one.</summary>
    [Fact]
    public void Compose_TurnsFollowingAQuestionAboutAThread_AreReadUnderThatThreadUntilAnotherQuestionStatesOne()
    {
        // Arrange
        var thread = EmailThreadId.Create(Guid.NewGuid());
        var asked = AgentMessageId.New();
        var answered = AgentMessageId.New();
        var askedAgain = AgentMessageId.New();
        var entries = AgentConversationExample.Written(
            new AgentMessageWritten(
                asked,
                AgentMessageAuthor.Person,
                PresentationText.Create("What is outstanding here?"),
                AgentMessageScope.Thread(thread)),
            new AgentAnswerStarted(answered),
            new AgentAnswerEnded(answered, AgentAnswerOutcome.Completed),
            AgentConversationExample.Question(askedAgain, "And across everything?"));

        // Act
        var conversation = Compose(entries);

        // Assert
        Assert.Equal(
            [AgentScopeKind.Thread, AgentScopeKind.Thread, AgentScopeKind.Mailbox],
            conversation.Messages.Select(message => message.Scope?.Kind));
        Assert.Equal(thread.Value, conversation.Messages[1].Scope?.Subject);
    }

    /// <summary>Nothing has stated one yet, so there is nothing to read a turn under.</summary>
    [Fact]
    public void Compose_AnAnswerNoQuestionPreceded_CarriesNoScope()
    {
        // Arrange
        var answered = AgentMessageId.New();
        var entries = AgentConversationExample.Written(new AgentAnswerStarted(answered));

        // Act
        var conversation = Compose(entries);

        // Assert
        Assert.Null(Assert.Single(conversation.Messages).Scope);
    }

    [Fact]
    public void Compose_AnOfferNothingAnswered_StandsPending()
    {
        // Arrange
        var answered = AgentMessageId.New();
        var entries = AgentConversationExample.Written(
            new AgentAnswerStarted(answered),
            new AgentActionProposed(answered, AgentConversationExample.Actionable(), AgentConversationExample.Act()));

        // Act
        var conversation = Compose(entries);

        // Assert
        var offer = Assert.Single(Assert.Single(conversation.Messages).ProposedActions);
        Assert.Equal(AgentProposalState.Pending, offer.State);
        Assert.Equal(2, offer.ProposedAt);
    }

    /// <summary>Accepted and then carried out unsuccessfully is its own state, and the newest answer is the one that stands.</summary>
    [Fact]
    public void Compose_AnOfferAcceptedAndThenFailed_StandsFailed()
    {
        // Arrange
        var answered = AgentMessageId.New();
        var entries = AgentConversationExample.Written(
            new AgentAnswerStarted(answered),
            new AgentActionProposed(answered, AgentConversationExample.Actionable(), AgentConversationExample.Act()),
            new AgentProposalResolved(2, AgentProposalState.Accepted),
            new AgentProposalResolved(2, AgentProposalState.Failed));

        // Act
        var conversation = Compose(entries);

        // Assert
        var offer = Assert.Single(Assert.Single(conversation.Messages).ProposedActions);
        Assert.Equal(AgentProposalState.Failed, offer.State);
    }

    /// <summary>The same offer made twice carries two outcomes, because each is named by its own place.</summary>
    [Fact]
    public void Compose_TheSameOfferMadeTwice_CarriesAnOutcomeOfItsOwnEachTime()
    {
        // Arrange
        var answered = AgentMessageId.New();
        var again = AgentMessageId.New();
        var entries = AgentConversationExample.Written(
            new AgentAnswerStarted(answered),
            new AgentActionProposed(answered, AgentConversationExample.Actionable(), AgentConversationExample.Act()),
            new AgentProposalResolved(2, AgentProposalState.Declined),
            new AgentAnswerStarted(again),
            new AgentActionProposed(again, AgentConversationExample.Actionable(), AgentConversationExample.Act()),
            new AgentProposalResolved(5, AgentProposalState.Accepted));

        // Act
        var conversation = Compose(entries);

        // Assert
        Assert.Collection(
            conversation.Messages,
            first => Assert.Equal(AgentProposalState.Declined, Assert.Single(first.ProposedActions).State),
            second => Assert.Equal(AgentProposalState.Accepted, Assert.Single(second.ProposedActions).State));
    }

    [Fact]
    public void Compose_TheSourcesAnAnswerDeclared_ReadBackOnTheTurnThatDeclaredThem()
    {
        // Arrange
        var answered = AgentMessageId.New();
        var entries = AgentConversationExample.Written(
            new AgentAnswerStarted(answered),
            new AgentCitationDeclared(answered, PresentationPlanExample.Citations()[0]),
            new AgentBlockComposed(answered, AgentConversationExample.Reading()));

        // Act
        var conversation = Compose(entries);

        // Assert
        Assert.Equal(
            PresentationPlanExample.FirstCitation,
            Assert.Single(Assert.Single(conversation.Messages).Citations).Id);
    }

    [Fact]
    public void Compose_NoEntriesAtAll_ReadsAConversationWithNoTurns()
    {
        // Act
        var conversation = Compose([]);

        // Assert
        Assert.Empty(conversation.Messages);
        Assert.Equal(StartedAt, conversation.StartedAt);
    }

    /// <summary>An entry naming a turn nothing before it opened is refused rather than placed in a turn of its own.</summary>
    [Fact]
    public void Compose_ABlockWhoseTurnTheEntriesNeverOpened_IsRefused()
    {
        // Arrange
        var answered = AgentMessageId.New();
        var entries = AgentConversationExample.Written(
            new AgentBlockComposed(answered, AgentConversationExample.Reading()));

        // Act, Assert
        Assert.Throws<ArgumentException>(() => Compose(entries));
    }

    [Fact]
    public void Compose_AnAnswerToAnOfferTheEntriesNeverMade_IsRefused()
    {
        // Arrange
        var entries = AgentConversationExample.Written(
            new AgentProposalResolved(7, AgentProposalState.Accepted));

        // Act, Assert
        Assert.Throws<ArgumentException>(() => Compose(entries));
    }

    [Fact]
    public void Compose_OneTurnOpenedTwice_IsRefused()
    {
        // Arrange
        var answered = AgentMessageId.New();
        var entries = AgentConversationExample.Written(
            new AgentAnswerStarted(answered),
            new AgentAnswerStarted(answered));

        // Act, Assert
        Assert.Throws<ArgumentException>(() => Compose(entries));
    }

    /// <summary>What a run did on the way to its answer and every summary taken of the conversation are the technical history, which the reading a person sees passes over.</summary>
    [Fact]
    public void Compose_ToolTrafficAndACompactionAmongTheTurns_ReadsOnlyTheTurns()
    {
        // Arrange
        var asked = AgentMessageId.New();
        var answered = AgentMessageId.New();
        var entries = AgentConversationExample.Written(
            AgentConversationExample.Question(asked, "Where did we land on the price?"),
            new AgentAnswerStarted(answered),
            new AgentConversationCompacted(answered, Through: 1, "They asked about the price.", Carried: []),
            new AgentToolCalled(answered, "call-1", "search_mail", "{}"),
            new AgentToolAnswered(answered, "call-1", "[]"),
            new AgentModelCharged(answered, SentCharacters: 900, InputTokens: 250),
            new AgentBlockComposed(answered, AgentConversationExample.Reading()),
            new AgentAnswerEnded(answered, AgentAnswerOutcome.Completed));

        // Act
        var conversation = Compose(entries);

        // Assert
        Assert.Equal([asked, answered], conversation.Messages.Select(static message => message.Id));
        Assert.Equal(AgentAnswerOutcome.Completed, conversation.Messages[^1].Outcome);
    }

    private static AgentConversation Compose(IReadOnlyList<AgentConversationEntry> entries) =>
        AgentConversation.Compose(AgentConversationExample.Conversation, "The price thread", StartedAt, entries);
}
