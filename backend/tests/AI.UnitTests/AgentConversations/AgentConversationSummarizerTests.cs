// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Text;
using MailFathom.AI.AgentConversations;
using MailFathom.AI.Orchestration;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.Agent.Answering;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Resilience;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.TestSupport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MailFathom.AI.UnitTests.AgentConversations;

/// <summary>Covers what compacting a conversation sends, what it keeps of the answer, and what the turn is left with when there is none.</summary>
/// <remarks>The summariser runs over a real provider client and a scripted transport, so what is asserted is what actually left this deployment.</remarks>
public sealed class AgentConversationSummarizerTests
{
    private const string Marker = "AKIAEXAMPLEKEY";

    private static readonly AgentHistoryTurn[] Turns =
    [
        new(AgentMessageAuthor.Person, "Who sent the racking quote?"),
        new(AgentMessageAuthor.Agent, "Northwind sent it on Monday."),
    ];

    [Fact]
    public async Task SummarizeAsync_AProviderThatSummarised_AnswersWithTheSummaryAndSentTheEarlierOneWithTheTurns()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion("They asked who quoted; Northwind did, on Monday."));

        // Act
        var summary = await provider.SummarizerOver().SummarizeAsync("They talked about racking.", Turns, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("They asked who quoted; Northwind did, on Monday.", summary);
        Assert.Contains("They talked about racking.", provider.RequestBodies[0], StringComparison.Ordinal);
        Assert.Contains("Person: Who sent the racking quote?", provider.RequestBodies[0], StringComparison.Ordinal);
        Assert.Contains("Agent: Northwind sent it on Monday.", provider.RequestBodies[0], StringComparison.Ordinal);
    }

    /// <summary>A provider that refused is not the turn's failure: nothing comes back, and the turn composes from what fits.</summary>
    [Fact]
    public async Task SummarizeAsync_AProviderThatRefused_AnswersWithNothing()
    {
        // Arrange
        using var provider = ScriptedTransport.Refusing(HttpStatusCode.BadRequest);

        // Act
        var summary = await provider.SummarizerOver().SummarizeAsync(null, Turns, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(summary);
    }

    [Fact]
    public async Task SummarizeAsync_ASummaryPastTheCeiling_IsCutToIt()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(new string('x', AgentConversationSummaryInstructions.MaximumSummaryLength + 50)));

        // Act
        var summary = await provider.SummarizerOver().SummarizeAsync(null, Turns, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AgentConversationSummaryInstructions.MaximumSummaryLength, summary?.Length);
    }

    /// <summary>A conversation can hold anything a person pasted into it, so what a deployment withholds from a provider is withheld here too.</summary>
    [Fact]
    public async Task SummarizeAsync_ATurnCarryingASecret_SendsTheProviderTheGuardedText()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion("A key was shared."));
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        using var actingFor = egress.ActingForUser();

        // Act
        await provider.SummarizerOver(egress.Guard).SummarizeAsync(
            null,
            [new AgentHistoryTurn(AgentMessageAuthor.Person, $"The key is {Marker}.")],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(Marker, provider.RequestBodies[0], StringComparison.Ordinal);
    }

    private static string Completion(string content) =>
        "{\"id\":\"chatcmpl-1\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"a-chat-model\","
        + "\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\""
        + content
        + "\"},\"finish_reason\":\"stop\"}]}";

    /// <summary>A provider that answers from a script, so the summariser is exercised over a real client and no network.</summary>
    private sealed class ScriptedTransport : IDisposable
    {
        private readonly FakeHttpMessageHandler handler;
        private string payload = string.Empty;
        private HttpStatusCode status = HttpStatusCode.OK;

        private ScriptedTransport() =>
            this.handler = new FakeHttpMessageHandler(async (request, cancellationToken) =>
            {
                this.RequestBodies.Add(request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken));

                return new HttpResponseMessage(this.status)
                {
                    Content = new StringContent(this.payload, Encoding.UTF8, "application/json"),
                };
            });

        public List<string> RequestBodies { get; } = [];

        public static ScriptedTransport Answering(string payload) => new() { payload = payload };

        public static ScriptedTransport Refusing(HttpStatusCode status) =>
            new() { status = status, payload = "{\"error\":{\"message\":\"no\"}}" };

        public AgentConversationSummarizer SummarizerOver(SensitiveContentEgressGuard? egressGuard = null)
        {
            var transportFactory = Substitute.For<IHttpClientFactory>();
            transportFactory
                .CreateClient(Arg.Any<string>())
                .Returns(_ => new HttpClient(this.handler, disposeHandler: false));

            var credentialSource = Substitute.For<IProviderEndpointCredentialSource>();
            credentialSource
                .ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult(ProviderEndpointCredential.FromApiKey("a-configured-key", resolvedMaterial: null)));

            var operationRunner = Substitute.For<IOutboundOperationRunner>();
            operationRunner
                .RunAsync(
                    Arg.Any<OutboundDependency>(),
                    Arg.Any<string>(),
                    Arg.Any<Func<CancellationToken, Task<ChatResponse>>>(),
                    Arg.Any<CancellationToken>())
                .Returns(call => call.Arg<Func<CancellationToken, Task<ChatResponse>>>()!(call.Arg<CancellationToken>()));

            return new AgentConversationSummarizer(
                ChatDeclarations.Plan(),
                MailAnsweringRunBounds.Default,
                Substitute.For<IMailAnsweringSpendLedger>(),
                credentialSource,
                new OpenAiCompatibleClientFactory(),
                transportFactory,
                operationRunner,
                Substitute.For<IAiProviderHealthRecorder>(),
                egressGuard ?? SensitiveContentEgressGuards.Inactive(),
                new EmptyAgentInstructionEnvelope(),
                NullLoggerFactory.Instance,
                NullLogger<AgentConversationSummarizer>.Instance);
        }

        public void Dispose() => this.handler.Dispose();
    }
}
