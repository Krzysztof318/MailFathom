// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Domain.Access;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the Agent conversation routes accept off the wire, what they refuse, and what each outcome is answered with.</summary>
/// <remarks>
/// The store is a substitute, because what each statement decides is proved against a real database; what is asserted
/// here is the transport — which requests are refused before anything is written, that every route reads the person off
/// the credential, and which status each of the store's answers becomes.
/// </remarks>
public sealed class ClientAgentConversationEndpointTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid Conversation = Guid.CreateVersion7(Now);

    private static readonly Guid Run = Guid.CreateVersion7(Now);

    private readonly IAgentConversationStore store = Substitute.For<IAgentConversationStore>();

    /// <summary>The paths a client composes from constants of its own, pinned so a rename is a decision rather than an accident.</summary>
    [Fact]
    public void Routes_AreThePathsAClientComposes() =>
        Assert.Equal(
            [
                "/agent/conversations",
                "/agent/conversations/{conversationId:guid}",
                "/agent/conversations/{conversationId:guid}/messages",
                "/agent/conversations/{conversationId:guid}/runs/{runId:guid}",
                "/agent/conversations/{conversationId:guid}/runs/{runId:guid}/messages",
                "/agent/conversations/{conversationId:guid}/proposals/{proposedAt:long}",
            ],
            [
                ClientAgentConversationEndpoints.ConversationsRoute,
                ClientAgentConversationEndpoints.ConversationRoute,
                ClientAgentConversationEndpoints.MessagesRoute,
                ClientAgentConversationEndpoints.RunRoute,
                ClientAgentConversationEndpoints.RunMessagesRoute,
                ClientAgentConversationEndpoints.ProposalRoute,
            ]);

    /// <summary>A question the store writes answers <c>202</c> naming the message, the run it opened, and where to read the conversation.</summary>
    [Fact]
    public async Task Ask_AQuestionTheStoreWrites_AnswersAcceptedNamingTheRunAndTheConversation()
    {
        // Arrange
        var message = Guid.CreateVersion7(Now);
        var answer = AgentMessageId.New();
        this.store
            .AskAsync(Arg.Any<AgentConversationId>(), SyntheticUser.Deployment, Arg.Any<AgentMessageWritten>(), Arg.Any<AgentMessageId>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new AgentMessagePosting(AgentMessagePostingOutcome.Written, 2, answer));

        // Act
        var answered = await ClientAgentConversationEndpoints.Ask(
            Conversation,
            new ClientAgentMessageRequest(message, "What did the supplier quote?", new ClientAgentMessageScope(AgentScopeKind.Mailbox, null)),
            Resolver(),
            this.Controls(),
            TestContext.Current.CancellationToken);

        // Assert
        var accepted = Assert.IsType<Accepted<ClientAgentMessageResponse>>(answered.Result);
        Assert.Equal(new ClientAgentMessageResponse(message, answer.Value, 2), accepted.Value);
        Assert.Equal($"/api/client/agent/conversations/{Conversation}", accepted.Location);
    }

    /// <summary>A request missing what a question needs is refused before anything reaches the store.</summary>
    [Theory]
    [MemberData(nameof(MalformedQuestions))]
    public async Task Ask_AMalformedQuestion_RefusesItWithoutWriting(int malformed)
    {
        // Arrange
        var (conversation, request) = MalformedQuestionCases[malformed];

        // Act
        var answered = await ClientAgentConversationEndpoints.Ask(
            conversation,
            request,
            Resolver(),
            this.Controls(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(answered.Result).StatusCode);
        Assert.Empty(this.store.ReceivedCalls());
    }

    /// <summary>Each refusal the store reports is a status a client can act on, and a conversation that is not the person's reads as none.</summary>
    [Theory]
    [InlineData(AgentMessagePostingOutcome.NoSuchConversation, StatusCodes.Status404NotFound)]
    [InlineData(AgentMessagePostingOutcome.AnswerInProgress, StatusCodes.Status409Conflict)]
    [InlineData(AgentMessagePostingOutcome.ConversationFull, StatusCodes.Status409Conflict)]
    [InlineData(AgentMessagePostingOutcome.TooManyConversations, StatusCodes.Status409Conflict)]
    public async Task Ask_AQuestionTheStoreRefuses_AnswersWithTheStatusOfTheRefusal(
        AgentMessagePostingOutcome outcome,
        int status)
    {
        // Arrange
        this.store
            .AskAsync(Arg.Any<AgentConversationId>(), Arg.Any<UserId>(), Arg.Any<AgentMessageWritten>(), Arg.Any<AgentMessageId>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(AgentMessagePosting.Refused(outcome));

        // Act
        var answered = await ClientAgentConversationEndpoints.Ask(
            Conversation,
            new ClientAgentMessageRequest(Guid.CreateVersion7(Now), "What did the supplier quote?", null),
            Resolver(),
            this.Controls(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(status, StatusOf(answered.Result));
    }

    /// <summary>A retried question answers with what the first post wrote, and an identifier already used for something else is a conflict.</summary>
    [Fact]
    public async Task Ask_AnIdentifierAlreadyWritten_AnswersWithTheFirstRunOrAConflictWhereThereWasNone()
    {
        // Arrange
        var answer = AgentMessageId.New();
        this.store
            .AskAsync(Arg.Any<AgentConversationId>(), Arg.Any<UserId>(), Arg.Any<AgentMessageWritten>(), Arg.Any<AgentMessageId>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(
                new AgentMessagePosting(AgentMessagePostingOutcome.AlreadyWritten, 4, answer),
                new AgentMessagePosting(AgentMessagePostingOutcome.AlreadyWritten, 6, Answer: null));
        var request = new ClientAgentMessageRequest(Guid.CreateVersion7(Now), "What did the supplier quote?", null);

        // Act
        var retried = await ClientAgentConversationEndpoints.Ask(Conversation, request, Resolver(), this.Controls(), TestContext.Current.CancellationToken);
        var reused = await ClientAgentConversationEndpoints.Ask(Conversation, request, Resolver(), this.Controls(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(answer.Value, Assert.IsType<Accepted<ClientAgentMessageResponse>>(retried.Result).Value!.RunId);
        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(reused.Result));
    }

    /// <summary>An instruction for a run that has ended is a conflict the person answers by asking afresh, never an instruction left for nobody.</summary>
    [Fact]
    public async Task Steer_ARunNoLongerComposed_AnswersConflict()
    {
        // Arrange
        this.store
            .SteerAsync(Arg.Any<AgentConversationId>(), SyntheticUser.Deployment, AgentMessageId.Create(Run), Arg.Any<AgentMessageWritten>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(AgentMessagePosting.Refused(AgentMessagePostingOutcome.NoAnswerInProgress));

        // Act
        var answered = await ClientAgentConversationEndpoints.Steer(
            Conversation,
            Run,
            new ClientAgentInstructionRequest(Guid.CreateVersion7(Now), "Only this month."),
            Resolver(),
            this.Controls(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(answered.Result));
    }

    /// <summary>A stop answers <c>204</c> whether it stopped the run or the run had already ended, and <c>404</c> only where the conversation is not the person's.</summary>
    [Theory]
    [InlineData(true, typeof(NoContent))]
    [InlineData(false, typeof(NotFound))]
    public async Task Stop_ARunNoLongerComposed_AnswersByWhetherTheConversationIsThePersons(bool conversationIsTheirs, Type expected)
    {
        // Arrange
        this.store
            .AppendAsync(Arg.Any<AgentConversationId>(), Arg.Any<UserId>(), Arg.Any<AgentConversationEntry>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((long?)null);
        this.store
            .ReadAsync(Arg.Any<AgentConversationId>(), SyntheticUser.Deployment, 0, 1, Arg.Any<CancellationToken>())
            .Returns(conversationIsTheirs ? new AgentConversationReading(null, Now, false, [], false) : null);

        // Act
        var answered = await ClientAgentConversationEndpoints.Stop(
            Conversation,
            Run,
            Resolver(),
            this.Controls(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType(expected, answered.Result);
    }

    /// <summary>Only accepting and declining are a person's answers, and anything else is refused before the store is asked.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("failed")]
    [InlineData("pending")]
    [InlineData("Accepted")]
    public async Task AnswerProposal_ADecisionThatIsNotAPersonsAnswer_IsRefused(string? decision)
    {
        // Act
        var answered = await ClientAgentConversationEndpoints.AnswerProposal(
            Conversation,
            proposedAt: 3,
            new ClientAgentProposalAnswerRequest(decision),
            Resolver(),
            this.Controls(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(answered.Result));
        Assert.Empty(this.store.ReceivedCalls());
    }

    /// <summary>A decision the store records answers with its place, and one it refuses — already decided, or not the person's — is a conflict.</summary>
    [Fact]
    public async Task AnswerProposal_RecordedThenRepeated_AnswersWithThePlaceThenAConflict()
    {
        // Arrange
        this.store
            .TryResolveProposalAsync(Arg.Any<AgentConversationId>(), SyntheticUser.Deployment, 3, AgentProposalState.Declined, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(8L, (long?)null);
        var request = new ClientAgentProposalAnswerRequest(ClientAgentProposalAnswerRequest.Declined);

        // Act
        var first = await ClientAgentConversationEndpoints.AnswerProposal(Conversation, 3, request, Resolver(), this.Controls(), TestContext.Current.CancellationToken);
        var second = await ClientAgentConversationEndpoints.AnswerProposal(Conversation, 3, request, Resolver(), this.Controls(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new ClientAgentProposalAnswerResponse(8), Assert.IsType<Ok<ClientAgentProposalAnswerResponse>>(first.Result).Value);
        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(second.Result));
    }

    /// <summary>A read hands each entry over beside its place and under the record's own discriminator, reading from the cursor the client held.</summary>
    [Fact]
    public async Task Read_FromACursor_HandsEachEntryBesideItsPlaceUnderItsStoredName()
    {
        // Arrange
        var answer = AgentMessageId.New();
        this.store
            .ReadAsync(AgentConversationId.Create(Conversation), SyntheticUser.Deployment, 3, AgentConversationBounds.MaximumEntriesPerRead, Arg.Any<CancellationToken>())
            .Returns(new AgentConversationReading(
                "Supplier quotes",
                Now,
                Composing: true,
                [new AgentAnswerStarted(answer) { Sequence = 4 }, new AgentStatusReported(answer, PresentationText.Create("Reading the quotes")) { Sequence = 5 }],
                MoreFollows: false));

        // Act
        var answered = await ClientAgentConversationEndpoints.Read(
            Conversation,
            since: 3,
            Resolver(),
            this.store,
            TestContext.Current.CancellationToken);

        // Assert
        var read = Assert.IsType<Ok<ClientAgentConversationReadResponse>>(answered.Result).Value!;
        Assert.True(read.Composing);
        Assert.Equal([4L, 5L], read.Entries.Select(entry => entry.Sequence));
        Assert.Equal(
            [AgentAnswerStarted.Kind, AgentStatusReported.Kind],
            read.Entries.Select(entry => entry.Entry.GetProperty("entry").GetString()));
        Assert.Equal(answer.Value, read.Entries[0].Entry.GetProperty("messageId").GetGuid());
    }

    /// <summary>A conversation the person does not hold reads as none, and a negative cursor reads from the beginning.</summary>
    [Fact]
    public async Task Read_AConversationThePersonDoesNotHold_AnswersNotFound()
    {
        // Arrange
        this.store
            .ReadAsync(Arg.Any<AgentConversationId>(), Arg.Any<UserId>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((AgentConversationReading?)null);

        // Act
        var answered = await ClientAgentConversationEndpoints.Read(
            Conversation,
            since: -5,
            Resolver(),
            this.store,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(answered.Result);
        await this.store.Received(1).ReadAsync(
            Arg.Any<AgentConversationId>(),
            SyntheticUser.Deployment,
            0,
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>The history is the signed-in person's, read at the bound, and each line carries what the store listed.</summary>
    [Fact]
    public async Task List_ThePersonsHistory_ListsEveryLineTheStoreReturned()
    {
        // Arrange
        var line = new AgentConversationSummary(AgentConversationId.Create(Conversation), "Supplier quotes", Now, Now.AddMinutes(3));
        this.store
            .ListAsync(SyntheticUser.Deployment, AgentConversationBounds.MaximumConversationsPerListing, Arg.Any<CancellationToken>())
            .Returns([line]);

        // Act
        var answered = await ClientAgentConversationEndpoints.List(Resolver(), this.store, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [new ClientAgentConversationSummary(Conversation, "Supplier quotes", Now, Now.AddMinutes(3))],
            answered.Value!.Conversations);
    }

    /// <summary>Deleting answers <c>204</c> where the conversation was the person's and <c>404</c> otherwise.</summary>
    [Theory]
    [InlineData(true, typeof(NoContent))]
    [InlineData(false, typeof(NotFound))]
    public async Task Delete_AConversation_AnswersByWhetherItWasThePersons(bool deleted, Type expected)
    {
        // Arrange
        this.store
            .TryDeleteAsync(AgentConversationId.Create(Conversation), SyntheticUser.Deployment, Arg.Any<CancellationToken>())
            .Returns(deleted);

        // Act
        var answered = await ClientAgentConversationEndpoints.Delete(Conversation, Resolver(), this.store, TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType(expected, answered.Result);
    }

    /// <summary>Indexes into <see cref="MalformedQuestionCases" />, which the request records being internal keeps out of a theory's own data.</summary>
    public static TheoryData<int> MalformedQuestions => [.. Enumerable.Range(0, MalformedQuestionCases.Length)];

    /// <summary>Requests missing an identifier, text, or a coherent scope.</summary>
    private static (Guid Conversation, ClientAgentMessageRequest? Request)[] MalformedQuestionCases =>
    [
        (Conversation, null),
        (Guid.Empty, new ClientAgentMessageRequest(Guid.CreateVersion7(Now), "What did the supplier quote?", null)),
        (Conversation, new ClientAgentMessageRequest(null, "What did the supplier quote?", null)),
        (Conversation, new ClientAgentMessageRequest(Guid.CreateVersion7(Now), "   ", null)),
        (Conversation, new ClientAgentMessageRequest(Guid.CreateVersion7(Now), new string('x', PresentationText.MaxLength + 1), null)),
        (Conversation, new ClientAgentMessageRequest(Guid.CreateVersion7(Now), "About this thread", new ClientAgentMessageScope(AgentScopeKind.Thread, null))),
    ];

    private static int? StatusOf(IResult result) => (result as IStatusCodeHttpResult)?.StatusCode;

    private static MailboxScopeResolver Resolver() =>
        new(
            AssignedMailAccountCatalogs.For(
                AccessAuthorizations.ForUserGranted(SyntheticUser.Deployment, MailFathomPermission.MailAsk),
                SyntheticServedAccount.Of("primary")),
            StubMailFolderParticipation.Nothing,
            StubJunkMailFolderCatalog.None,
            StubMailFolderMappings.Nothing.Resolver);

    private AgentConversationControls Controls() =>
        new(this.store, ClientSignalPublishers.ReachingNobody, Substitute.For<IUserLanguages>(), new FakeTimeProvider(Now));
}
