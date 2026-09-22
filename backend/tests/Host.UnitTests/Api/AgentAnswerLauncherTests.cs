// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Agent.Answering;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Domain.Access;
using MailFathom.Host.Api;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what becomes of an answer this process could not start composing at all, which nothing else would report.</summary>
/// <remarks>
/// Nothing in a request awaits the task the launcher starts, so a fault escaping it would be observed by nobody and would
/// leave the answer composing forever. What is asserted is that the fault is written down without the question, and the
/// answer is ended as failed.
/// </remarks>
public sealed class AgentAnswerLauncherTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private static readonly AgentQuestion Question = new(
        AgentConversationId.New(),
        SyntheticUser.Deployment,
        PresentationText.Create("which supplier quoted least"),
        Scope: null,
        AgentMessageId.New(),
        OpenedAt: 3,
        Now);

    private readonly IAgentConversationStore store = Substitute.For<IAgentConversationStore>();

    private readonly RecordingLogger<AgentAnswerLauncher> logger = new();

    /// <summary>A scope this process could not compose ends the answer as failed, rather than leaving it composing.</summary>
    [Fact]
    public async Task Start_AScopeThisProcessCannotCompose_EndsTheAnswerAsFailed()
    {
        // Arrange
        this.store
            .AppendAsync(Question.Conversation, Question.User, Arg.Any<AgentConversationEntry>(), Now, Arg.Any<CancellationToken>())
            .Returns(4L);

        // Act
        await this.Launcher().Start(Question, Caller);

        // Assert
        await this.store.Received(1).AppendAsync(
            Question.Conversation,
            Question.User,
            new AgentAnswerEnded(Question.Answer, AgentAnswerOutcome.Failed),
            Now,
            Arg.Any<CancellationToken>());
    }

    /// <summary>The failure the conversation says nothing about is written down for the operator, and it carries none of the question.</summary>
    [Fact]
    public async Task Start_AScopeThisProcessCannotCompose_WritesTheFaultDownWithoutTheQuestion()
    {
        // Act
        await this.Launcher().Start(Question, Caller);

        // Assert
        var written = Assert.Single(this.logger.Messages);
        Assert.Contains("had no name for", written, StringComparison.Ordinal);
        Assert.DoesNotContain("supplier", written, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An ending the store cannot write is written down too, and nothing escapes into the task nobody awaits.</summary>
    [Fact]
    public async Task Start_AnEndingTheStoreCannotWrite_IsWrittenDownAndEscapesNothing()
    {
        // Arrange
        this.store
            .AppendAsync(Question.Conversation, Question.User, Arg.Any<AgentConversationEntry>(), Now, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<long?>(new InvalidOperationException("the record could not be written")));

        // Act
        await this.Launcher().Start(Question, Caller);

        // Assert
        Assert.Equal(2, this.logger.Messages.Count);
        Assert.Contains("its ending could not be written", this.logger.Messages[1], StringComparison.Ordinal);
    }

    private static AuthorizedPrincipal Caller =>
        AuthorizedPrincipal.CallerActingFor(SyntheticUser.Deployment, "test-caller", [MailFathomPermission.MailAsk]);

    private AgentAnswerLauncher Launcher()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(_ => throw new InvalidOperationException("no scope was composed"));

        return new AgentAnswerLauncher(
            scopeFactory,
            this.store,
            ClientSignalPublishers.ReachingNobody,
            Substitute.For<IUserLanguages>(),
            Substitute.For<IHostApplicationLifetime>(),
            new FakeTimeProvider(Now),
            this.logger);
    }
}
