// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Text;
using MailFathom.AI.Orchestration;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.AI.Search;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Chat;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.Search.Phrasing;
using MailFathom.Application.Resilience;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MailFathom.AI.UnitTests.Search;

/// <summary>Covers what one reading sends, what it makes of the answer, and what a search is left with when there is none.</summary>
/// <remarks>
/// The reading runs over a real provider client and a scripted transport, so what is exercised is the client
/// construction, the credential resolution, and the turn that actually left this deployment.
/// </remarks>
public sealed class MailSearchPhraseAgentTests
{
    /// <summary>The literal the scanner in the guarded-egress test reports, standing in for a credential in a sentence.</summary>
    private const string Marker = "AKIAEXAMPLEKEY";

    private static readonly DateOnly Today = new(2026, 9, 9);

    [Fact]
    public async Task ReadAsync_AProviderThatReadTheSentence_AnswersWithTheFiltersAndTheCriteria()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(
            """{\"filters\": {\"unread\": true, \"receivedFrom\": \"2026-09-01\"}, \"criteria\": [\"racking quotation\"]}"""));
        var reader = provider.ReaderOver();

        // Act
        var reading = await reader.ReadAsync(Phrase(), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(reading.WasRead);
        Assert.True(reading.Filters.Unread);
        Assert.Equal(new DateOnly(2026, 9, 1), reading.Filters.ReceivedFrom);
        Assert.Equal(["racking quotation"], reading.Criteria);
    }

    /// <summary>A derivation that could not be made leaves the typed words to be searched for, exactly as a deployment with no model searches them.</summary>
    [Fact]
    public async Task ReadAsync_AProviderThatAnsweredWithProse_LeavesThePlainWordSearch()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion("I am afraid I cannot help with that."));
        var reader = provider.ReaderOver();

        // Act
        var reading = await reader.ReadAsync(Phrase(), TestContext.Current.CancellationToken);

        // Assert
        Assert.False(reading.WasRead);
        Assert.Same(MailSearchPhraseReading.Nothing, reading);
    }

    [Fact]
    public async Task ReadAsync_AProviderThatRefusedTheCall_LeavesThePlainWordSearch()
    {
        // Arrange
        using var provider = ScriptedTransport.Refusing(HttpStatusCode.BadRequest);
        var reader = provider.ReaderOver();

        // Act
        var reading = await reader.ReadAsync(Phrase(), TestContext.Current.CancellationToken);

        // Assert
        Assert.False(reading.WasRead);
    }

    /// <summary>A key that would not resolve never reaches the endpoint, and is the same plain search a refusal is.</summary>
    [Fact]
    public async Task ReadAsync_ACredentialThatCannotBeResolved_LeavesThePlainWordSearchAndSendsNothing()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion("""{\"criteria\": [\"invoice\"]}"""));
        var reader = provider.ReaderOver(credentialFailure: new InvalidOperationException(
            "The provider key of AI endpoint 'a-chat-endpoint' could not be resolved."));

        // Act
        var reading = await reader.ReadAsync(Phrase(), TestContext.Current.CancellationToken);

        // Assert
        Assert.False(reading.WasRead);
        Assert.Equal(0, provider.RequestCount);
    }

    /// <summary>A sentence is a prompt somebody wrote, so what a deployment withholds from a provider is withheld here too.</summary>
    [Fact]
    public async Task ReadAsync_ASentenceCarryingASecret_SendsTheProviderTheGuardedText()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion("""{\"criteria\": [\"key\"]}"""));
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        using var actingFor = egress.ActingForUser();
        var reader = provider.ReaderOver(egressGuard: egress.Guard);
        var phrase = new MailSearchPhrase(
            EmailSearchQueryText.Create($"the message with the key {Marker} in it"),
            Today);

        // Act
        await reader.ReadAsync(phrase, TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(Marker, provider.RequestBodies[0], StringComparison.Ordinal);
    }

    /// <summary>A relative expression is resolved against the reader's own day, so the day is what the turn states.</summary>
    [Fact]
    public async Task ReadAsync_AnySentence_TellsTheModelWhichDayItWasAskedOn()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion("""{\"criteria\": [\"invoice\"]}"""));
        var reader = provider.ReaderOver();

        // Act
        await reader.ReadAsync(Phrase(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("2026-09-09", provider.RequestBodies[0], StringComparison.Ordinal);
    }

    /// <summary>No mail is shown to a reading, so one call is the whole of what a sentence costs here.</summary>
    [Fact]
    public async Task ReadAsync_AnySentence_ReachesTheProviderExactlyOnce()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion("""{\"criteria\": [\"invoice\"]}"""));
        var reader = provider.ReaderOver();

        // Act
        await reader.ReadAsync(Phrase(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, provider.RequestCount);
    }

    /// <summary>What a reading cost is charged to the same allowance a question is, because a provider charges for both alike.</summary>
    [Fact]
    public async Task ReadAsync_AProviderThatReportedItsUsage_ChargesTheCallToTheRunAndToThePeriod()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(
            Completion("""{\"criteria\": [\"invoice\"]}""", inputTokens: 9, outputTokens: 5));
        var runLedger = new MailAnsweringRunLedger(MailAnsweringRunBounds.Default);
        var spendLedger = Substitute.For<IMailAnsweringSpendLedger>();
        var reader = provider.ReaderOver(runLedger: runLedger, spendLedger: spendLedger);

        // Act
        await reader.ReadAsync(Phrase(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new MailAnsweringRunSpend(1, 14, 0, 0), runLedger.Read());
        await spendLedger.Received(1).RecordSpendAsync(new ChatTokenUsage(9, 5), Arg.Any<CancellationToken>());
    }

    /// <summary>The one failure that is not a fallback: a ceiling has to reach the person, or a deployment silently spends past it.</summary>
    [Fact]
    public async Task ReadAsync_ARunThatHasSpentItsCallAllowance_RefusesBeforeReachingTheProvider()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion("""{\"criteria\": [\"invoice\"]}"""));
        var runLedger = new MailAnsweringRunLedger(
            MailAnsweringRunBounds.Create(20_000, maximumProviderCalls: 1, 80_000));
        runLedger.RequireAllowanceForNextCall();
        var reader = provider.ReaderOver(runLedger: runLedger);

        // Act
        var refusal = await Assert.ThrowsAsync<MailAnsweringBudgetExhaustedException>(() =>
            reader.ReadAsync(Phrase(), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailAnsweringBudgetScope.Run, refusal.Scope);
        Assert.Equal(0, provider.RequestCount);
    }

    /// <summary>What somebody typed is theirs, so a reading records how much it read and never a word of the sentence.</summary>
    [Theory]
    [InlineData("""{\"filters\": {\"unread\": true}, \"criteria\": [\"racking quotation\"]}""")]
    [InlineData("I am afraid I cannot help with that.")]
    public async Task ReadAsync_AnySentence_WritesNothingOfItToTheLog(string answer)
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        using var provider = ScriptedTransport.Answering(Completion(answer));
        var reader = provider.ReaderOver(logs: logs);
        var phrase = new MailSearchPhrase(
            EmailSearchQueryText.Create("unread mail about the racking quotation"),
            Today);

        // Act
        await reader.ReadAsync(phrase, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(logs.Records);
        Assert.All(
            logs.Records,
            record => Assert.DoesNotContain("racking", record.Message, StringComparison.OrdinalIgnoreCase));
    }

    private static MailSearchPhrase Phrase() =>
        new(EmailSearchQueryText.Create("unread mail about the racking quotation since last week"), Today);

    /// <summary>Builds the chat-completion payload a provider answers with.</summary>
    private static string Completion(string content, int? inputTokens = null, int? outputTokens = null) =>
        "{\"id\":\"chatcmpl-1\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"a-chat-model\","
        + "\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\""
        + content
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
        private ILoggerFactory loggerFactory = NullLoggerFactory.Instance;
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

        public MailSearchPhraseAgent ReaderOver(
            SensitiveContentEgressGuard? egressGuard = null,
            Exception? credentialFailure = null,
            MailAnsweringRunLedger? runLedger = null,
            IMailAnsweringSpendLedger? spendLedger = null,
            RecordingLoggerProvider? logs = null)
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

            // Owned by this transport rather than by the caller: the agent keeps the factory and creates a logger
            // through it on the call, so a factory disposed with the arrangement would fail the run it was built for.
            if (logs is not null)
            {
                this.loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));
            }

            return new MailSearchPhraseAgent(
                ChatDeclarations.Plan(),
                runLedger ?? new MailAnsweringRunLedger(MailAnsweringRunBounds.Default),
                spendLedger ?? Substitute.For<IMailAnsweringSpendLedger>(),
                credentialSource,
                new OpenAiCompatibleClientFactory(),
                transportFactory,
                operationRunner,
                Substitute.For<IAiProviderHealthRecorder>(),
                egressGuard ?? SensitiveContentEgressGuards.Inactive(),
                new EmptyAgentInstructionEnvelope(),
                this.loggerFactory,
                this.loggerFactory.CreateLogger<MailSearchPhraseAgent>());
        }

        public void Dispose()
        {
            this.handler.Dispose();
            this.loggerFactory.Dispose();
        }
    }
}
