// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Text;
using MailFathom.AI.Enrichment;
using MailFathom.AI.Orchestration;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Chat;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Enrichment;
using MailFathom.Application.Resilience;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MailFathom.AI.UnitTests.Enrichment;

/// <summary>Covers what one derivation sends, what it makes of the answer, and what it does when there is none.</summary>
/// <remarks>
/// The derivation goes over a real provider client and a scripted transport, so what is exercised is the client
/// construction, the credential resolution, the admission against the period, and the turn that actually left this
/// deployment.
/// </remarks>
public sealed class EmailEnrichmentAgentTests
{
    /// <summary>The literal the scanner reports, standing in for a credential a colleague pasted into a message.</summary>
    private const string Marker = "AKIAEXAMPLEKEY";

    private const string Marks = """
        {\"sense\": {\"text\": \"a racking quotation\", \"reason\": \"the passage attaches one\", \"passages\": [0]}}
        """;

    /// <summary>This one is registered only where the switch is on, so the pass in front of it queries rather than stopping.</summary>
    [Fact]
    public void IsActive_ADeploymentThatTurnedEnrichmentOn_SaysSo()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Marks));

        // Act & Assert
        Assert.True(provider.EnricherOver().IsActive);
    }

    [Fact]
    public async Task DeriveAsync_AProviderThatAnsweredWithMarks_SettlesThoseMarks()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Marks));
        var agent = provider.EnricherOver();

        // Act
        var derivation = await agent.DeriveAsync(Enrichable(), MailUserLanguage.English, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(derivation.IsSettled);
        var mark = Assert.Single(derivation.Marks);
        Assert.Equal(EmailEnrichmentAspect.Sense, mark.Aspect);
        Assert.Equal("a racking quotation", mark.Text);
        Assert.Equal(EmailEnrichmentSource.Model, mark.Provenance.Source);
        Assert.Equal(EmailEnrichmentAgentComposition.AgentName, mark.Provenance.Origin);
    }

    /// <summary>
    /// A provider that answered and was paid for buys the same answer if asked again, so the message is settled as
    /// having nothing to say rather than offered to the endpoint forever.
    /// </summary>
    [Fact]
    public async Task DeriveAsync_AProviderThatAnsweredWithProse_SettlesTheMessageWithNothingToSay()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion("I am afraid I cannot help with that."));
        var agent = provider.EnricherOver();

        // Act
        var derivation = await agent.DeriveAsync(Enrichable(), MailUserLanguage.English, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(derivation.IsSettled);
        Assert.Empty(derivation.Marks);
    }

    /// <summary>A provider that never answered decided nothing about the message, so nothing is written down.</summary>
    [Fact]
    public async Task DeriveAsync_AProviderThatRefusedTheCall_WithholdsTheDerivation()
    {
        // Arrange
        using var provider = ScriptedTransport.Refusing(HttpStatusCode.BadRequest);
        var agent = provider.EnricherOver();

        // Act
        var derivation = await agent.DeriveAsync(Enrichable(), MailUserLanguage.English, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailEnrichmentWithholding.ProviderUnavailable, derivation.Withheld);
    }

    [Fact]
    public async Task DeriveAsync_ACredentialThatCannotBeResolved_WithholdsWithoutReachingTheEndpoint()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Marks));
        var agent = provider.EnricherOver(credentialFailure: new InvalidOperationException(
            "The provider key of AI endpoint 'a-chat-endpoint' could not be resolved."));

        // Act
        var derivation = await agent.DeriveAsync(Enrichable(), MailUserLanguage.English, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailEnrichmentWithholding.ProviderUnavailable, derivation.Withheld);
        Assert.Equal(0, provider.RequestCount);
    }

    /// <summary>
    /// Enrichment is admitted against the same period ceilings a question is, which is what stops it from being a
    /// second, unmetered way of spending a deployment's allowance.
    /// </summary>
    [Fact]
    public async Task DeriveAsync_APeriodThatHasSpentItsAllowance_WithholdsBeforeReachingTheProvider()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Marks));
        var spendLedger = Substitute.For<IMailAnsweringSpendLedger>();
        spendLedger.TryAdmitRunAsync(Arg.Any<CancellationToken>()).Returns(false);
        var agent = provider.EnricherOver(spendLedger: spendLedger);

        // Act
        var derivation = await agent.DeriveAsync(Enrichable(), MailUserLanguage.English, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailEnrichmentWithholding.AllowanceExhausted, derivation.Withheld);
        Assert.Equal(0, provider.RequestCount);
    }

    [Fact]
    public async Task DeriveAsync_AProviderThatReportedItsUsage_ChargesTheCallToThePeriod()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Marks, inputTokens: 11, outputTokens: 7));
        var spendLedger = Substitute.For<IMailAnsweringSpendLedger>();
        spendLedger.TryAdmitRunAsync(Arg.Any<CancellationToken>()).Returns(true);
        var agent = provider.EnricherOver(spendLedger: spendLedger);

        // Act
        await agent.DeriveAsync(Enrichable(), MailUserLanguage.English, TestContext.Current.CancellationToken);

        // Assert
        await spendLedger.Received(1).RecordSpendAsync(new ChatTokenUsage(11, 7), Arg.Any<CancellationToken>());
    }

    /// <summary>A message with nothing cut from it has no evidence to cite, so it costs no call at all.</summary>
    [Fact]
    public async Task DeriveAsync_AMessageWithNoPassages_SettlesWithoutReachingTheProvider()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Marks));
        var agent = provider.EnricherOver();
        var withoutPassages = new EnrichableEmail(
            StoredEmailId.Create(Guid.CreateVersion7()),
            "The racking quotation",
            new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero),
            []);

        // Act
        var derivation = await agent.DeriveAsync(withoutPassages, MailUserLanguage.English, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(derivation.IsSettled);
        Assert.Equal(0, provider.RequestCount);
    }

    /// <summary>A passage is somebody's mail, so what a deployment withholds from a provider is withheld here too.</summary>
    [Fact]
    public async Task DeriveAsync_AMessageCarryingASecret_SendsTheProviderTheGuardedText()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Marks));
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        using var actingFor = egress.ActingForUser();
        var agent = provider.EnricherOver(egressGuard: egress.Guard);
        var carryingASecret = new EnrichableEmail(
            StoredEmailId.Create(Guid.CreateVersion7()),
            $"the key {Marker}",
            new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero),
            [
                new EnrichablePassage(
                    EmailChunkId.Create(Guid.CreateVersion7()),
                    0,
                    $"a colleague pasted {Marker} into this thread"),
            ]);

        // Act
        await agent.DeriveAsync(carryingASecret, MailUserLanguage.English, TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(Marker, provider.RequestBodies[0], StringComparison.Ordinal);
    }

    /// <summary>One message is one call, because the agent reaches no tool and is shown nothing else.</summary>
    [Fact]
    public async Task DeriveAsync_AnyMessage_ReachesTheProviderExactlyOnce()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Marks));
        var agent = provider.EnricherOver();

        // Act
        await agent.DeriveAsync(Enrichable(), MailUserLanguage.English, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, provider.RequestCount);
    }

    private static EnrichableEmail Enrichable() =>
        new(
            StoredEmailId.Create(Guid.CreateVersion7()),
            "The racking quotation",
            new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero),
            [new EnrichablePassage(EmailChunkId.Create(Guid.CreateVersion7()), 0, "the quotation is attached")]);

    /// <summary>Builds the chat-completion payload a provider answers with.</summary>
    private static string Completion(string content, int? inputTokens = null, int? outputTokens = null) =>
        "{\"id\":\"chatcmpl-1\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"a-chat-model\","
        + "\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\""
        + content.ReplaceLineEndings(string.Empty)
        + "\"},\"finish_reason\":\"stop\"}]"
        + (inputTokens is null
            ? string.Empty
            : $",\"usage\":{{\"prompt_tokens\":{inputTokens},\"completion_tokens\":{outputTokens},"
              + $"\"total_tokens\":{inputTokens + outputTokens}}}")
        + "}";

    /// <summary>A provider that answers from a script, so the derivation is exercised over a real client and no network.</summary>
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

        public EmailEnrichmentAgent EnricherOver(
            SensitiveContentEgressGuard? egressGuard = null,
            Exception? credentialFailure = null,
            IMailAnsweringSpendLedger? spendLedger = null)
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

            return new EmailEnrichmentAgent(
                ChatDeclarations.Plan(),
                MailAnsweringRunBounds.Default,
                spendLedger ?? AdmittingSpendLedger(),
                credentialSource,
                new OpenAiCompatibleClientFactory(),
                transportFactory,
                operationRunner,
                Substitute.For<IAiProviderHealthRecorder>(),
                egressGuard ?? SensitiveContentEgressGuards.Inactive(),
                new EmptyAgentInstructionEnvelope(),
                NullLoggerFactory.Instance,
                NullLogger<EmailEnrichmentAgent>.Instance);
        }

        public void Dispose() => this.handler.Dispose();

        private static IMailAnsweringSpendLedger AdmittingSpendLedger()
        {
            var spendLedger = Substitute.For<IMailAnsweringSpendLedger>();
            spendLedger.TryAdmitRunAsync(Arg.Any<CancellationToken>()).Returns(true);

            return spendLedger;
        }
    }
}
