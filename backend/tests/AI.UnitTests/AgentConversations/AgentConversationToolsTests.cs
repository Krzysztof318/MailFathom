// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.AgentConversations;
using MailFathom.AI.Retrieval;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.Access;
using MailFathom.Application.Agent.Answering;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.Signals;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.AI.UnitTests.AgentConversations;

/// <summary>Covers which tools one Agent run is offered and what a proposing tool leaves behind.</summary>
/// <remarks>
/// The readers other than the authorization are left unset because nothing asserted here reads mail through them: which
/// tools exist is decided by the grant alone, and a proposal is written from the arguments the model supplied. The search
/// answers from a recording retrieval, which is what a proposal citing a message the run found is arranged over.
/// </remarks>
public sealed class AgentConversationToolsTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private static readonly MailAccountId Primary = MailAccountId.Create("primary");

    private static readonly string[] Recipients = ["ada@example.org"];

    private readonly FakeTimeProvider clock = new(Now);

    private readonly AgentConversationId conversation = AgentConversationId.New();

    private readonly IAgentConversationStore store = Substitute.For<IAgentConversationStore>();

    private readonly List<AgentConversationEntry> written = [];

    private readonly RecordingEmailKnowledgeSearch knowledgeSearch = new();

    private readonly ClientSignals signals;

    private readonly AgentAnswerJournal journal;

    private AgentConversationTools tools = null!;

    /// <summary>Arranges a store that accepts every write and records it.</summary>
    public AgentConversationToolsTests()
    {
        this.signals = new ClientSignals([new RecordingClientSignalChannel()], this.clock);
        this.journal = new AgentAnswerJournal(
            this.conversation,
            SyntheticUser.Deployment,
            AgentMessageId.New(),
            openedAt: 1,
            this.store,
            this.signals,
            Substitute.For<IUserLanguages>(),
            this.clock);
        this.store
            .AppendAsync(this.conversation, SyntheticUser.Deployment, Arg.Any<AgentConversationEntry>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                this.written.Add(call.ArgAt<AgentConversationEntry>(2));

                return (long?)this.written.Count + 1;
            });
    }

    /// <summary>A grant that only reads proposes nothing that leaves the deployment, and proposes onto the person's own calendar and list, which those use cases admit under the same grant.</summary>
    [Fact]
    public void Create_AGrantThatOnlyReads_OffersNoMailProposal()
    {
        // Act
        var names = this.Tools(MailFathomPermission.MailRead).Create().Select(static tool => tool.Name);

        // Assert
        Assert.Equal(
            ["suggest_follow_ups", "search_mail", "read_thread", "show_thread_state", "read_calendar", "read_tasks", "propose_event", "propose_task"],
            names);
    }

    /// <summary>A grant that drafts and sends but reads nothing may propose a new message and cannot answer one it could not read.</summary>
    [Fact]
    public void Create_AGrantThatSendsButDoesNotRead_OffersOnlyANewMessage()
    {
        // Act
        var names = this.Tools(MailFathomPermission.MailDraftsWrite, MailFathomPermission.MailSend).Create().Select(static tool => tool.Name);

        // Assert
        Assert.Equal(["suggest_follow_ups", "propose_message"], names);
    }

    /// <summary>A grant that drafts without sending proposes no mail, because accepting it could not be carried out.</summary>
    [Fact]
    public void Create_AGrantThatCannotSend_OffersNoMailProposal()
    {
        // Act
        var names = this.Tools(MailFathomPermission.MailRead, MailFathomPermission.MailDraftsWrite).Create().Select(static tool => tool.Name);

        // Assert
        Assert.DoesNotContain(names, static name => name is "propose_message" or "propose_reply");
    }

    /// <summary>A grant that reads, drafts, and sends is offered every proposing tool, answering a message it read among them.</summary>
    [Fact]
    public void Create_AGrantThatReadsAndSends_OffersEveryProposingTool()
    {
        // Act
        var names = this.Tools(MailFathomPermission.MailRead, MailFathomPermission.MailDraftsWrite, MailFathomPermission.MailSend)
            .Create()
            .Select(static tool => tool.Name)
            .Where(static name => name.StartsWith("propose_", StringComparison.Ordinal));

        // Assert
        Assert.Equal(["propose_event", "propose_task", "propose_message", "propose_reply"], names);
    }

    /// <summary>What the model suggests asking next is held for the answer's ending and written nowhere on its own.</summary>
    [Fact]
    public async Task SuggestFollowUps_QuestionsWithinTheBounds_AreHeldForTheEndingAndWriteNothing()
    {
        // Arrange
        var tool = this.ToolNamed("suggest_follow_ups", MailFathomPermission.MailRead);

        // Act
        await tool.InvokeAsync(Suggesting("Draft a reply to Ada", "Show me the sources"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["Draft a reply to Ada", "Show me the sources"], this.tools.FollowUps.Select(static followUp => followUp.Value));
        Assert.Empty(this.written);
    }

    /// <summary>More suggestions than an answer carries are refused back to the model, and what it suggested before stands.</summary>
    [Fact]
    public async Task SuggestFollowUps_MoreThanAnAnswerCarries_IsRefusedAndKeepsWhatWasHeld()
    {
        // Arrange
        var tool = this.ToolNamed("suggest_follow_ups", MailFathomPermission.MailRead);
        await tool.InvokeAsync(Suggesting("Draft a reply to Ada"), TestContext.Current.CancellationToken);

        // Act
        var answer = await tool.InvokeAsync(Suggesting("One", "Two", "Three", "Four"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("one to 3 questions", answer?.ToString(), StringComparison.Ordinal);
        Assert.Equal(["Draft a reply to Ada"], this.tools.FollowUps.Select(static followUp => followUp.Value));
    }

    /// <summary>A model that is replaced after suggesting leaves nothing for the answer the next one composes.</summary>
    [Fact]
    public async Task ForgetFollowUps_AfterAModelSuggested_LeavesNoneHeld()
    {
        // Arrange
        var tool = this.ToolNamed("suggest_follow_ups", MailFathomPermission.MailRead);
        await tool.InvokeAsync(Suggesting("Draft a reply to Ada"), TestContext.Current.CancellationToken);

        // Act
        this.tools.ForgetFollowUps();

        // Assert
        Assert.Empty(this.tools.FollowUps);
    }

    /// <summary>A recipient list the model left out is refused back to it, and nothing is proposed.</summary>
    [Fact]
    public async Task ProposeMessage_NoRecipients_IsRefusedAndProposesNothing()
    {
        // Arrange
        var tool = this.ToolNamed("propose_message", MailFathomPermission.MailDraftsWrite, MailFathomPermission.MailSend);

        // Act
        var answer = await tool.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?>
            {
                ["account"] = "primary",
                ["recipients"] = null,
                ["subject"] = "Quote",
                ["body"] = "Thank you, we accept.",
            }),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.StartsWith("Give at least one valid address", answer?.ToString(), StringComparison.Ordinal);
        Assert.Empty(this.written.OfType<AgentActionProposed>());
    }

    /// <summary>Proposing a message writes the proposal and the exact act accepting it would carry out, and sends nothing.</summary>
    [Fact]
    public async Task ProposeMessage_AValidMessage_WritesTheProposalCarryingExactlyThatAct()
    {
        // Arrange
        var tool = this.ToolNamed("propose_message", MailFathomPermission.MailDraftsWrite, MailFathomPermission.MailSend);

        // Act
        await tool.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?>
            {
                ["account"] = "primary",
                ["recipients"] = Recipients,
                ["subject"] = "Quote",
                ["body"] = "Thank you, we accept.",
            }),
            TestContext.Current.CancellationToken);

        // Assert
        var proposal = Assert.Single(this.written.OfType<AgentActionProposed>());
        var sending = Assert.IsType<AgentMessageSending>(proposal.Act);
        Assert.Equal(
            ("primary", "ada@example.org", "Quote", "Thank you, we accept."),
            (sending.Account, Assert.Single(sending.Recipients).Address, sending.Subject.Value, sending.Body.Value));
        Assert.True(this.tools.HasProposed);
    }

    /// <summary>An account the person does not read is refused back to the model and nothing is proposed.</summary>
    [Fact]
    public async Task ProposeMessage_AnAccountNotInScope_ProposesNothing()
    {
        // Arrange
        var tool = this.ToolNamed("propose_message", MailFathomPermission.MailDraftsWrite, MailFathomPermission.MailSend);

        // Act
        await tool.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?>
            {
                ["account"] = "elsewhere",
                ["recipients"] = Recipients,
                ["subject"] = "Quote",
                ["body"] = "Thank you, we accept.",
            }),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(this.written.OfType<AgentActionProposed>());
        Assert.False(this.tools.HasProposed);
    }

    /// <summary>Proposing an event writes the proposal and the exact act accepting it would carry out, and schedules nothing.</summary>
    [Fact]
    public async Task ProposeEvent_AValidEvent_WritesTheProposalCarryingExactlyThatAct()
    {
        // Arrange
        var tool = this.ToolNamed("propose_event", MailFathomPermission.MailRead);

        // Act
        await tool.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?>
            {
                ["title"] = "Renewal call",
                ["start"] = "2026-09-23T09:00:00+02:00",
                ["end"] = "2026-09-23T10:00:00+02:00",
                ["allDay"] = false,
                ["messageId"] = null,
            }),
            TestContext.Current.CancellationToken);

        // Assert
        var proposal = Assert.Single(this.written.OfType<AgentActionProposed>());
        var scheduling = Assert.IsType<AgentEventScheduling>(proposal.Act);
        Assert.Equal(
            ("Renewal call", new DateTimeOffset(2026, 9, 23, 9, 0, 0, TimeSpan.FromHours(2)), false, (StoredEmailId?)null),
            (scheduling.Title.Value, scheduling.Start, scheduling.IsAllDay, scheduling.SourceMessage));
        Assert.True(this.tools.HasProposed);
    }

    /// <summary>An end that is not after the start is refused back to the model rather than proposed as a span no calendar takes.</summary>
    [Fact]
    public async Task ProposeEvent_AnEndBeforeTheStart_IsRefusedAndProposesNothing()
    {
        // Arrange
        var tool = this.ToolNamed("propose_event", MailFathomPermission.MailRead);

        // Act
        var answer = await tool.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?>
            {
                ["title"] = "Renewal call",
                ["start"] = "2026-09-23T09:00:00+02:00",
                ["end"] = "2026-09-23T08:00:00+02:00",
                ["allDay"] = false,
                ["messageId"] = null,
            }),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.StartsWith("Give an end as an ISO 8601 instant", answer?.ToString(), StringComparison.Ordinal);
        Assert.Empty(this.written.OfType<AgentActionProposed>());
    }

    /// <summary>A citation is minted from what the run read, so a message the model names out of nowhere becomes no source at all.</summary>
    [Fact]
    public async Task ProposeEvent_AMessageTheRunNeverRead_ProposesWithoutCitingIt()
    {
        // Arrange
        var tool = this.ToolNamed("propose_event", MailFathomPermission.MailRead);

        // Act
        var answer = await tool.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?>
            {
                ["title"] = "Renewal call",
                ["start"] = "2026-09-23T09:00:00+02:00",
                ["end"] = null,
                ["allDay"] = false,
                ["messageId"] = Guid.NewGuid().ToString(),
            }),
            TestContext.Current.CancellationToken);

        // Assert
        var proposal = Assert.Single(this.written.OfType<AgentActionProposed>());
        Assert.Null(Assert.IsType<AgentEventScheduling>(proposal.Act).SourceMessage);
        Assert.Empty(proposal.Block.Evidence.Citations);
        Assert.Contains("cites no source", answer?.ToString(), StringComparison.Ordinal);
    }

    /// <summary>A message the search showed the model is a source a proposal may rest on while the run is still going, not only once it has ended.</summary>
    [Fact]
    public async Task ProposeTask_AMessageTheSearchShowed_CitesItAndNamesItAsTheSource()
    {
        // Arrange
        var found = Guid.CreateVersion7();
        this.knowledgeSearch.Returning("notice period", KnowledgePassages.Create("Please send the list by Thursday.", found, subject: "Notice period"));
        var search = this.ToolNamed("search_mail", MailFathomPermission.MailRead);
        await search.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { [ScopedMailKnowledgeRetrieval.QueryArgumentName] = "notice period" }),
            TestContext.Current.CancellationToken);
        var propose = Assert.IsType<AIFunction>(Assert.Single(this.tools.Create(), tool => tool.Name == "propose_task"), exactMatch: false);

        // Act
        var answer = await propose.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?>
            {
                ["title"] = "Send the notice-period list",
                ["dueOn"] = null,
                ["messageId"] = found.ToString(),
            }),
            TestContext.Current.CancellationToken);

        // Assert
        var proposal = Assert.Single(this.written.OfType<AgentActionProposed>());
        var citation = Assert.Single(this.tools.Cited);
        Assert.Equal(
            ((StoredEmailId?)StoredEmailId.Create(found), PresentationSupport.Supported, citation.Id),
            (Assert.IsType<AgentTaskRecording>(proposal.Act).SourceMessage, proposal.Block.Evidence.Support, Assert.Single(proposal.Block.Evidence.Citations)));
        Assert.DoesNotContain("cites no source", answer?.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Proposing a task writes the proposal and the exact act accepting it would carry out, and creates nothing.</summary>
    [Fact]
    public async Task ProposeTask_AValidTask_WritesTheProposalCarryingExactlyThatAct()
    {
        // Arrange
        var tool = this.ToolNamed("propose_task", MailFathomPermission.MailRead);

        // Act
        await tool.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?>
            {
                ["title"] = "Send the notice-period list",
                ["dueOn"] = "2026-09-24",
                ["messageId"] = null,
            }),
            TestContext.Current.CancellationToken);

        // Assert
        var proposal = Assert.Single(this.written.OfType<AgentActionProposed>());
        var recording = Assert.IsType<AgentTaskRecording>(proposal.Act);
        Assert.Equal(
            ("Send the notice-period list", new DateOnly(2026, 9, 24)),
            (recording.Title.Value, recording.DueOn));
        Assert.True(this.tools.HasProposed);
    }

    /// <summary>A task owed by no particular day is what mail asking for something without a date proposes.</summary>
    [Fact]
    public async Task ProposeTask_NoDueDay_ProposesATaskOwedByNoDay()
    {
        // Arrange
        var tool = this.ToolNamed("propose_task", MailFathomPermission.MailRead);

        // Act
        await tool.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?>
            {
                ["title"] = "Send the notice-period list",
                ["dueOn"] = null,
                ["messageId"] = null,
            }),
            TestContext.Current.CancellationToken);

        // Assert
        var proposal = Assert.Single(this.written.OfType<AgentActionProposed>());
        Assert.Null(Assert.IsType<AgentTaskRecording>(proposal.Act).DueOn);
    }

    /// <summary>A due day written any other way is refused back to the model rather than read as some other day.</summary>
    [Fact]
    public async Task ProposeTask_ADueDayThatIsNotADay_IsRefusedAndProposesNothing()
    {
        // Arrange
        var tool = this.ToolNamed("propose_task", MailFathomPermission.MailRead);

        // Act
        var answer = await tool.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?>
            {
                ["title"] = "Send the notice-period list",
                ["dueOn"] = "24 September",
                ["messageId"] = null,
            }),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.StartsWith("Give the due day as yyyy-MM-dd", answer?.ToString(), StringComparison.Ordinal);
        Assert.Empty(this.written.OfType<AgentActionProposed>());
    }

    /// <summary>An instant that states no offset is refused back to the model rather than read against this server's own time zone.</summary>
    [Theory]
    [InlineData("2026-09-22T09:00:00", "2026-09-23T09:00:00Z")]
    [InlineData("2026-09-22T09:00:00+02:00", "2026-09-23T09:00:00")]
    public async Task ReadCalendar_AnInstantWithoutAnOffset_IsRefusedWithoutReadingTheCalendar(string from, string until)
    {
        // Arrange
        var tool = this.ToolNamed("read_calendar", MailFathomPermission.MailRead);

        // Act
        var answer = await tool.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["from"] = from, ["until"] = until }),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.StartsWith("Give two ISO 8601 instants with an offset", answer?.ToString(), StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        this.journal.Dispose();

        return this.signals.DisposeAsync();
    }

    private static AIFunctionArguments Suggesting(params string[] questions) =>
        new(new Dictionary<string, object?> { ["questions"] = questions });

    private AIFunction ToolNamed(string name, params MailFathomPermission[] granted)
    {
        this.tools = this.Tools(granted);

        return Assert.IsType<AIFunction>(
            Assert.Single(this.tools.Create(), tool => tool.Name == name),
            exactMatch: false);
    }

    private AgentConversationTools Tools(params MailFathomPermission[] granted)
    {
        var retrieval = new ScopedMailKnowledgeRetrieval(
            this.knowledgeSearch,
            MailboxScope.Create([Primary], [new MailFolderIdentity(Primary, MailFolderAlias.Create("INBOX"))]),
            new MailAnsweringRunLedger(MailAnsweringRunBounds.Default),
            SensitiveContentEgressGuards.Inactive(),
            Now);
        var readers = new AgentConversationReaders(
            ScopeResolver: null!,
            KnowledgeSearch: null!,
            ContentReader: null!,
            StateBrowser: null!,
            Calendar: null!,
            Tasks: null!,
            ResponseAuthoring: null!,
            AccessAuthorizations.ForCallerGranted(granted));

        return new AgentConversationTools(this.journal, retrieval, readers, SensitiveContentEgressGuards.Inactive(), [Primary]);
    }
}
