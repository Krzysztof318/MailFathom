// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Text;
using MailFathom.AI.Discovery;
using MailFathom.AI.Orchestration;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Discovery.Planning;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Presentation.Blocks;
using MailFathom.Application.Discovery.Runs;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Resilience;
using MailFathom.Application.Retrieval;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MailFathom.AI.UnitTests.Discovery;

/// <summary>Covers what one composition sends, what it makes of the answer, and what it does when there is none.</summary>
/// <remarks>
/// The composition goes over a real provider client and a scripted transport, so what is exercised is the client
/// construction, the credential resolution, and the mail that actually left this deployment.
/// </remarks>
public sealed class DiscoveryCompositionAgentTests
{
    /// <summary>The literal the scanner in the guarded-egress test reports, standing in for a credential in an extract.</summary>
    private const string Marker = "AKIAEXAMPLEKEY";

    private static readonly DateTimeOffset ObservedAt = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    private static readonly MailAccountId Primary = MailAccountId.Create("primary");

    [Fact]
    public async Task ComposeAsync_AProviderThatAnsweredFromTheExtracts_ComposesThatAnswer()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(
            """{\"answer\": \"They accepted the revised figure.\", \"confidence\": \"high\", \"sources\": [\"s1\"]}"""));
        var composer = provider.ComposerOver();

        // Act
        var plan = await composer.ComposeAsync(
            Question(),
            Plan(DiscoveryIntent.FindFact),
            Evidence("we accept the revised figure"),
            Coverage(),
            TestContext.Current.CancellationToken);

        // Assert
        var block = Assert.IsType<AnswerBlock>(plan.Blocks[0]);
        Assert.Equal("They accepted the revised figure.", block.Text.Value);
        Assert.Equal(PresentationSupport.Supported, block.Evidence.Support);
    }

    /// <summary>A provider that refused the call costs the answer, and the result says exactly that rather than failing.</summary>
    [Fact]
    public async Task ComposeAsync_AProviderThatRefusedTheCall_ComposesAResultSayingTheMailDoesNotAnswer()
    {
        // Arrange
        using var provider = ScriptedTransport.Refusing(HttpStatusCode.BadRequest);
        var composer = provider.ComposerOver();

        // Act
        var plan = await composer.ComposeAsync(
            Question(),
            Plan(DiscoveryIntent.FindFact),
            Evidence("we accept the revised figure"),
            Coverage(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(PresentationSupport.Unsupported, plan.Blocks[0].Evidence.Support);
        Assert.Single(plan.Citations);
    }

    /// <summary>A key that would not resolve never reaches the endpoint, and is the same honest answer a refusal is.</summary>
    [Fact]
    public async Task ComposeAsync_ACredentialThatCannotBeResolved_ComposesAResultOverWhatTheRunRead()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(
            """{\"answer\": \"They accepted.\", \"sources\": [\"s1\"]}"""));
        var composer = provider.ComposerOver(credentialFailure: new InvalidOperationException(
            "The provider key of AI endpoint 'a-chat-endpoint' could not be resolved."));

        // Act
        var plan = await composer.ComposeAsync(
            Question(),
            Plan(DiscoveryIntent.FindFact),
            Evidence("we accept the revised figure"),
            Coverage(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(PresentationSupport.Unsupported, plan.Blocks[0].Evidence.Support);
        Assert.Equal(0, provider.RequestCount);
    }

    /// <summary>An extract is mail leaving this deployment, so what a deployment withholds from a provider is withheld here too.</summary>
    [Fact]
    public async Task ComposeAsync_AnExtractCarryingASecret_SendsTheProviderTheGuardedText()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(
            """{\"answer\": \"They accepted.\", \"sources\": [\"s1\"]}"""));
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        using var actingFor = egress.ActingForOwner();
        var composer = provider.ComposerOver(egressGuard: egress.Guard);

        // Act
        await composer.ComposeAsync(
            Question(),
            Plan(DiscoveryIntent.FindFact),
            Evidence($"the key is {Marker} as agreed"),
            Coverage(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(Marker, provider.RequestBodies[0], StringComparison.Ordinal);
    }

    /// <summary>The question is the owner's own words and leaves this deployment in the same turn, so it is guarded like an extract.</summary>
    [Fact]
    public async Task ComposeAsync_AQuestionCarryingASecret_SendsTheProviderTheGuardedQuestion()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(
            """{\"answer\": \"They accepted.\", \"sources\": [\"s1\"]}"""));
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        using var actingFor = egress.ActingForOwner();
        var composer = provider.ComposerOver(egressGuard: egress.Guard);

        // Act
        await composer.ComposeAsync(
            Question($"was {Marker} the key they sent"),
            Plan(DiscoveryIntent.FindFact),
            Evidence("we accept the revised figure"),
            Coverage(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(Marker, provider.RequestBodies[0], StringComparison.Ordinal);
    }

    /// <summary>What a plan quotes is the owner's own mail going back to the owner, so it is not redacted on the way.</summary>
    [Fact]
    public async Task ComposeAsync_AnExtractCarryingASecret_StillQuotesItBackToTheOwner()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(
            """{\"answer\": \"They accepted.\", \"sources\": [\"s1\"]}"""));
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        using var actingFor = egress.ActingForOwner();
        var composer = provider.ComposerOver(egressGuard: egress.Guard);

        // Act
        var plan = await composer.ComposeAsync(
            Question(),
            Plan(DiscoveryIntent.FindFact),
            Evidence($"the key is {Marker} as agreed"),
            Coverage(),
            TestContext.Current.CancellationToken);

        // Assert
        var block = Assert.IsType<EvidenceListBlock>(plan.Blocks[1]);
        Assert.Contains(Marker, block.Entries[0].Fragment.Value, StringComparison.Ordinal);
    }

    /// <summary>One turn with no tools, so one question costs one call however much mail it read.</summary>
    [Fact]
    public async Task ComposeAsync_AnyQuestion_ReachesTheProviderExactlyOnce()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(
            """{\"answer\": \"They accepted.\", \"sources\": [\"s1\"]}"""));
        var composer = provider.ComposerOver();

        // Act
        await composer.ComposeAsync(
            Question(),
            Plan(DiscoveryIntent.FindFact),
            Evidence("we accept", "we will revert", "the figure stands"),
            Coverage(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, provider.RequestCount);
    }

    private static MailQuestion Question(string text = "which supplier quoted least") =>
        new(
            MailQuestionText.Create(text),
            MailboxScope.Create(SyntheticMailOwner.Deployment, [Primary], []));

    private static DiscoveryRunPlan Plan(DiscoveryIntent intent) =>
        DiscoveryRunPlan.Compose(
            intent,
            RetrievalPlan.Create(
                EmailKnowledgeBounds.Default,
                [EmailKnowledgeQuery.ForText("quotation")],
                sufficientPassages: 5));

    private static DiscoveryEvidence Evidence(params string[] extracts) =>
        new(
            [.. extracts.Select(Passage)],
            EmailSearchRetrievalMode.Hybrid,
            LookupsRun: 1,
            LookupsRefused: 0);

    private static IReadOnlyList<AccountCoverage> Coverage() =>
    [
        new(
            PresentationText.Create(Primary.Value),
            PresentationFreshness.CurrentAt(ObservedAt),
            earliestReceivedAt: null,
            latestReceivedAt: null),
    ];

    private static EmailKnowledgePassage Passage(string text) => new()
    {
        StoredEmailId = StoredEmailId.Create(Guid.CreateVersion7()),
        AccountId = Primary,
        FolderAlias = MailFolderAlias.Create("INBOX"),
        Subject = null,
        ReceivedAt = null,
        SenderVerification = SenderVerification.NotEstablished,
        MachineAuthorship = MachineAuthorshipAssessment.NotAssessed,
        Text = text,
    };

    /// <summary>Builds the chat-completion payload a provider answers with.</summary>
    private static string Completion(string content) =>
        "{\"id\":\"chatcmpl-1\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"a-chat-model\","
        + "\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\""
        + content
        + "\"},\"finish_reason\":\"stop\"}]}";

    /// <summary>A provider that answers from a script, so the composition is exercised over a real client and no network.</summary>
    private sealed class ScriptedTransport : IDisposable
    {
        private readonly FakeHttpMessageHandler handler;
        private string payload = string.Empty;
        private HttpStatusCode status = HttpStatusCode.OK;

        private ScriptedTransport() =>
            this.handler = new FakeHttpMessageHandler(async (request, cancellationToken) =>
            {
                this.RequestCount++;
                this.RequestBodies.Add(request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken));

                return new HttpResponseMessage(this.status)
                {
                    Content = new StringContent(this.payload, Encoding.UTF8, "application/json"),
                };
            });

        public int RequestCount { get; private set; }

        /// <summary>What the provider was actually sent, which is where a test reads what left this deployment.</summary>
        public List<string> RequestBodies { get; } = [];

        public static ScriptedTransport Answering(string payload) => new() { payload = payload };

        public static ScriptedTransport Refusing(HttpStatusCode status) =>
            new() { status = status, payload = "{\"error\":{\"message\":\"no\"}}" };

        public DiscoveryCompositionAgent ComposerOver(
            SensitiveContentEgressGuard? egressGuard = null,
            Exception? credentialFailure = null)
        {
            var transportFactory = Substitute.For<IHttpClientFactory>();
            transportFactory
                .CreateClient(Arg.Any<string>())
                .Returns(_ => new HttpClient(this.handler, disposeHandler: false));

            var credentialSource = Substitute.For<IProviderEndpointCredentialSource>();
            credentialSource
                .ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(_ => credentialFailure is null
                    ? Task.FromResult(ProviderEndpointCredential.FromApiKey("a-configured-key", resolvedMaterial: null))
                    : Task.FromException<ProviderEndpointCredential>(credentialFailure));

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

            return new DiscoveryCompositionAgent(
                ChatDeclarations.Plan(),
                credentialSource,
                new OpenAiCompatibleClientFactory(),
                transportFactory,
                operationRunner,
                Substitute.For<IAiProviderHealthRecorder>(),
                egressGuard ?? SensitiveContentEgressGuards.Inactive(),
                new EmptyAgentInstructionEnvelope(),
                NullLoggerFactory.Instance,
                NullLogger<DiscoveryCompositionAgent>.Instance);
        }

        public void Dispose() => this.handler.Dispose();
    }
}
