// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Text;
using MailFathom.AI.Orchestration;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.AI.ReplyDrafts;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Emails.ReplyDrafts;
using MailFathom.Application.Resilience;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MailFathom.AI.UnitTests.ReplyDrafts;

/// <summary>Covers what one drafting sends, what it makes of the answer, and what it does when there is none.</summary>
/// <remarks>
/// The drafting goes over a real provider client and a scripted transport, so what is exercised is the client
/// construction, the credential resolution, the turn that actually left this deployment, and above all what that turn
/// does not carry: an address is resolved here rather than sent, and a test reading the request body is what holds that
/// to be true.
/// </remarks>
public sealed class ReplyDraftAgentTests
{
    /// <summary>The literal the scanner reports, standing in for a credential a colleague pasted into a conversation.</summary>
    private const string Marker = "AKIAEXAMPLEKEY";

    private const string Reply = """
        {\"body\": \"Tuesday at ten works.\", \"claims\": [{\"text\": \"They quoted 4 200.\", \"messages\": [0]}],
        \"recipients\": [0]}
        """;

    [Fact]
    public async Task WriteAsync_AProviderThatAnsweredWithAReply_DraftsIt()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Reply));
        var agent = provider.WriterOver();

        // Act
        var draft = await agent.WriteAsync(Brief(), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(draft.WasWritten);
        Assert.Equal("Tuesday at ten works.", draft.Body);
        Assert.Equal("They quoted 4 200.", Assert.Single(draft.Claims).Text);
        Assert.Equal("karolina@example.test", Assert.Single(draft.ProposedRecipients).Address);
    }

    /// <summary>The proposal is a position the turn published, so the address itself never has to leave this deployment.</summary>
    [Fact]
    public async Task WriteAsync_AnyDrafting_SendsTheProviderTheNamesAndNoAddress()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Reply));
        var agent = provider.WriterOver();

        // Act
        await agent.WriteAsync(Brief(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("Karolina", provider.RequestBodies[0], StringComparison.Ordinal);
        Assert.DoesNotContain("karolina@example.test", provider.RequestBodies[0], StringComparison.Ordinal);
        Assert.DoesNotContain("@example.test", provider.RequestBodies[0], StringComparison.Ordinal);
    }

    /// <summary>A manner is what the samples are for, so they travel with the drafting or the reply sounds like nobody.</summary>
    [Fact]
    public async Task WriteAsync_ADraftingCarryingStyleSamples_SendsThemWithTheConversation()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Reply));
        var agent = provider.WriterOver();

        // Act
        await agent.WriteAsync(Brief(styleSamples: ["Cheers, Anna"]), TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("Cheers, Anna", provider.RequestBodies[0], StringComparison.Ordinal);
    }

    /// <summary>What somebody typed is the point of the drafting, so both texts reach the turn.</summary>
    [Fact]
    public async Task WriteAsync_ADraftingCarryingASelectionAndAnInstruction_SendsBoth()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Reply));
        var agent = provider.WriterOver();
        var brief = Brief() with { Selection = "the delivery window", Instruction = "propose Tuesday" };

        // Act
        await agent.WriteAsync(brief, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("the delivery window", provider.RequestBodies[0], StringComparison.Ordinal);
        Assert.Contains("propose Tuesday", provider.RequestBodies[0], StringComparison.Ordinal);
    }

    /// <summary>A conversation is somebody's mail, so what a deployment withholds from a provider is withheld here too.</summary>
    [Fact]
    public async Task WriteAsync_AConversationCarryingASecret_SendsTheProviderTheGuardedText()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Reply));
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        using var actingFor = egress.ActingForUser();
        var agent = provider.WriterOver(egressGuard: egress.Guard);
        var carryingASecret = Brief(
            text: $"a colleague pasted {Marker} into this thread",
            styleSamples: [$"and again in {Marker}"]) with
        {
            Selection = $"the key {Marker}",
            Instruction = $"answer about {Marker}",
        };

        // Act
        await agent.WriteAsync(carryingASecret, TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(Marker, provider.RequestBodies[0], StringComparison.Ordinal);
    }

    /// <summary>What a person has when a provider fails is the composer they were already looking at.</summary>
    [Fact]
    public async Task WriteAsync_AProviderThatRefusedTheCall_DraftsNothing()
    {
        // Arrange
        using var provider = ScriptedTransport.Refusing(HttpStatusCode.InternalServerError);
        var agent = provider.WriterOver();

        // Act
        var draft = await agent.WriteAsync(Brief(), TestContext.Current.CancellationToken);

        // Assert
        Assert.False(draft.WasWritten);
    }

    /// <summary>An endpoint the configuration no longer declares is a failure with no health record, and it ends the same way.</summary>
    [Fact]
    public async Task WriteAsync_ACredentialThatCouldNotBeResolved_DraftsNothing()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Reply));
        var agent = provider.WriterOver(
            credentialFailure: new InvalidOperationException("the alias names no endpoint"));

        // Act
        var draft = await agent.WriteAsync(Brief(), TestContext.Current.CancellationToken);

        // Assert
        Assert.False(draft.WasWritten);
        Assert.Equal(0, provider.RequestCount);
    }

    /// <summary>A person pressing a button the allowance is spent on is told so rather than left with silence.</summary>
    [Fact]
    public async Task WriteAsync_APeriodThatHasSpentItsAllowance_RefusesRatherThanFallingBack()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Reply));
        var spendLedger = Substitute.For<IMailAnsweringSpendLedger>();
        spendLedger.TryAdmitRunAsync(Arg.Any<CancellationToken>()).Returns(false);
        var agent = provider.WriterOver(spendLedger: spendLedger);

        // Act and assert
        await Assert.ThrowsAsync<MailAnsweringBudgetExhaustedException>(
            () => agent.WriteAsync(Brief(), TestContext.Current.CancellationToken));
        Assert.Equal(0, provider.RequestCount);
    }

    /// <summary>One drafting is one call, because the agent reaches no tool and is shown nothing else.</summary>
    [Fact]
    public async Task WriteAsync_AnyDrafting_ReachesTheProviderExactlyOnce()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Reply));
        var agent = provider.WriterOver();

        // Act
        await agent.WriteAsync(Brief(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, provider.RequestCount);
    }

    private static ReplyDraftBrief Brief(
        string text = "It is 4 200 zloty.",
        IReadOnlyList<string>? styleSamples = null)
    {
        EmailAddress.TryCreate("Karolina", "karolina@example.test", out var karolina);

        return new ReplyDraftBrief(
            new ReplyDraftSources(
                "The racking quotation",
                [
                    new ReplyDraftMessage(
                        StoredEmailId.Create(Guid.CreateVersion7()),
                        0,
                        "Karolina",
                        new DateTimeOffset(2026, 9, 7, 9, 0, 0, TimeSpan.Zero),
                        text),
                ],
                [new ReplyDraftParticipant(0, karolina)],
                styleSamples ?? []),
            Selection: null,
            Instruction: null);
    }

    /// <summary>Builds the chat-completion payload a provider answers with.</summary>
    private static string Completion(string content) =>
        "{\"id\":\"chatcmpl-1\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"a-chat-model\","
        + "\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\""
        + content.ReplaceLineEndings(string.Empty)
        + "\"},\"finish_reason\":\"stop\"}]}";

    /// <summary>A provider that answers from a script, so the drafting is exercised over a real client and no network.</summary>
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

        public ReplyDraftAgent WriterOver(
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

            return new ReplyDraftAgent(
                ChatDeclarations.Plan(),
                new MailAnsweringRunLedger(MailAnsweringRunBounds.Default),
                spendLedger ?? AdmittingSpendLedger(),
                credentialSource,
                new OpenAiCompatibleClientFactory(),
                transportFactory,
                operationRunner,
                Substitute.For<IAiProviderHealthRecorder>(),
                egressGuard ?? SensitiveContentEgressGuards.Inactive(),
                new EmptyAgentInstructionEnvelope(),
                NullLoggerFactory.Instance,
                NullLogger<ReplyDraftAgent>.Instance);
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
