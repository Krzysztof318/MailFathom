// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.AgentConversations;
using MailFathom.AI.Retrieval;
using MailFathom.Application.Access;
using MailFathom.Application.Agent.Answering;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.Signals;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.AI.UnitTests.AgentConversations;

/// <summary>Covers which tools one Agent run is offered and what a proposing tool leaves behind.</summary>
/// <remarks>
/// The readers other than the authorization are left unset because nothing asserted here reads mail: which tools exist is
/// decided by the grant alone, and a proposal is written from the arguments the model supplied.
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

    private readonly ClientSignals signals;

    private readonly AgentAnswerJournal journal;

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

    /// <summary>A grant that only reads is offered the reading tools and nothing that proposes.</summary>
    [Fact]
    public void Create_AGrantThatOnlyReads_OffersNoProposingTool()
    {
        // Act
        var names = this.Tools(MailFathomPermission.MailRead).Create().Select(static tool => tool.Name);

        // Assert
        Assert.Equal(["search_mail", "read_thread", "show_thread_state", "read_calendar", "read_tasks"], names);
    }

    /// <summary>A grant that drafts and sends but reads nothing may propose a new message and cannot answer one it could not read.</summary>
    [Fact]
    public void Create_AGrantThatSendsButDoesNotRead_OffersOnlyANewMessage()
    {
        // Act
        var names = this.Tools(MailFathomPermission.MailDraftsWrite, MailFathomPermission.MailSend).Create().Select(static tool => tool.Name);

        // Assert
        Assert.Equal(["propose_message"], names);
    }

    /// <summary>A grant that drafts without sending is offered nothing to propose, because accepting it could not be carried out.</summary>
    [Fact]
    public void Create_AGrantThatCannotSend_OffersNoProposingTool()
    {
        // Act
        var names = this.Tools(MailFathomPermission.MailRead, MailFathomPermission.MailDraftsWrite).Create().Select(static tool => tool.Name);

        // Assert
        Assert.DoesNotContain(names, static name => name.StartsWith("propose_", StringComparison.Ordinal));
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
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        this.journal.Dispose();

        return this.signals.DisposeAsync();
    }

    private AIFunction ToolNamed(string name, params MailFathomPermission[] granted) =>
        Assert.IsType<AIFunction>(
            Assert.Single(this.Tools(granted).Create(), tool => tool.Name == name),
            exactMatch: false);

    private AgentConversationTools Tools(params MailFathomPermission[] granted)
    {
        var retrieval = new ScopedMailKnowledgeRetrieval(
            Substitute.For<IEmailKnowledgeSearch>(),
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
