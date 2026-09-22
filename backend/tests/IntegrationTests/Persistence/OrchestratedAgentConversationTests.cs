// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Presentation.Blocks;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves that an Agent conversation written on one replica is read, stopped, answered, and removed from another.</summary>
/// <remarks>
/// <para>
/// This is the property the store exists for and the whole of what a durable conversation buys. Every statement it has
/// decides something in one composed command — the place advanced inside the insert, the answer a part may be written
/// into, the ceiling on a conversation, the cursor read that falls back to the beginning, and where a proposal stands
/// before it is allowed to move — so a fake reproducing them in a dictionary is a different mechanism in a different
/// process, which is exactly the per-replica arrangement a durable record replaces.
/// </para>
/// <para>
/// Two hosts rather than two calls on one, wherever the claim is about a second replica. Each composes its own service
/// graph over the one orchestrated database, which is what a second replica is.
/// </para>
/// <para>
/// A user of this class's own, provisioned and erased around each case, because both tables cascade from the user
/// record and the suite shares one database.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedAgentConversationTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>How many callers answer one offer at once, enough that a caller reliably loses the race to accept it.</summary>
    private const int ContendingCallers = 8;

    /// <summary>What the composed block says, which is invented here rather than drawn from anything a mailbox holds.</summary>
    private const string AnswerText = "Nothing in the mailbox answers the question.";

    /// <summary>The instant every statement here is stamped with, so nothing in the class reads a clock.</summary>
    private static readonly DateTimeOffset Instant = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A conversation one replica wrote is read whole by another, in order and with what it composed.</summary>
    /// <remarks>
    /// The payload crosses a column through a polymorphic serializer, so this is also where an entry that lost its
    /// discriminator would be found: a conversation read back as a list of base entries renders as nothing on a screen
    /// and fails no unit test. A composed block nests a second discriminator inside the first, which is why one is in
    /// the conversation rather than only the entries that carry no block.
    /// </remarks>
    [Fact]
    public async Task ReadAsync_AConversationAnotherReplicaWrote_ReadsEveryEntryInTheOrderItWasWritten()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var composingHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await using var readingHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(composingHost, user, cancellationToken);

        try
        {
            // Arrange
            var composing = await StoreOfAsync(composingHost, cancellationToken);
            var reading = await StoreOfAsync(readingHost, cancellationToken);
            var conversation = await StartedAsync(composing, user, cancellationToken);
            var answer = AgentMessageId.New();

            await composing.AppendAsync(conversation, UserId.Create(user), Question(), Instant, cancellationToken);
            await composing.AppendAsync(conversation, UserId.Create(user), new AgentAnswerStarted(answer), Instant, cancellationToken);
            await composing.AppendAsync(conversation, UserId.Create(user), Composed(answer), Instant, cancellationToken);
            await composing.AppendAsync(
                conversation,
                UserId.Create(user),
                new AgentAnswerEnded(answer, AgentAnswerOutcome.Completed),
                Instant,
                cancellationToken);

            await composing.TrySetTitleAsync(conversation, UserId.Create(user), PresentationText.Create("The price thread"), cancellationToken);

            // Act
            var read = await reading.ReadAsync(conversation, UserId.Create(user), afterSequence: 0, limit: 50, cancellationToken);

            // Assert
            Assert.NotNull(read);
            Assert.Equal("The price thread", read.Title);
            Assert.False(read.Composing);
            Assert.False(read.MoreFollows);
            Assert.Equal([1L, 2L, 3L, 4L], read.Entries.Select(written => written.Sequence));
            Assert.Equal(
                [
                    AgentMessageWritten.Kind,
                    AgentAnswerStarted.Kind,
                    AgentBlockComposed.Kind,
                    AgentAnswerEnded.Kind,
                ],
                read.Entries.Select(written => written.EntryName));
            Assert.All(read.Entries, written => Assert.Equal(conversation, written.ConversationId));
            Assert.Equal(
                AnswerText,
                Assert.IsType<AnswerBlock>(Assert.IsType<AgentBlockComposed>(read.Entries[2]).Block).Text.Value);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(composingHost, user);
        }
    }

    /// <summary>A reader's cursor hands back the tail alone, and one naming a place this conversation never reached reads it whole.</summary>
    /// <remarks>
    /// Both halves are one <c>CASE</c> inside the read's own statement rather than a decision a caller takes, which is
    /// what makes a cursor left over from another conversation read as the beginning rather than as an empty answer a
    /// client would wait on forever.
    /// </remarks>
    [Fact]
    public async Task ReadAsync_FromACursor_ReturnsTheTailAndReadsAnUnreachedCursorFromTheBeginning()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(host, user, cancellationToken);

        try
        {
            // Arrange
            var store = await StoreOfAsync(host, cancellationToken);
            var conversation = await StartedAsync(store, user, cancellationToken);
            var answer = AgentMessageId.New();

            await store.AppendAsync(conversation, UserId.Create(user), Question(), Instant, cancellationToken);
            await store.AppendAsync(conversation, UserId.Create(user), new AgentAnswerStarted(answer), Instant, cancellationToken);
            await store.AppendAsync(conversation, UserId.Create(user), Composed(answer), Instant, cancellationToken);

            // Act
            var tail = await store.ReadAsync(conversation, UserId.Create(user), afterSequence: 2, limit: 50, cancellationToken);
            var unreached = await store.ReadAsync(conversation, UserId.Create(user), afterSequence: 99, limit: 50, cancellationToken);
            var somebodyElses = await store.ReadAsync(conversation, UserId.Create(Guid.NewGuid()), afterSequence: 0, limit: 50, cancellationToken);

            // Assert
            Assert.Equal([3L], tail?.Entries.Select(written => written.Sequence));
            Assert.Equal([1L, 2L, 3L], unreached?.Entries.Select(written => written.Sequence));
            Assert.True(tail?.Composing);
            Assert.Null(somebodyElses);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(host, user);
        }
    }

    /// <summary>A bounded read hands back the page it was asked for and says the conversation holds more.</summary>
    [Fact]
    public async Task ReadAsync_AConversationLongerThanTheLimit_ReturnsThePageAndSaysMoreFollows()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(host, user, cancellationToken);

        try
        {
            // Arrange
            var store = await StoreOfAsync(host, cancellationToken);
            var conversation = await StartedAsync(store, user, cancellationToken);
            var answer = AgentMessageId.New();

            await store.AppendAsync(conversation, UserId.Create(user), new AgentAnswerStarted(answer), Instant, cancellationToken);

            foreach (var _ in Enumerable.Range(0, 4))
            {
                await store.AppendAsync(conversation, UserId.Create(user), Composed(answer), Instant, cancellationToken);
            }

            // Act
            var page = await store.ReadAsync(conversation, UserId.Create(user), afterSequence: 0, limit: 2, cancellationToken);
            var rest = await store.ReadAsync(conversation, UserId.Create(user), afterSequence: 2, limit: 10, cancellationToken);

            // Assert
            Assert.Equal([1L, 2L], page?.Entries.Select(written => written.Sequence));
            Assert.True(page?.MoreFollows);
            Assert.Equal([3L, 4L, 5L], rest?.Entries.Select(written => written.Sequence));
            Assert.False(rest?.MoreFollows);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(host, user);
        }
    }

    /// <summary>A stop reaching a conversation that filled while its answer ran writes both the ending and the agent's note.</summary>
    /// <remarks>
    /// Every other entry stops short of the places kept for this pair, so the stop is neither refused as though the
    /// answer were not running nor written as an ending with no word from the agent after it. Nothing fits past the note.
    /// </remarks>
    [Fact]
    public async Task StopAsync_AnAnswerRunningWhenTheConversationFills_WritesTheEndingAndTheNoteInTheKeptPlaces()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(host, user, cancellationToken);

        try
        {
            // Arrange
            var store = await StoreOfAsync(host, cancellationToken);
            var person = UserId.Create(user);
            var conversation = await StartedAsync(store, user, cancellationToken);
            var answer = AgentMessageId.New();
            await store.AppendAsync(conversation, person, new AgentAnswerStarted(answer), Instant, cancellationToken);

            var lastOrdinaryPlace = AgentConversationBounds.MaximumEntries - AgentConversationBounds.PlacesKeptForAnEnding;

            for (var place = 2; place <= lastOrdinaryPlace; place++)
            {
                await store.AppendAsync(conversation, person, Composed(answer), Instant, cancellationToken);
            }

            // Act
            var composedIntoAKeptPlace = await store.AppendAsync(conversation, person, Composed(answer), Instant, cancellationToken);
            var noted = await store.StopAsync(conversation, person, answer, Note(), Instant, cancellationToken);
            var pastTheEnd = await store.AppendAsync(conversation, person, Note(), Instant, cancellationToken);

            // Assert
            Assert.Null(composedIntoAKeptPlace);
            Assert.Equal(AgentConversationBounds.MaximumEntries, noted);
            Assert.Null(pastTheEnd);

            var tail = await store.ReadAsync(conversation, person, afterSequence: lastOrdinaryPlace, limit: 10, cancellationToken);
            Assert.NotNull(tail);
            Assert.False(tail.Composing);
            Assert.Equal([AgentAnswerEnded.Kind, AgentMessageWritten.Kind], tail.Entries.Select(written => written.EntryName));
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(host, user);
        }
    }

    /// <summary>An answer ended on one replica is one no other replica may go on writing into, and what it composed stays.</summary>
    /// <remarks>
    /// This is how a person stopping their run reaches the replica spending against a provider, and it is the whole of
    /// that mechanism: there is no message and no shared token, only the condition on the append. Nothing already
    /// written is removed, so the blocks composed before the stop are still in the conversation afterwards.
    /// </remarks>
    [Fact]
    public async Task AppendAsync_AfterAnotherReplicaEndedTheAnswer_RefusesTheNextPartAndKeepsWhatArrived()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var composingHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await using var stoppingHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(composingHost, user, cancellationToken);

        try
        {
            // Arrange
            var composing = await StoreOfAsync(composingHost, cancellationToken);
            var stopping = await StoreOfAsync(stoppingHost, cancellationToken);
            var conversation = await StartedAsync(composing, user, cancellationToken);
            var answer = AgentMessageId.New();

            await composing.AppendAsync(conversation, UserId.Create(user), new AgentAnswerStarted(answer), Instant, cancellationToken);
            await composing.AppendAsync(conversation, UserId.Create(user), Composed(answer), Instant, cancellationToken);

            // Act
            var stopped = await stopping.AppendAsync(
                conversation,
                UserId.Create(user),
                new AgentAnswerEnded(answer, AgentAnswerOutcome.Stopped),
                Instant,
                cancellationToken);
            var refused = await composing.AppendAsync(conversation, UserId.Create(user), Composed(answer), Instant, cancellationToken);
            var note = await stopping.AppendAsync(conversation, UserId.Create(user), Note(), Instant, cancellationToken);

            // Assert
            Assert.Equal(3L, stopped);
            Assert.Null(refused);
            Assert.Equal(4L, note);

            var read = await composing.ReadAsync(conversation, UserId.Create(user), afterSequence: 0, limit: 50, cancellationToken);
            Assert.False(read?.Composing);
            Assert.Equal(
                [AgentAnswerStarted.Kind, AgentBlockComposed.Kind, AgentAnswerEnded.Kind, AgentMessageWritten.Kind],
                read?.Entries.Select(written => written.EntryName));
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(composingHost, user);
        }
    }

    /// <summary>One answer is composed at a time, so a second opened while one is running is refused.</summary>
    [Fact]
    public async Task AppendAsync_ASecondAnswerOpenedWhileOneIsComposing_IsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(host, user, cancellationToken);

        try
        {
            // Arrange
            var store = await StoreOfAsync(host, cancellationToken);
            var conversation = await StartedAsync(store, user, cancellationToken);
            var answer = AgentMessageId.New();

            await store.AppendAsync(conversation, UserId.Create(user), new AgentAnswerStarted(answer), Instant, cancellationToken);

            // Act
            var second = await store.AppendAsync(
                conversation,
                UserId.Create(user),
                new AgentAnswerStarted(AgentMessageId.New()),
                Instant,
                cancellationToken);
            var steering = await store.AppendAsync(conversation, UserId.Create(user), Question(), Instant, cancellationToken);

            // Assert
            Assert.Null(second);
            Assert.Equal(2L, steering);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(host, user);
        }
    }

    /// <summary>A person typing while a run composes takes a place of its own, and no two entries ever share one.</summary>
    /// <remarks>
    /// The two writers are real — a route writing a steering message and a run writing a block — and the place they
    /// each take is the conversation's own counter advanced inside the insert. Two callers reading a maximum would
    /// both find the same next number; the row lock that advance takes is what makes that impossible rather than
    /// unlikely, and the key over the conversation and the place is what would report it if it were not.
    /// </remarks>
    [Fact]
    public async Task AppendAsync_SeveralWritersAtOnce_GivesEachEntryAPlaceOfItsOwn()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(host, user, cancellationToken);

        try
        {
            // Arrange
            var store = await StoreOfAsync(host, cancellationToken);
            var conversation = await StartedAsync(store, user, cancellationToken);
            var answer = AgentMessageId.New();

            await store.AppendAsync(conversation, UserId.Create(user), new AgentAnswerStarted(answer), Instant, cancellationToken);

            // Act
            var attempts = await ConcurrentIdempotency.RunAsync(
                "Appending to one conversation from several writers",
                ContendingCallers,
                (ordinal, token) => store.AppendAsync(
                    conversation,
                    UserId.Create(user),
                    ordinal % 2 == 0 ? Composed(answer) : Question(),
                    Instant,
                    token),
                cancellationToken);

            // Assert
            var places = attempts.Results.Where(place => place is not null).Select(place => place!.Value).ToArray();
            Assert.Equal(ContendingCallers, places.Length);
            Assert.Equal(Enumerable.Range(2, ContendingCallers).Select(place => (long)place), places.Order());
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(host, user);
        }
    }

    /// <summary>One question posted by several callers at once — a client retrying over a dropped connection — is written once, and every caller is handed the same answer.</summary>
    /// <remarks>
    /// The question starts the conversation too, so this is also the claim that starting one is safe to race: the key
    /// absorbs the second start, the held row serializes the posts, and the retry finds the question the winner wrote
    /// rather than writing a second copy the fold would refuse.
    /// </remarks>
    [Fact]
    public async Task AskAsync_OneQuestionPostedByManyAtOnce_WritesItOnceAndHandsEveryCallerTheSameAnswer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(host, user, cancellationToken);

        try
        {
            // Arrange
            var store = await StoreOfAsync(host, cancellationToken);
            var conversation = AgentConversationId.New();
            var question = Question();

            // Act
            var attempts = await ConcurrentIdempotency.RunAsync(
                "Posting one question from several callers",
                ContendingCallers,
                (_, token) => store.AskAsync(conversation, UserId.Create(user), question, AgentMessageId.New(), Instant, token),
                cancellationToken);

            // Assert
            var postings = attempts.Results.ToArray();
            Assert.Equal(ContendingCallers, postings.Length);
            Assert.Single(postings, posting => posting.Outcome is AgentMessagePostingOutcome.Written);
            Assert.All(postings, posting => Assert.True(posting.Stands));
            Assert.Single(postings.Select(posting => (posting.Answer, posting.Reached)).Distinct());
            Assert.Equal(2, await CountEntriesOfAsync(host, conversation, cancellationToken));
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(host, user);
        }
    }

    /// <summary>A question waits for the answer being composed while an instruction joins it, and neither reaches an answer that has ended or a conversation that is somebody else's.</summary>
    /// <remarks>
    /// What is being proved is that the decision is taken from the held row: a question left behind with no answer would
    /// read as an instruction to whatever answered next, and an instruction to an ended answer would stand in the record
    /// as a question nobody answers. The record then folds, which is the proof that nothing either refusal left behind
    /// broke the order.
    /// </remarks>
    [Fact]
    public async Task AskAsync_AndSteerAsync_AdmitWhatTheAnswerBeingComposedAllowsAndNothingElse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();
        var somebodyElse = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(host, user, cancellationToken);
        await OrchestratedForeignUser.ProvisionAsync(host, somebodyElse, cancellationToken);

        try
        {
            // Arrange
            var store = await StoreOfAsync(host, cancellationToken);
            var conversation = AgentConversationId.New();
            var person = UserId.Create(user);
            var asked = await store.AskAsync(conversation, person, Question(), AgentMessageId.New(), Instant, cancellationToken);
            var running = asked.Answer!.Value;

            // Act
            var askedOver = await store.AskAsync(conversation, person, Question(), AgentMessageId.New(), Instant, cancellationToken);
            var steered = await store.SteerAsync(conversation, person, running, Question(), Instant, cancellationToken);
            var steeredElsewhere = await store.SteerAsync(conversation, person, AgentMessageId.New(), Question(), Instant, cancellationToken);
            await store.AppendAsync(conversation, person, new AgentAnswerEnded(running, AgentAnswerOutcome.Stopped), Instant, cancellationToken);
            var steeredLate = await store.SteerAsync(conversation, person, running, Question(), Instant, cancellationToken);
            var askedAfter = await store.AskAsync(conversation, person, Question(), AgentMessageId.New(), Instant, cancellationToken);
            var askedAsSomebodyElse = await store.AskAsync(conversation, UserId.Create(somebodyElse), Question(), AgentMessageId.New(), Instant, cancellationToken);

            // Assert
            Assert.Equal((AgentMessagePostingOutcome.Written, 2L), (asked.Outcome, asked.Reached));
            Assert.Equal(AgentMessagePostingOutcome.AnswerInProgress, askedOver.Outcome);
            Assert.Equal((AgentMessagePostingOutcome.Written, 3L, running), (steered.Outcome, steered.Reached, steered.Answer!.Value));
            Assert.Equal(AgentMessagePostingOutcome.NoAnswerInProgress, steeredElsewhere.Outcome);
            Assert.Equal(AgentMessagePostingOutcome.NoAnswerInProgress, steeredLate.Outcome);
            Assert.Equal((AgentMessagePostingOutcome.Written, 6L), (askedAfter.Outcome, askedAfter.Reached));
            Assert.Equal(AgentMessagePostingOutcome.NoSuchConversation, askedAsSomebodyElse.Outcome);

            var read = await store.ReadAsync(conversation, person, afterSequence: 0, limit: 50, cancellationToken);
            Assert.NotNull(read);
            Assert.True(read.Composing);
            Assert.Equal(5, AgentConversation.Compose(conversation, read.Title, read.StartedAt, read.Entries).Messages.Count);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(host, user);
            await OrchestratedForeignUser.EraseAsync(host, somebodyElse);
        }
    }

    /// <summary>A person at the ceiling on conversations can start no other, and can still ask into every one they hold.</summary>
    /// <remarks>
    /// The identifier of a new conversation is the client's, so the ceiling is the one thing that stops a grant from
    /// growing the table by asking under fresh identifiers. It has to refuse the start without refusing a question
    /// into a conversation that already stands, which is the half a count taken in the wrong place would get wrong.
    /// </remarks>
    [Fact]
    public async Task AskAsync_ForAPersonAtTheCeilingOnConversations_StartsNoOtherAndStillAnswersInOneTheyHold()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(host, user, cancellationToken);

        try
        {
            // Arrange
            var store = await StoreOfAsync(host, cancellationToken);
            var person = UserId.Create(user);
            var held = AgentConversationId.New();
            await store.TryStartAsync(held, person, Instant, cancellationToken);

            for (var started = 1; started < AgentConversationBounds.MaximumConversations; started++)
            {
                await store.TryStartAsync(AgentConversationId.New(), person, Instant, cancellationToken);
            }

            // Act
            var startedPastTheCeiling = await store.TryStartAsync(AgentConversationId.New(), person, Instant, cancellationToken);
            var askedAfresh = await store.AskAsync(AgentConversationId.New(), person, Question(), AgentMessageId.New(), Instant, cancellationToken);
            var askedInOneHeld = await store.AskAsync(held, person, Question(), AgentMessageId.New(), Instant, cancellationToken);

            // Assert
            Assert.False(startedPastTheCeiling);
            Assert.Equal(AgentMessagePostingOutcome.TooManyConversations, askedAfresh.Outcome);
            Assert.Equal(AgentMessagePostingOutcome.Written, askedInOneHeld.Outcome);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(host, user);
        }
    }

    /// <summary>An offer several callers accept at once is accepted once, because accepting is what permits the act.</summary>
    /// <remarks>
    /// The whole reason where a proposal stands is read inside the statement that moves it. A read followed by a write
    /// would let two presses both find it pending, and an action with a side effect would then be permitted twice —
    /// which for a proposal that sends mail is a message nobody can recall.
    /// </remarks>
    [Fact]
    public async Task TryResolveProposalAsync_SeveralCallersAcceptingAtOnce_AcceptsItOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(host, user, cancellationToken);

        try
        {
            // Arrange
            var store = await StoreOfAsync(host, cancellationToken);
            var conversation = await StartedAsync(store, user, cancellationToken);
            var answer = AgentMessageId.New();

            await store.AppendAsync(conversation, UserId.Create(user), new AgentAnswerStarted(answer), Instant, cancellationToken);
            var proposedAt = await store.AppendAsync(conversation, UserId.Create(user), Proposed(answer), Instant, cancellationToken);

            // Act
            var attempts = await ConcurrentIdempotency.RunAsync(
                "Accepting one proposed action from several callers",
                ContendingCallers,
                (_, token) => store.TryResolveProposalAsync(
                    conversation,
                    UserId.Create(user),
                    proposedAt!.Value,
                    AgentProposalState.Accepted,
                    Instant,
                    token),
                cancellationToken);

            // Assert
            attempts.AssertSingleEffect(attempts.Results.Count(place => place is not null));
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(host, user);
        }
    }

    /// <summary>An offer moves from where it stands and nowhere else, which is what keeps the four states a history rather than a guess.</summary>
    [Fact]
    public async Task TryResolveProposalAsync_AMoveTheOfferCannotMake_IsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(host, user, cancellationToken);

        try
        {
            // Arrange
            var store = await StoreOfAsync(host, cancellationToken);
            var conversation = await StartedAsync(store, user, cancellationToken);
            var answer = AgentMessageId.New();

            await store.AppendAsync(conversation, UserId.Create(user), new AgentAnswerStarted(answer), Instant, cancellationToken);
            var declinedAt = await store.AppendAsync(conversation, UserId.Create(user), Proposed(answer), Instant, cancellationToken);
            var acceptedAt = await store.AppendAsync(conversation, UserId.Create(user), Proposed(answer), Instant, cancellationToken);
            var leftPendingAt = await store.AppendAsync(conversation, UserId.Create(user), Proposed(answer), Instant, cancellationToken);

            // Act
            var declined = await Resolve(store, conversation, user, declinedAt, AgentProposalState.Declined, cancellationToken);
            var failedAfterDeclining = await Resolve(store, conversation, user, declinedAt, AgentProposalState.Failed, cancellationToken);
            var accepted = await Resolve(store, conversation, user, acceptedAt, AgentProposalState.Accepted, cancellationToken);
            var failedAfterAccepting = await Resolve(store, conversation, user, acceptedAt, AgentProposalState.Failed, cancellationToken);

            // The offer this one names is still pending, so nothing but the person refuses it — against one already
            // somewhere it cannot move from, the assertion would hold with the ownership check gone.
            var somebodyElses = await store.TryResolveProposalAsync(
                conversation,
                UserId.Create(Guid.NewGuid()),
                leftPendingAt!.Value,
                AgentProposalState.Declined,
                Instant,
                cancellationToken);
            var theirsStillMoves = await Resolve(store, conversation, user, leftPendingAt, AgentProposalState.Declined, cancellationToken);
            var nothingWasOfferedThere = await Resolve(store, conversation, user, 1, AgentProposalState.Accepted, cancellationToken);

            // Assert
            Assert.NotNull(declined);
            Assert.Null(failedAfterDeclining);
            Assert.NotNull(accepted);
            Assert.NotNull(failedAfterAccepting);
            Assert.Null(somebodyElses);
            Assert.NotNull(theirsStillMoves);
            Assert.Null(nothingWasOfferedThere);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(host, user);
        }
    }

    /// <summary>A person's history is theirs, most recently active first, and nothing they do reaches somebody else's.</summary>
    /// <remarks>
    /// <para>
    /// Somebody else's conversation is here so that every write's ownership check is answered against a record in a
    /// state the write could otherwise have succeeded in: it exists, it is not full, and nothing about it but whose it
    /// is refuses the attempt. That is what separates a refusal earned by the check from one the record's own state
    /// would have produced anyway.
    /// </para>
    /// <para>
    /// The removal is one statement against the conversations, and what it takes with it is the entries' own cascade —
    /// which is also what an erasure of the person reaches both tables through, so the count afterwards is the whole of
    /// the storage limitation on a conversation somebody deleted.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ListAsync_APersonsOwnConversations_ReadsThemByRecencyAndLosesTheEntriesOfOneRemoved()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();
        var somebodyElse = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(host, user, cancellationToken);
        await OrchestratedForeignUser.ProvisionAsync(host, somebodyElse, cancellationToken);

        try
        {
            // Arrange
            var store = await StoreOfAsync(host, cancellationToken);
            var older = await StartedAsync(store, user, cancellationToken);
            var newer = await StartedAsync(store, user, cancellationToken);
            var theirs = await StartedAsync(store, somebodyElse, cancellationToken);

            await store.AppendAsync(older, UserId.Create(user), Question(), Instant, cancellationToken);
            await store.AppendAsync(newer, UserId.Create(user), Question(), Instant.AddMinutes(1), cancellationToken);
            await store.AppendAsync(theirs, UserId.Create(somebodyElse), Question(), Instant.AddMinutes(2), cancellationToken);

            // Act
            var history = await store.ListAsync(UserId.Create(user), limit: 10, cancellationToken);
            var wroteIntoSomebodyElses = await store.AppendAsync(theirs, UserId.Create(user), Question(), Instant, cancellationToken);
            var namedSomebodyElses = await store.TrySetTitleAsync(theirs, UserId.Create(user), PresentationText.Create("Not theirs to name"), cancellationToken);
            var removedSomebodyElses = await store.TryDeleteAsync(theirs, UserId.Create(user), cancellationToken);
            var removed = await store.TryDeleteAsync(newer, UserId.Create(user), cancellationToken);

            // Assert
            Assert.Equal([newer, older], history.Select(line => line.Id));
            Assert.Null(wroteIntoSomebodyElses);
            Assert.False(namedSomebodyElses);
            Assert.False(removedSomebodyElses);
            Assert.True(removed);
            Assert.Equal(0, await CountEntriesOfAsync(host, newer, cancellationToken));
            Assert.Null(await store.ReadAsync(newer, UserId.Create(user), afterSequence: 0, limit: 10, cancellationToken));
            Assert.NotNull(await store.ReadAsync(theirs, UserId.Create(somebodyElse), afterSequence: 0, limit: 10, cancellationToken));
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(host, user);
            await OrchestratedForeignUser.EraseAsync(host, somebodyElse);
        }
    }

    /// <summary>A person erased takes their conversations and everything said in them, through the cascade alone.</summary>
    [Fact]
    public async Task TryStartAsync_AConversationOfAnErasedPerson_GoesWithTheirRecord()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(host, user, cancellationToken);

        try
        {
            // Arrange
            var store = await StoreOfAsync(host, cancellationToken);
            var conversation = await StartedAsync(store, user, cancellationToken);

            await store.AppendAsync(conversation, UserId.Create(user), Question(), Instant, cancellationToken);

            // Act
            var startedTwice = await store.TryStartAsync(conversation, UserId.Create(user), Instant, cancellationToken);

            await OrchestratedForeignUser.EraseAsync(host, user);

            // Assert
            Assert.False(startedTwice);
            Assert.Null(await store.ReadAsync(conversation, UserId.Create(user), afterSequence: 0, limit: 10, cancellationToken));
            Assert.Equal(0, await CountEntriesOfAsync(host, conversation, cancellationToken));
        }
        finally
        {
            // Erased a second time on the ordinary path, which reports that there was nothing to erase rather than
            // failing. What the block is for is a case that ended before the erasure above.
            await OrchestratedForeignUser.EraseAsync(host, user);
        }
    }

    private static Task<long?> Resolve(
        IAgentConversationStore store,
        AgentConversationId conversation,
        Guid user,
        long? proposedAt,
        AgentProposalState state,
        CancellationToken cancellationToken) => store.TryResolveProposalAsync(
            conversation,
            UserId.Create(user),
            proposedAt!.Value,
            state,
            Instant,
            cancellationToken);

    private static async Task<AgentConversationId> StartedAsync(
        IAgentConversationStore store,
        Guid user,
        CancellationToken cancellationToken)
    {
        var id = AgentConversationId.New();

        Assert.True(await store.TryStartAsync(id, UserId.Create(user), Instant, cancellationToken));

        return id;
    }

    private static AgentMessageWritten Question() => new(
        AgentMessageId.New(),
        AgentMessageAuthor.Person,
        PresentationText.Create("Where did we land on the price?"),
        AgentMessageScope.Mailbox());

    private static AgentMessageWritten Note() => new(
        AgentMessageId.New(),
        AgentMessageAuthor.Agent,
        PresentationText.Create("The run was stopped. What arrived stays."),
        Scope: null);

    private static AgentBlockComposed Composed(AgentMessageId answer) => new(
        answer,
        new AnswerBlock(
            PresentationEvidence.Unsupported(PresentationFreshness.Unknown),
            PresentationText.Create(AnswerText),
            PresentationConfidence.Low));

    private static AgentActionProposed Proposed(AgentMessageId answer) => new(
        answer,
        new SuggestedActionBlock(
            PresentationEvidence.Unsupported(PresentationFreshness.Unknown),
            SuggestedActionKind.OpenThread,
            PresentationText.Create("The thread has the figure in it."),
            SuggestedActionImpact.ReadsOnly,
            requiresConfirmation: true));

    private static Task<int> CountEntriesOfAsync(
        OrchestratedMailFathomServices host,
        AgentConversationId conversation,
        CancellationToken cancellationToken) => host.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .AgentConversationEntries
                .AsNoTracking()
                .Where(written => written.ConversationId == conversation.Value)
                .CountAsync(token),
            cancellationToken);

    private static Task<IAgentConversationStore> StoreOfAsync(
        OrchestratedMailFathomServices host,
        CancellationToken cancellationToken) => host.InScopeAsync(
            (scope, _) => Task.FromResult(scope.GetRequiredService<IAgentConversationStore>()),
            cancellationToken);
}
