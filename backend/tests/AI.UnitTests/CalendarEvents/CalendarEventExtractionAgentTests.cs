// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Text;
using MailFathom.AI.CalendarEvents;
using MailFathom.AI.Chat;
using MailFathom.AI.Orchestration;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Calendar.Extraction;
using MailFathom.Application.Chat;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Enrichment;
using MailFathom.Application.Resilience;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MailFathom.AI.UnitTests.CalendarEvents;

/// <summary>Covers what each half of one extraction sends, what it makes of the answer, and what it does with none.</summary>
/// <remarks>
/// The reading goes over a real provider client and a scripted transport, so what is exercised is the client
/// construction, the credential resolution, the admission against the period, and the turn that actually left this
/// deployment.
/// </remarks>
public sealed class CalendarEventExtractionAgentTests
{
    /// <summary>The literal the scanner reports, standing in for a credential a colleague pasted into a message.</summary>
    private const string Marker = "AKIAEXAMPLEKEY";

    private const string OneEvent = """
        {\"events\": [{\"title\": \"Racking survey\", \"start\": \"2026-09-24T10:00\", \"end\": \"2026-09-24T11:00\"}]}
        """;

    private static readonly DateTimeOffset ReceivedAt = new(2026, 9, 21, 9, 30, 0, TimeSpan.FromHours(2));

