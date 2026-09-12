// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Text;
using MailFathom.AI.BodyCleanup;
using MailFathom.AI.Chat;
using MailFathom.AI.Orchestration;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.Chat;
using MailFathom.Application.EmailContent.Cleaning;
using MailFathom.Application.Resilience;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MailFathom.AI.UnitTests.BodyCleanup;

/// <summary>Covers what one proposal sends, what it makes of the answer, and what it does when there is none.</summary>
/// <remarks>
/// The proposal goes over a real provider client and a scripted transport, so what is exercised is the client
/// construction, the credential resolution, the admission against the period, and the turn that actually left this
/// deployment.
/// </remarks>
public sealed class MailBodyCleanupAgentTests
{
    /// <summary>The literal the scanner reports, standing in for a credential a colleague pasted into a message.</summary>
    private const string Marker = "AKIAEXAMPLEKEY";

    private const string Partition =
        """{\"segments\":[{\"from\":0,\"to\":0,\"action\":\"drop\"},{\"from\":1,\"to\":1,\"action\":\"keep\"}]}""";

    /// <summary>This one is registered only where the switch is on, so the pass in front of it composes an outline rather than stopping.</summary>
    [Fact]
    public void IsActive_ADeploymentThatTurnedTheCleaningOn_SaysSo()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Partition));

        // Act and assert
        Assert.True(provider.CleanerOver().IsActive);
    }

    [Fact]
    public async Task ProposeAsync_AProviderThatAnsweredWithAPartition_ProposesThoseRanges()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Partition));

        // Act
        var proposal = await provider.CleanerOver().ProposeAsync(Outline(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailBodyCleaningWithholding.None, proposal.Withholding);
        Assert.Equal(
            [new MailBodyCleaningSegment(0, 0, Keep: false), new MailBodyCleaningSegment(1, 1, Keep: true)],
            proposal.Segments);
    }

    /// <summary>
    /// An answer of prose is a proposal of no ranges rather than a withholding, because the pass above tells the two apart:
    /// one is asked again and the other is not.
    /// </summary>
    [Fact]
    public async Task ProposeAsync_AProviderThatAnsweredWithProse_ProposesNothingWithoutWithholding()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion("I am afraid I cannot help with that."));

        // Act
        var proposal = await provider.CleanerOver().ProposeAsync(Outline(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailBodyCleaningWithholding.None, proposal.Withholding);
        Assert.Empty(proposal.Segments);
    }

    [Fact]
    public async Task ProposeAsync_AProviderThatRefusedTheCall_WithholdsTheCleaning()
    {
        // Arrange
        using var provider = ScriptedTransport.Refusing(HttpStatusCode.BadRequest);

        // Act
        var proposal = await provider.CleanerOver().ProposeAsync(Outline(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailBodyCleaningWithholding.ProviderUnavailable, proposal.Withholding);
    }

    [Fact]
    public async Task ProposeAsync_ACredentialThatCannotBeResolved_WithholdsWithoutReachingTheEndpoint()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Partition));
        var cleaner = provider.CleanerOver(credentialFailure: new InvalidOperationException(
            "The provider key of AI endpoint 'a-chat-endpoint' could not be resolved."));

        // Act
        var proposal = await cleaner.ProposeAsync(Outline(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailBodyCleaningWithholding.ProviderUnavailable, proposal.Withholding);
        Assert.Equal(0, provider.RequestCount);
    }

    /// <summary>
    /// A cleaning is admitted against the same period ceilings a question is, which is what stops the third rendering from
    /// being a second, unmetered way of spending a deployment's allowance.
    /// </summary>
    [Fact]
    public async Task ProposeAsync_APeriodThatHasSpentItsAllowance_WithholdsBeforeReachingTheProvider()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Partition));
        var spendLedger = Substitute.For<IMailAnsweringSpendLedger>();
        spendLedger.TryAdmitRunAsync(Arg.Any<CancellationToken>()).Returns(false);

        // Act
        var proposal = await provider.CleanerOver(spendLedger: spendLedger)
            .ProposeAsync(Outline(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailBodyCleaningWithholding.AllowanceExhausted, proposal.Withholding);
        Assert.Equal(0, provider.RequestCount);
    }

    [Fact]
    public async Task ProposeAsync_AProviderThatReportedItsUsage_ChargesTheCallToThePeriod()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Partition, inputTokens: 11, outputTokens: 7));
        var spendLedger = Substitute.For<IMailAnsweringSpendLedger>();
        spendLedger.TryAdmitRunAsync(Arg.Any<CancellationToken>()).Returns(true);

        // Act
        await provider.CleanerOver(spendLedger: spendLedger)
            .ProposeAsync(Outline(), TestContext.Current.CancellationToken);

        // Assert
        await spendLedger.Received(1).RecordSpendAsync(new ChatTokenUsage(11, 7), Arg.Any<CancellationToken>());
    }

    /// <summary>A block's opening is somebody's mail, so what a deployment withholds from a provider is withheld here too.</summary>
    [Fact]
    public async Task ProposeAsync_AnOutlineCarryingASecret_SendsTheProviderTheGuardedText()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Partition));
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        using var actingFor = egress.ActingForUser();
        var carryingASecret = new CleanableMailBody(
            $"the key {Marker}",
            Marker,
            [new CleanableMailBlock(0, "paragraph", LinkCount: 0, $"a colleague pasted {Marker} into this message")]);

        // Act
        await provider.CleanerOver(egressGuard: egress.Guard)
            .ProposeAsync(carryingASecret, TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(Marker, provider.RequestBodies[0], StringComparison.Ordinal);
    }

    /// <summary>One open is one call, because the agent reaches no tool and is shown nothing but the outline.</summary>
    [Fact]
    public async Task ProposeAsync_AnyOutline_ReachesTheProviderExactlyOnce()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Partition));

        // Act
        await provider.CleanerOver().ProposeAsync(Outline(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, provider.RequestCount);
    }

    /// <summary>
    /// A chain that answered nowhere failed at its last model, so the withholding line names that one. The alias the
    /// pass was configured with has a fallback behind it, and naming it would send whoever reads the line to an
    /// endpoint that may be working.
    /// </summary>
    [Fact]
    public async Task ProposeAsync_AChainWhoseFallbackAlsoFailed_WithholdsAgainstTheFallbacksAlias()
    {
        // Arrange
        using var provider = ScriptedTransport.Refusing(HttpStatusCode.TooManyRequests);
        using var logs = new RecordingLoggerFactory();
        var chain = ChatDeclarations
            .Plan()
            .WithFallback(ChatDeclarations.Plan(ChatDeclarations.Endpoint("standby")));

        var cleaner = provider.CleanerOver(
            plan: chain,
            logger: logs.CreateLogger<MailBodyCleanupAgent>());

        // Act
        var proposal = await cleaner.ProposeAsync(Outline(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailBodyCleaningWithholding.ProviderUnavailable, proposal.Withholding);

        var withheld = Assert.Single(
            logs.Records,
            record => record.Properties.ContainsKey("Withholding"));

        Assert.Equal("standby", withheld.Properties["EndpointAlias"]);
    }

    /// <summary>
    /// The line an operator reads names the model that actually produced the proposal. The alias the pass was
    /// configured with is the model it asked first, so a fallback answering would otherwise send whoever reads the log
    /// to an endpoint that is working.
    /// </summary>
    [Fact]
    public async Task ProposeAsync_AProposalTheFallbackProduced_LogsTheFallbacksAlias()
    {
        // Arrange
        using var provider = ScriptedTransport.RefusingThenAnswering(
            HttpStatusCode.TooManyRequests,
            Completion(Partition));

        using var logs = new RecordingLoggerFactory();
        var chain = ChatDeclarations
            .Plan()
            .WithFallback(ChatDeclarations.Plan(ChatDeclarations.Endpoint("standby")));

        var cleaner = provider.CleanerOver(
            plan: chain,
            logger: logs.CreateLogger<MailBodyCleanupAgent>());

        // Act
        var proposal = await cleaner.ProposeAsync(Outline(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailBodyCleaningWithholding.None, proposal.Withholding);

        var proposed = Assert.Single(
            logs.Records,
            record => record.Properties.ContainsKey("SegmentCount"));

        Assert.Equal("standby", proposed.Properties["EndpointAlias"]);
    }

    /// <summary>
    /// One proposal is one run however many models it was asked of, so the fallback attempt spends the run's remaining
    /// allowance rather than opening a second one. A ceiling of one call is what makes that visible: the main model
    /// spends it, and the fallback is then refused here rather than reaching the provider and being billed for.
    /// </summary>
    [Fact]
    public async Task ProposeAsync_AFallbackBehindARateLimitedModel_SpendsOneRunsAllowanceAcrossBoth()
    {
        // Arrange
        using var provider = ScriptedTransport.Refusing(HttpStatusCode.TooManyRequests);
        var chain = ChatDeclarations
            .Plan()
            .WithFallback(ChatDeclarations.Plan(ChatDeclarations.Endpoint("standby")));

        var cleaner = provider.CleanerOver(
            plan: chain,
            runBounds: MailAnsweringRunBounds.Create(maximumRetrievedCharacters: 20_000, maximumProviderCalls: 1, maximumTokens: 80_000));

        // Act
        var proposal = await cleaner.ProposeAsync(Outline(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, provider.RequestCount);
        Assert.Equal(MailBodyCleaningWithholding.ProviderUnavailable, proposal.Withholding);
    }

    private static CleanableMailBody Outline() => new(
        "Your receipt",
        "The Shop",
        [
            new CleanableMailBlock(0, "paragraph", LinkCount: 0, "View this in your browser"),
            new CleanableMailBlock(1, "paragraph", LinkCount: 0, "Your code is 558132"),
        ]);

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

    /// <summary>A provider that answers from a script, so the proposal is exercised over a real client and no network.</summary>
    private sealed class ScriptedTransport : IDisposable
    {
        private readonly FakeHttpMessageHandler handler;
        private string payload = string.Empty;
        private string? nextPayload;
        private HttpStatusCode status = HttpStatusCode.OK;

        private ScriptedTransport() =>
            this.handler = new FakeHttpMessageHandler(async (request, cancellationToken) =>
            {
                this.RequestCount++;
                this.RequestBodies.Add(request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken));

                var answered = new HttpResponseMessage(this.status)
                {
                    Content = new StringContent(this.payload, Encoding.UTF8, "application/json"),
                };

                if (this.nextPayload is { } following)
                {
                    this.status = HttpStatusCode.OK;
                    this.payload = following;
                    this.nextPayload = null;
                }

                return answered;
            });

        public int RequestCount { get; private set; }

        /// <summary>What the provider was actually sent, which is where a test reads what left this deployment.</summary>
        public List<string> RequestBodies { get; } = [];

        public static ScriptedTransport Answering(string payload) => new() { payload = payload };

        public static ScriptedTransport Refusing(HttpStatusCode status) =>
            new() { status = status, payload = "{\"error\":{\"message\":\"no\"}}" };

        /// <summary>Refuses the first call and answers the second, which is one chain falling through to its fallback.</summary>
        public static ScriptedTransport RefusingThenAnswering(HttpStatusCode status, string payload) =>
            new() { status = status, payload = "{\"error\":{\"message\":\"no\"}}", nextPayload = payload };

        public MailBodyCleanupAgent CleanerOver(
            SensitiveContentEgressGuard? egressGuard = null,
            Exception? credentialFailure = null,
            IMailAnsweringSpendLedger? spendLedger = null,
            ChatGenerationPlan? plan = null,
            MailAnsweringRunBounds? runBounds = null,
            ILogger<MailBodyCleanupAgent>? logger = null)
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

            return new MailBodyCleanupAgent(
                new MailBodyCleanupPlan(plan ?? ChatDeclarations.Plan()),
                runBounds ?? MailAnsweringRunBounds.Default,
                spendLedger ?? AdmittingSpendLedger(),
                credentialSource,
                new OpenAiCompatibleClientFactory(),
                transportFactory,
                operationRunner,
                egressGuard ?? SensitiveContentEgressGuards.Inactive(),
                new EmptyAgentInstructionEnvelope(),
                NullLoggerFactory.Instance,
                logger ?? NullLogger<MailBodyCleanupAgent>.Instance);
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
