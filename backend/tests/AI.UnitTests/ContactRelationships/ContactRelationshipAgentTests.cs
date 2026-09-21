// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Text;
using MailFathom.AI.ContactRelationships;
using MailFathom.AI.Orchestration;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Contacts.Correspondence;
using MailFathom.Application.Contacts.Relationship;
using MailFathom.Application.Resilience;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MailFathom.AI.UnitTests.ContactRelationships;

/// <summary>Covers what one derivation sends, what it makes of the answer, and what it withholds when there is none.</summary>
/// <remarks>
/// The derivation goes over a real provider client and a scripted transport, so what is exercised is the client
/// construction, the credential resolution, and above all the turn that actually left this deployment — which is where a
/// test can hold the claim that no identifier of a stored message travels with a subject.
/// </remarks>
public sealed class ContactRelationshipAgentTests
{
    /// <summary>The literal the scanner reports, standing in for a credential a colleague pasted into a subject line.</summary>
    private const string Marker = "AKIAEXAMPLEKEY";

    private const string Card = """
        {\"note\": {\"text\": \"They lead the addendum renegotiation.\", \"sources\": [0]},
        \"openItem\": {\"text\": \"the indexation cap\", \"sources\": [1]}}
        """;

    private static readonly DateTimeOffset FirstJuly = new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly EmailThreadId TheThread = EmailThreadId.Create(Guid.CreateVersion7());

    private static readonly StoredEmailId TheMessage = StoredEmailId.Create(Guid.CreateVersion7());

    private static readonly StoredEmailId TheDocumentMessage = StoredEmailId.Create(Guid.CreateVersion7());

    /// <summary>An answer of the shape the instruction asks for is the card an opened contact is headed by.</summary>
    [Fact]
    public async Task DeriveAsync_AProviderThatAnsweredWithACard_DerivesIt()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Card));
        var agent = provider.DeriverOver();

        // Act
        var card = await agent.DeriveAsync(Brief(), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(card.WasDerived);
        Assert.Equal("They lead the addendum renegotiation.", card.Note?.Text);
        Assert.Equal(ContactRelationshipAspect.OpenItem, Assert.Single(card.Observations).Aspect);
    }

    /// <summary>A citation is a position the turn published, so no identifier of a stored message has to leave this deployment.</summary>
    [Fact]
    public async Task DeriveAsync_AnyDerivation_SendsTheSubjectsAndTheFileNamesAndNoIdentifier()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Card));
        var agent = provider.DeriverOver();

        // Act
        await agent.DeriveAsync(Brief(), TestContext.Current.CancellationToken);

        // Assert
        var sent = provider.RequestBodies[0];

        Assert.Contains("the addendum", sent, StringComparison.Ordinal);
        Assert.Contains("addendum.pdf", sent, StringComparison.Ordinal);
        Assert.DoesNotContain(TheThread.Value.ToString(), sent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(TheMessage.Value.ToString(), sent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(TheDocumentMessage.Value.ToString(), sent, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A subject is text a sender wrote, so what a deployment withholds from a provider is withheld here too.</summary>
    [Fact]
    public async Task DeriveAsync_ASubjectCarryingASecret_SendsTheProviderTheGuardedText()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Card));
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        using var actingFor = egress.ActingForUser();
        var agent = provider.DeriverOver(egressGuard: egress.Guard);

        // Act
        await agent.DeriveAsync(
            Brief(subject: $"the key {Marker}", fileName: $"{Marker}.pdf"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(Marker, provider.RequestBodies[0], StringComparison.Ordinal);
    }

    /// <summary>What a person has when a provider fails is the contact page they opened, so the card is withheld rather than the read refused.</summary>
    [Fact]
    public async Task DeriveAsync_AProviderThatRefusedTheCall_DerivesNothing()
    {
        // Arrange
        using var provider = ScriptedTransport.Refusing(HttpStatusCode.InternalServerError);
        var agent = provider.DeriverOver();

        // Act
        var card = await agent.DeriveAsync(Brief(), TestContext.Current.CancellationToken);

        // Assert
        Assert.False(card.WasDerived);
    }

    /// <summary>An endpoint the configuration no longer declares is a failure with no health record, and it ends the same way.</summary>
    [Fact]
    public async Task DeriveAsync_ACredentialThatCouldNotBeResolved_DerivesNothing()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Card));
        var agent = provider.DeriverOver(
            credentialFailure: new InvalidOperationException("the alias names no endpoint"));

        // Act
        var card = await agent.DeriveAsync(Brief(), TestContext.Current.CancellationToken);

        // Assert
        Assert.False(card.WasDerived);
        Assert.Equal(0, provider.RequestCount);
    }

    /// <summary>
    /// A card arrives because a page was opened rather than because a button was pressed, so a spent allowance withholds
    /// it instead of refusing the contact — which is where this parts company with a drafted reply, and it is refused
    /// before anything is composed or scanned.
    /// </summary>
    [Fact]
    public async Task DeriveAsync_APeriodThatHasSpentItsAllowance_DerivesNothingWithoutReachingTheProvider()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Card));
        var spendLedger = Substitute.For<IMailAnsweringSpendLedger>();
        spendLedger.TryAdmitRunAsync(Arg.Any<CancellationToken>()).Returns(false);
        var agent = provider.DeriverOver(spendLedger: spendLedger);

        // Act
        var card = await agent.DeriveAsync(Brief(), TestContext.Current.CancellationToken);

        // Assert
        Assert.False(card.WasDerived);
        Assert.Equal(0, provider.RequestCount);
    }

    /// <summary>One opened contact is one call, because the agent reaches no tool and is shown nothing else.</summary>
    [Fact]
    public async Task DeriveAsync_AnyDerivation_ReachesTheProviderExactlyOnce()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Card));
        var agent = provider.DeriverOver();

        // Act
        await agent.DeriveAsync(Brief(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, provider.RequestCount);
    }

    /// <summary>A derivation is about a correspondence, so nothing is the argument mistake it looks like.</summary>
    [Fact]
    public async Task DeriveAsync_WithoutABrief_IsRefused()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(Card));
        var agent = provider.DeriverOver();

        // Act and assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => agent.DeriveAsync(null!, TestContext.Current.CancellationToken));
    }

    private static ContactRelationshipBrief Brief(
        string subject = "the addendum",
        string fileName = "addendum.pdf",
        UserLanguage language = UserLanguage.English) =>
        new(
            new ContactCorrespondence(
                [new CorrespondingThread(TheThread, TheMessage, subject, FirstJuly)],
                [new CorrespondingDocument(TheDocumentMessage, 3, fileName, "application/pdf", FirstJuly)]),
            language);

    /// <summary>Builds the chat-completion payload a provider answers with.</summary>
    private static string Completion(string content) =>
        "{\"id\":\"chatcmpl-1\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"a-chat-model\","
        + "\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\""
        + content.ReplaceLineEndings(string.Empty)
        + "\"},\"finish_reason\":\"stop\"}]}";

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

        public ContactRelationshipAgent DeriverOver(
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

            return new ContactRelationshipAgent(
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
                NullLogger<ContactRelationshipAgent>.Instance);
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