    /// <summary>This one is registered only where the switch is on, so the pass in front of it reads rather than stopping.</summary>
    [Fact]
    public void IsActive_ADeploymentThatTurnedTheExtractionOn_SaysSo()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(OneEvent));

        // Act & Assert
        Assert.True(provider.ExtractorOver().IsActive);
    }

    [Fact]
    public async Task ProposeFromEmailAsync_AProviderThatNamedAnEvent_SettlesItInTheMessagesOwnOffset()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(OneEvent));
        var agent = provider.ExtractorOver();

        // Act
        var extraction = await agent.ProposeFromEmailAsync(Enrichable(), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(extraction.IsSettled);
        var proposed = Assert.Single(extraction.Events);
        Assert.Equal("Racking survey", proposed.Title.Value);
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 10, 0, 0, TimeSpan.FromHours(2)), proposed.Start);
    }

    /// <summary>
    /// A provider that answered and was paid for buys the same answer if asked again, so the message is settled as
    /// naming nothing rather than offered to the endpoint forever.
    /// </summary>
    [Fact]
    public async Task ProposeFromEmailAsync_AProviderThatAnsweredWithProse_SettlesTheMessageWithNoEvents()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion("I am afraid I cannot help with that."));
        var agent = provider.ExtractorOver();

        // Act
        var extraction = await agent.ProposeFromEmailAsync(Enrichable(), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(extraction.IsSettled);
        Assert.Empty(extraction.Events);
    }

    [Fact]
    public async Task ProposeFromEmailAsync_AProviderThatRefusedTheCall_WithholdsTheExtraction()
    {
        // Arrange
        using var provider = ScriptedTransport.Refusing(HttpStatusCode.BadRequest);
        var agent = provider.ExtractorOver();

        // Act
        var extraction = await agent.ProposeFromEmailAsync(Enrichable(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CalendarEventExtractionWithholding.ProviderUnavailable, extraction.Withheld);
    }

    /// <summary>
    /// Proposing from mail is admitted against the same period ceilings a question is, which is what keeps it from
    /// being a second, unmetered way of spending a deployment's allowance.
    /// </summary>
    [Fact]
    public async Task ProposeFromEmailAsync_APeriodThatHasSpentItsAllowance_WithholdsBeforeReachingTheProvider()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(OneEvent));
        var spendLedger = Substitute.For<IMailAnsweringSpendLedger>();
        spendLedger.TryAdmitRunAsync(Arg.Any<CancellationToken>()).Returns(false);
        var agent = provider.ExtractorOver(spendLedger: spendLedger);

        // Act
        var extraction = await agent.ProposeFromEmailAsync(Enrichable(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CalendarEventExtractionWithholding.AllowanceExhausted, extraction.Withheld);
        Assert.Equal(0, provider.RequestCount);
    }

    [Fact]
    public async Task ProposeFromEmailAsync_AProviderThatReportedItsUsage_ChargesTheCallToThePeriod()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(OneEvent, inputTokens: 11, outputTokens: 7));
        var spendLedger = Substitute.For<IMailAnsweringSpendLedger>();
        spendLedger.TryAdmitRunAsync(Arg.Any<CancellationToken>()).Returns(true);
        var agent = provider.ExtractorOver(spendLedger: spendLedger);

        // Act
        await agent.ProposeFromEmailAsync(Enrichable(), TestContext.Current.CancellationToken);

        // Assert
        await spendLedger.Received(1).RecordSpendAsync(new ChatTokenUsage(11, 7), Arg.Any<CancellationToken>());
    }

    /// <summary>A message with nothing cut from it carries no text to read, so it costs no call at all.</summary>
    [Fact]
    public async Task ProposeFromEmailAsync_AMessageWithNoPassages_SettlesWithoutReachingTheProvider()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(OneEvent));
        var agent = provider.ExtractorOver();
        var withoutPassages = new EnrichableEmail(
            StoredEmailId.Create(Guid.CreateVersion7()),
            "The racking survey",
            ReceivedAt,
            []);

        // Act
        var extraction = await agent.ProposeFromEmailAsync(withoutPassages, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(extraction.IsSettled);
        Assert.Equal(0, provider.RequestCount);
    }

    /// <summary>Almost every date in mail is relative, and an undated message would resolve them against nothing.</summary>
    [Fact]
    public async Task ProposeFromEmailAsync_AMessageWithNoArrivalInstant_SettlesWithoutReachingTheProvider()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(OneEvent));
        var agent = provider.ExtractorOver();
        var undated = new EnrichableEmail(
            StoredEmailId.Create(Guid.CreateVersion7()),
            "The racking survey",
            ReceivedAt: null,
            [new EnrichablePassage(EmailChunkId.Create(Guid.CreateVersion7()), 0, "shall we say Thursday at ten")]);

        // Act
        var extraction = await agent.ProposeFromEmailAsync(undated, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(extraction.IsSettled);
        Assert.Empty(extraction.Events);
        Assert.Equal(0, provider.RequestCount);
    }

    /// <summary>A passage is somebody's mail, so what a deployment withholds from a provider is withheld here too.</summary>
    [Fact]
    public async Task ProposeFromEmailAsync_AMessageCarryingASecret_SendsTheProviderTheGuardedText()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(OneEvent));
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        using var actingFor = egress.ActingForUser();
        var agent = provider.ExtractorOver(egressGuard: egress.Guard);
        var carryingASecret = new EnrichableEmail(
            StoredEmailId.Create(Guid.CreateVersion7()),
            $"the key {Marker}",
            ReceivedAt,
            [
                new EnrichablePassage(
                    EmailChunkId.Create(Guid.CreateVersion7()),
                    0,
                    $"a colleague pasted {Marker} into this thread"),
            ]);

        // Act
        await agent.ProposeFromEmailAsync(carryingASecret, TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(Marker, provider.RequestBodies[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// A long message is read from as much of it as the endpoint takes, rather than raising out of the pass that was
    /// deriving it: the same message would be composed to the same length on every run, so a refusal would stop that
    /// account's pass for ever.
    /// </summary>
    [Fact]
    public async Task ProposeFromEmailAsync_AMessageLongerThanTheEndpointAccepts_IsStillReadFromItsOpening()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(OneEvent));
        var agent = provider.ExtractorOver(plan: ChatDeclarations.Plan(maximumRequestCharacters: 800));
        var longMessage = new EnrichableEmail(
            StoredEmailId.Create(Guid.CreateVersion7()),
            "The racking survey",
            ReceivedAt,
            [
                .. Enumerable.Range(0, 6).Select(ordinal => new EnrichablePassage(
                    EmailChunkId.Create(Guid.CreateVersion7()),
                    ordinal,
                    new string('a', 400))),
            ]);

        // Act
        var extraction = await agent.ProposeFromEmailAsync(longMessage, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(extraction.IsSettled);
        Assert.Single(extraction.Events);
        Assert.Equal(1, provider.RequestCount);
    }

    /// <summary>One message is one call, because the agent reaches no tool and is shown nothing else.</summary>
    [Fact]
    public async Task ProposeFromEmailAsync_AnyMessage_ReachesTheProviderExactlyOnce()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(OneEvent));
        var agent = provider.ExtractorOver();

        // Act
        await agent.ProposeFromEmailAsync(Enrichable(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, provider.RequestCount);
    }

    /// <summary>One message may name several dates, up to what the reading keeps.</summary>
    [Fact]
    public async Task ProposeFromEmailAsync_AProviderThatNamedSeveralEvents_SettlesUpToTheBound()
    {
        // Arrange
        var written = string.Join(
            ",",
            Enumerable.Range(0, CalendarEventExtraction.MaximumEvents + 2).Select(ordinal =>
                $"{{\\\"title\\\": \\\"Visit {ordinal}\\\", \\\"start\\\": \\\"2026-09-24T10:00\\\"}}"));
        using var provider = ScriptedTransport.Answering(Completion($"{{\\\"events\\\": [{written}]}}"));
        var agent = provider.ExtractorOver();

        // Act
        var extraction = await agent.ProposeFromEmailAsync(Enrichable(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CalendarEventExtraction.MaximumEvents, extraction.Events.Count);
    }

    [Fact]
    public async Task DraftFromDescriptionAsync_AProviderThatNamedAnEvent_SettlesItInThePersonsOwnOffset()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(OneEvent));
        var agent = provider.ExtractorOver();

        // Act
        var extraction = await agent.DraftFromDescriptionAsync(Description(), TestContext.Current.CancellationToken);

        // Assert
        var drafted = Assert.Single(extraction.Events);
        Assert.Equal("Racking survey", drafted.Title.Value);
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 10, 0, 0, TimeSpan.FromHours(2)), drafted.Start);
    }

    /// <summary>Somebody describing a meeting is describing one, so a sentence read into several keeps the first.</summary>
    [Fact]
    public async Task DraftFromDescriptionAsync_AProviderThatNamedSeveralEvents_SettlesExactlyOne()
    {
        // Arrange
        const string several = """
            {\"events\": [{\"title\": \"Racking survey\", \"start\": \"2026-09-24T10:00\"},
            {\"title\": \"Site visit\", \"start\": \"2026-09-25T10:00\"}]}
            """;
        using var provider = ScriptedTransport.Answering(Completion(several));
        var agent = provider.ExtractorOver();

        // Act
        var extraction = await agent.DraftFromDescriptionAsync(Description(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Racking survey", Assert.Single(extraction.Events).Title.Value);
    }

    /// <summary>A sentence somebody typed is a prompt they wrote, and is guarded exactly as their mail is.</summary>
    [Fact]
    public async Task DraftFromDescriptionAsync_ADescriptionCarryingASecret_SendsTheProviderTheGuardedText()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(OneEvent));
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        using var actingFor = egress.ActingForUser();
        var agent = provider.ExtractorOver(egressGuard: egress.Guard);

        // Act
        await agent.DraftFromDescriptionAsync(
            Description($"call about {Marker} tomorrow at one"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(Marker, provider.RequestBodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task DraftFromDescriptionAsync_APeriodThatHasSpentItsAllowance_WithholdsBeforeReachingTheProvider()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(OneEvent));
        var spendLedger = Substitute.For<IMailAnsweringSpendLedger>();
        spendLedger.TryAdmitRunAsync(Arg.Any<CancellationToken>()).Returns(false);
        var agent = provider.ExtractorOver(spendLedger: spendLedger);

        // Act
        var extraction = await agent.DraftFromDescriptionAsync(Description(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CalendarEventExtractionWithholding.AllowanceExhausted, extraction.Withheld);
        Assert.Equal(0, provider.RequestCount);
    }

    [Fact]
    public async Task DraftFromDescriptionAsync_AProviderThatRefusedTheCall_WithholdsTheExtraction()
    {
        // Arrange
        using var provider = ScriptedTransport.Refusing(HttpStatusCode.BadRequest);
        var agent = provider.ExtractorOver();

        // Act
        var extraction = await agent.DraftFromDescriptionAsync(Description(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CalendarEventExtractionWithholding.ProviderUnavailable, extraction.Withheld);
    }

    private static EnrichableEmail Enrichable() =>
        new(
            StoredEmailId.Create(Guid.CreateVersion7()),
            "The racking survey",
            ReceivedAt,
            [new EnrichablePassage(EmailChunkId.Create(Guid.CreateVersion7()), 0, "shall we say Thursday at ten")]);

    private static CalendarEventDescription Description(
        string text = "racking survey on Thursday at ten")
    {
        Assert.True(CalendarEventDescription.TryCreate(text, ReceivedAt, out var description));

        return description;
    }

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

    /// <summary>A provider that answers from a script, so the reading is exercised over a real client and no network.</summary>
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

        public CalendarEventExtractionAgent ExtractorOver(
            SensitiveContentEgressGuard? egressGuard = null,
            IMailAnsweringSpendLedger? spendLedger = null,
            ChatGenerationPlan? plan = null)
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

            return new CalendarEventExtractionAgent(
                plan ?? ChatDeclarations.Plan(),
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
                NullLogger<CalendarEventExtractionAgent>.Instance);
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
