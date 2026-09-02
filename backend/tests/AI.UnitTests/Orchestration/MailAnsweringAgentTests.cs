// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Security.Cryptography;
using System.Text;
using MailFathom.AI.Chat;
using MailFathom.AI.Orchestration;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Chat;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Resilience;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Answering.Audit;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MailFathom.AI.UnitTests.Orchestration;

/// <summary>Covers what one answering run does around the composed agent: what it bounds, what it opens, and what it publishes.</summary>
/// <remarks>
/// The run goes over a real provider client and a scripted transport, so what is exercised is the client construction,
/// the credential resolution, and the mapping of a provider's answer. What the agent itself is composed of is proved
/// against a substituted chat client instead, where the tool loop can be driven directly.
/// </remarks>
public sealed class MailAnsweringAgentTests
{
    private static readonly MailQuestion Question = new(
        MailQuestionText.Create("was the invoice attached"),
        MailboxScope.Create(SyntheticMailOwner.Deployment, [MailAccountId.Create("primary")], []));

    /// <summary>The literal the scanner in the guarded-egress tests reports, standing in for a credential in mail.</summary>
    private const string Marker = "AKIAEXAMPLEKEY";

    private static readonly DateTimeOffset RunStartedAt = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AnswerAsync_AProviderThatAnswered_ReturnsTheAnswerAndWhatTheRunRetrieved()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion("The invoice was attached."));
        var agent = provider.AgentOver(new RecordingEmailKnowledgeSearch());
        var observation = Observation();

        // Act
        var answer = await agent.AnswerAsync(Question, observation, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("The invoice was attached.", answer.Text);
        Assert.Empty(observation.Retrieval.Passages);
        provider.HealthRecorder.Received(1).RecordServed(AiProviderRole.Chat);
    }

    /// <summary>An empty string reaching a caller reads as a model that had nothing to say rather than as a run that produced nothing.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnswerAsync_AProviderThatProducedNoText_IsAnEmptyAnswerFailure(string content)
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(content));
        var agent = provider.AgentOver(new RecordingEmailKnowledgeSearch());

        // Act
        var failure = await Assert.ThrowsAsync<ChatGenerationFailedException>(() =>
            agent.AnswerAsync(Question, Observation(), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(ChatGenerationFailure.AnswerEmpty, failure.Failure);
    }

    /// <summary>
    /// The record of a run that failed is the one most worth having, so what the run was conducted with is written down
    /// before anything that can fail and the retrieval is reported however the run ended.
    /// </summary>
    [Fact]
    public async Task AnswerAsync_ARunThatProducedNoAnswer_StillReportsWhatItWasConductedWith()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(string.Empty));
        var agent = provider.AgentOver(new RecordingEmailKnowledgeSearch());
        var observation = Observation();

        // Act
        await Assert.ThrowsAsync<ChatGenerationFailedException>(() =>
            agent.AnswerAsync(Question, observation, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("answering", observation.ChatEndpointAlias);
        Assert.Equal(MailAnsweringInstructions.Version, observation.InstructionsVersion);
        Assert.Empty(observation.Retrieval.Passages);
    }

    /// <summary>The version has to move with the text, or a record would name a policy an answer was not produced under.</summary>
    [Fact]
    public void InstructionsVersion_IsDerivedFromTheInstructionItNames()
    {
        // Arrange, Act
        var version = MailAnsweringInstructions.Version;

        // Assert
        Assert.Equal(
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(MailAnsweringInstructions.Text)))[..12],
            version);
    }

    /// <summary>The question is one turn, so what bounds a turn bounds the question, and it is refused before anything is sent.</summary>
    /// <remarks>
    /// A question this large is one a deployment declared a smaller conversation than, rather than one no caller could
    /// write: the question's own bound is the use case's and is far below this. Both exist because they answer different
    /// questions — what a caller may ask, and what this endpoint may be sent.
    /// </remarks>
    [Fact]
    public async Task AnswerAsync_AQuestionLargerThanOneCallSends_IsRefusedWithoutReachingTheProvider()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion("never reached"));
        var agent = provider.AgentOver(
            new RecordingEmailKnowledgeSearch(),
            ChatDeclarations.Plan(maximumRequestCharacters: 100));

        // Act
        await Assert.ThrowsAsync<ArgumentException>(() => agent.AnswerAsync(
            Question with { Text = MailQuestionText.Create(new string('a', 500)) },
            Observation(),
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(0, provider.RequestCount);
    }

    /// <summary>A question is text somebody typed into a client, and it leaves this deployment as completely as an extract does.</summary>
    [Fact]
    public async Task AnswerAsync_ASwitchedOnScanner_RedactsTheQuestionBeforeItReachesTheProvider()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        using var actingFor = egress.ActingForOwner();
        using var provider = ScriptedTransport.Answering(Completion("It is rotated now."));
        var agent = provider.AgentOver(new RecordingEmailKnowledgeSearch(), egressGuard: egress.Guard);

        // Act
        await agent.AnswerAsync(
            Question with { Text = MailQuestionText.Create($"is the key {Marker} still valid") },
            Observation(),
            TestContext.Current.CancellationToken);

        // Assert
        var body = Assert.Single(provider.RequestBodies);

        Assert.DoesNotContain(Marker, body, StringComparison.Ordinal);
        Assert.Contains("[redacted:CloudKey]", body, StringComparison.Ordinal);
    }

    /// <summary>Sending a prompt a scanner could not read would be the leak the switch was turned on to prevent.</summary>
    [Fact]
    public async Task AnswerAsync_ADetectorThatCannotAnswer_RefusesTheRunWithoutReachingTheProvider()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Unavailable(TimeProvider.System);
        using var actingFor = egress.ActingForOwner();
        using var provider = ScriptedTransport.Answering(Completion("never reached"));
        var agent = provider.AgentOver(new RecordingEmailKnowledgeSearch(), egressGuard: egress.Guard);

        // Act
        await Assert.ThrowsAsync<SensitiveContentScannerUnavailableException>(() =>
            agent.AnswerAsync(Question, Observation(), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(0, provider.RequestCount);
    }

    /// <summary>An opt-in nobody took must not appear on this path at all.</summary>
    [Fact]
    public async Task AnswerAsync_ADeploymentThatScansNothing_SendsTheQuestionAsItWasAsked()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion("It is rotated now."));
        var agent = provider.AgentOver(new RecordingEmailKnowledgeSearch());

        // Act
        await agent.AnswerAsync(
            Question with { Text = MailQuestionText.Create($"is the key {Marker} still valid") },
            Observation(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(Marker, Assert.Single(provider.RequestBodies), StringComparison.Ordinal);
    }

    /// <summary>Opens the record of one run, over the same scope every question here carries.</summary>
    private static MailAnsweringRunObservation Observation() => new(
        MailAnsweringRunId.Create(Guid.CreateVersion7()),
        Question.Scope,
        RunStartedAt);

    /// <summary>Builds the chat-completion payload a provider answers with.</summary>
    private static string Completion(string content) =>
        "{\"id\":\"chatcmpl-1\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"a-chat-model\","
        + "\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\""
        + content
        + "\"},\"finish_reason\":\"stop\"}]}";

    /// <summary>A provider that answers from a script, so the run is exercised over a real client and no network.</summary>
    private sealed class ScriptedTransport : IDisposable
    {
        private readonly FakeHttpMessageHandler handler;
        private readonly List<string> requestBodies = [];
        private string payload = string.Empty;

        private ScriptedTransport() =>
            this.handler = new FakeHttpMessageHandler(async (request, cancellationToken) =>
            {
                this.RequestCount++;
                this.requestBodies.Add(request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken));

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(this.payload, Encoding.UTF8, "application/json"),
                };
            });

        /// <summary>The health state every call this provider serves reports into, so a test can read what the run established.</summary>
        public IAiProviderHealthRecorder HealthRecorder { get; } = Substitute.For<IAiProviderHealthRecorder>();

        /// <summary>The period ledger every call of the run is charged to, so a test can read what it was told the run spent.</summary>
        public IMailAnsweringSpendLedger SpendLedger { get; } = Substitute.For<IMailAnsweringSpendLedger>();

        public int RequestCount { get; private set; }

        /// <summary>What the provider was actually sent, which is where a test reads whether anything left unredacted.</summary>
        public IReadOnlyList<string> RequestBodies => this.requestBodies;

        public static ScriptedTransport Answering(string payload) => new() { payload = payload };

        public MailAnsweringAgent AgentOver(
            IEmailKnowledgeSearch knowledgeSearch,
            ChatGenerationPlan? plan = null,
            MailAnsweringRunBounds? runBounds = null,
            SensitiveContentEgressGuard? egressGuard = null)
        {
            var transportFactory = Substitute.For<IHttpClientFactory>();
            transportFactory
                .CreateClient(Arg.Any<string>())
                .Returns(_ => new HttpClient(this.handler, disposeHandler: false));

            var credentialSource = Substitute.For<IProviderEndpointCredentialSource>();
            credentialSource
                .ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult(
                    ProviderEndpointCredential.FromApiKey("a-configured-key", resolvedMaterial: null)));

            var operationRunner = Substitute.For<IOutboundOperationRunner>();
            operationRunner
                .RunAsync(
                    Arg.Any<OutboundDependency>(),
                    Arg.Any<string>(),
                    Arg.Any<Func<CancellationToken, Task<Microsoft.Extensions.AI.ChatResponse>>>(),
                    Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    // The substitute cannot know the argument is present, and it always is: this configuration matches
                    // the only overload the decorator calls.
                    var operation = call.Arg<Func<CancellationToken, Task<Microsoft.Extensions.AI.ChatResponse>>>()!;

                    return operation(call.Arg<CancellationToken>());
                });

            return new MailAnsweringAgent(
                plan ?? ChatDeclarations.Plan(),
                runBounds ?? MailAnsweringRunBounds.Default,
                credentialSource,
                new OpenAiCompatibleClientFactory(),
                transportFactory,
                knowledgeSearch,
                operationRunner,
                this.HealthRecorder,
                this.SpendLedger,
                egressGuard ?? SensitiveContentEgressGuards.Inactive(),
                NullLoggerFactory.Instance,
                NullLogger<MailAnsweringAgent>.Instance);
        }

        public void Dispose() => this.handler.Dispose();
    }
}
