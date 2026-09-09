// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Text;
using MailFathom.AI.Orchestration;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.AI.ThreadStates;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Chat;
using MailFathom.Application.Emails.ThreadStates;
using MailFathom.Application.Resilience;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MailFathom.AI.UnitTests.ThreadStates;

/// <summary>Covers what one derivation sends, what it makes of the answer, and what it does when there is none.</summary>
/// <remarks>
/// The derivation goes over a real provider client and a scripted transport, so what is exercised is the client
/// construction, the credential resolution, the admission against the period, and the turn that actually left this
/// deployment.
/// </remarks>
public sealed class ThreadStateAgentTests
{
    /// <summary>The literal the scanner reports, standing in for a credential a colleague pasted into a conversation.</summary>
    private const string Marker = "AKIAEXAMPLEKEY";

    private const string Statements = """
        {\"agreements\": [{\"text\": \"The response time stays at two hours.\", \"messages\": [0]}]}
        """;

    /// <summary>This one is registered only where the switch is on, so the pass in front of it queries rather than stopping.</summary>
    [Fact]
    public void IsActive_ADeploymentThatTurnedTheDerivationOn_SaysSo()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Statements));

        // Act and assert
        Assert.True(provider.DeriverOver().IsActive);
    }

    [Fact]
    public async Task DeriveAsync_AProviderThatAnsweredWithStatements_SettlesThoseStatements()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Statements));
        var agent = provider.DeriverOver();
        var thread = Derivable();

        // Act
        var derivation = await agent.DeriveAsync(thread, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(derivation.IsSettled);
        Assert.Equal(ThreadStateCoverage.WholeThread, derivation.Coverage);
        var entry = Assert.Single(derivation.Entries);
        Assert.Equal(ThreadStateAspect.Agreement, entry.Aspect);
        Assert.Equal("The response time stays at two hours.", entry.Text);
        Assert.Equal([thread.Messages[0].StoredEmailId], entry.Sources);
    }

    /// <summary>
    /// A conversation past the bound is answered before anything is admitted, composed, or sent: the honest answer is
    /// that it is too long, rather than a state derived from the part of it that fits.
    /// </summary>
    [Fact]
    public async Task DeriveAsync_AConversationPastTheBound_RecordsItAsTooLargeWithoutReachingTheProvider()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Statements));
        var spendLedger = Substitute.For<IMailAnsweringSpendLedger>();
        spendLedger.TryAdmitRun().Returns(true);
        var agent = provider.DeriverOver(spendLedger: spendLedger);
        var thread = Derivable() with { ExceedsBound = true };

        // Act
        var derivation = await agent.DeriveAsync(thread, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(derivation.IsSettled);
        Assert.Equal(ThreadStateCoverage.ThreadTooLarge, derivation.Coverage);
        Assert.Empty(derivation.Entries);
        Assert.Equal(0, provider.RequestCount);
        spendLedger.DidNotReceive().TryAdmitRun();
    }

    /// <summary>
    /// A provider that answered and was paid for buys the same answer if asked again, so the conversation is settled as
    /// having nothing to say rather than offered to the endpoint forever.
    /// </summary>
    [Fact]
    public async Task DeriveAsync_AProviderThatAnsweredWithProse_SettlesTheConversationWithNothingToSay()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion("I am afraid I cannot help with that."));
        var agent = provider.DeriverOver();

        // Act
        var derivation = await agent.DeriveAsync(Derivable(), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(derivation.IsSettled);
        Assert.Empty(derivation.Entries);
    }

    /// <summary>A provider that never answered decided nothing about the conversation, so nothing is written down.</summary>
    [Fact]
    public async Task DeriveAsync_AProviderThatRefusedTheCall_WithholdsTheDerivation()
    {
        // Arrange
        using var provider = ScriptedTransport.Refusing(HttpStatusCode.BadRequest);
        var agent = provider.DeriverOver();

        // Act
        var derivation = await agent.DeriveAsync(Derivable(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ThreadStateWithholding.ProviderUnavailable, derivation.Withheld);
    }

    [Fact]
    public async Task DeriveAsync_ACredentialThatCannotBeResolved_WithholdsWithoutReachingTheEndpoint()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Statements));
        var agent = provider.DeriverOver(credentialFailure: new InvalidOperationException(
            "The provider key of AI endpoint 'a-chat-endpoint' could not be resolved."));

        // Act
        var derivation = await agent.DeriveAsync(Derivable(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ThreadStateWithholding.ProviderUnavailable, derivation.Withheld);
        Assert.Equal(0, provider.RequestCount);
    }

    /// <summary>
    /// A derivation is admitted against the same period ceilings a question is, which is what stops it from being a
    /// second, unmetered way of spending a deployment's allowance.
    /// </summary>
    [Fact]
    public async Task DeriveAsync_APeriodThatHasSpentItsAllowance_WithholdsBeforeReachingTheProvider()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Statements));
        var spendLedger = Substitute.For<IMailAnsweringSpendLedger>();
        spendLedger.TryAdmitRun().Returns(false);
        var agent = provider.DeriverOver(spendLedger: spendLedger);

        // Act
        var derivation = await agent.DeriveAsync(Derivable(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ThreadStateWithholding.AllowanceExhausted, derivation.Withheld);
        Assert.Equal(0, provider.RequestCount);
    }

    [Fact]
    public async Task DeriveAsync_AProviderThatReportedItsUsage_ChargesTheCallToThePeriod()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Statements, inputTokens: 11, outputTokens: 7));
        var spendLedger = Substitute.For<IMailAnsweringSpendLedger>();
        spendLedger.TryAdmitRun().Returns(true);
        var agent = provider.DeriverOver(spendLedger: spendLedger);

        // Act
        await agent.DeriveAsync(Derivable(), TestContext.Current.CancellationToken);

        // Assert
        spendLedger.Received(1).RecordSpend(new ChatTokenUsage(11, 7));
    }

    /// <summary>A conversation the store handed over with no messages has nothing to cite, so it costs no call.</summary>
    [Fact]
    public async Task DeriveAsync_AConversationWithNoMessages_SettlesWithoutReachingTheProvider()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Statements));
        var agent = provider.DeriverOver();
        var withoutMessages = Derivable() with { Messages = [] };

        // Act
        var derivation = await agent.DeriveAsync(withoutMessages, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(derivation.IsSettled);
        Assert.Empty(derivation.Entries);
        Assert.Equal(0, provider.RequestCount);
    }

    /// <summary>A message is somebody's mail, so what a deployment withholds from a provider is withheld here too.</summary>
    [Fact]
    public async Task DeriveAsync_AConversationCarryingASecret_SendsTheProviderTheGuardedText()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Statements));
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        using var actingFor = egress.ActingForUser();
        var agent = provider.DeriverOver(egressGuard: egress.Guard);
        var carryingASecret = Derivable() with
        {
            Subject = $"the key {Marker}",
            Messages =
            [
                new DerivableThreadMessage(
                    StoredEmailId.Create(Guid.CreateVersion7()),
                    0,
                    Marker,
                    new DateTimeOffset(2026, 9, 7, 9, 0, 0, TimeSpan.Zero),
                    $"a colleague pasted {Marker} into this thread"),
            ],
        };

        // Act
        await agent.DeriveAsync(carryingASecret, TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(Marker, provider.RequestBodies[0], StringComparison.Ordinal);
    }

    /// <summary>One conversation is one call, because the agent reaches no tool and is shown nothing else.</summary>
    [Fact]
    public async Task DeriveAsync_AnyConversation_ReachesTheProviderExactlyOnce()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Statements));
        var agent = provider.DeriverOver();

        // Act
        await agent.DeriveAsync(Derivable(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, provider.RequestCount);
    }

    private static DerivableThread Derivable() =>
        new(
            EmailThreadId.Create(Guid.CreateVersion7()),
            "The racking quotation",
            new ThreadStateRevision(1, new DateTimeOffset(2026, 9, 7, 9, 0, 0, TimeSpan.Zero)),
            [
                new DerivableThreadMessage(
                    StoredEmailId.Create(Guid.CreateVersion7()),
                    0,
                    "Karolina",
                    new DateTimeOffset(2026, 9, 7, 9, 0, 0, TimeSpan.Zero),
                    "we can hold the two-hour response time"),
            ],
            ExceedsBound: false);

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

        public ThreadStateAgent DeriverOver(
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

            return new ThreadStateAgent(
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
                NullLogger<ThreadStateAgent>.Instance);
        }

        public void Dispose() => this.handler.Dispose();

        private static IMailAnsweringSpendLedger AdmittingSpendLedger()
        {
            var spendLedger = Substitute.For<IMailAnsweringSpendLedger>();
            spendLedger.TryAdmitRun().Returns(true);

            return spendLedger;
        }
    }
}
