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
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Resilience;
using MailFathom.Application.Retrieval;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MailFathom.AI.UnitTests.Discovery;

/// <summary>Covers what one derivation sends, what it makes of the answer, and what it does when there is none.</summary>
/// <remarks>
/// The derivation goes over a real provider client and a scripted transport, so what is exercised is the client
/// construction, the credential resolution, and the turn that actually left this deployment.
/// </remarks>
public sealed class DiscoveryPlanningAgentTests
{
    /// <summary>The literal the scanner in the guarded-egress test reports, standing in for a credential in a question.</summary>
    private const string Marker = "AKIAEXAMPLEKEY";

    private static readonly MailboxScope WholeMailbox = MailboxScope.Create(
        SyntheticMailOwner.Deployment,
        [MailAccountId.Create("primary")],
        []);

    [Fact]
    public async Task DerivePlanAsync_AProviderThatAnsweredWithAPlan_DerivesThatPlan()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(
            """{\"intent\": \"compareTerms\", \"sufficientPassages\": 6, \"lookups\": [{\"queryText\": \"quotation\"}]}"""));
        var planner = provider.PlannerOver();

        // Act
        var plan = await planner.DerivePlanAsync(Question(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(DiscoveryIntent.CompareTerms, plan.Intent);
        Assert.Equal(["quotation"], plan.Retrieval.Lookups.Select(lookup => lookup.QueryText));
        Assert.Equal(6, plan.Retrieval.SufficientPassages);
    }

    /// <summary>An answer nothing can be read out of leaves a run with the plan a deployment with no model would run.</summary>
    [Fact]
    public async Task DerivePlanAsync_AProviderThatAnsweredWithProse_FallsBackToTheQuestionsOwnWords()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion("I am afraid I cannot help with that."));
        var planner = provider.PlannerOver();

        // Act
        var plan = await planner.DerivePlanAsync(Question(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(DiscoveryIntent.Unclassified, plan.Intent);
        Assert.Equal(
            ["which supplier quoted least"],
            plan.Retrieval.Lookups.Select(lookup => lookup.QueryText));
    }

    /// <summary>A provider that refused the call is a worse plan rather than no answer, so the run goes on.</summary>
    [Fact]
    public async Task DerivePlanAsync_AProviderThatRefusedTheCall_StillProducesARunnablePlan()
    {
        // Arrange
        using var provider = ScriptedTransport.Refusing(HttpStatusCode.BadRequest);
        var planner = provider.PlannerOver();

        // Act
        var plan = await planner.DerivePlanAsync(Question(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(DiscoveryIntent.Unclassified, plan.Intent);
        Assert.Single(plan.Retrieval.Lookups);
    }

    /// <summary>A key that would not resolve never reaches the endpoint, and is the same worse plan a refusal is.</summary>
    [Fact]
    public async Task DerivePlanAsync_ACredentialThatCannotBeResolved_StillProducesARunnablePlan()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(
            """{\"intent\": \"findFact\", \"lookups\": [{\"queryText\": \"invoice\"}]}"""));
        var planner = provider.PlannerOver(credentialFailure: new InvalidOperationException(
            "The provider key of AI endpoint 'a-chat-endpoint' could not be resolved."));

        // Act
        var plan = await planner.DerivePlanAsync(Question(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(DiscoveryIntent.Unclassified, plan.Intent);
        Assert.Single(plan.Retrieval.Lookups);
        Assert.Equal(0, provider.RequestCount);
    }

    /// <summary>A question is a prompt somebody wrote, so what a deployment withholds from a provider is withheld here too.</summary>
    [Fact]
    public async Task DerivePlanAsync_AQuestionCarryingASecret_SendsTheProviderTheGuardedText()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(
            """{\"intent\": \"findFact\", \"lookups\": [{\"queryText\": \"key\"}]}"""));
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        using var actingFor = egress.ActingForOwner();
        var planner = provider.PlannerOver(egressGuard: egress.Guard);
        var question = new MailQuestion(
            MailQuestionText.Create($"what do I do about the key {Marker} a colleague sent"),
            WholeMailbox);

        // Act
        await planner.DerivePlanAsync(question, TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(Marker, provider.RequestBodies[0], StringComparison.Ordinal);
    }

    /// <summary>The scope reaches the model as how much was selected, never as an identifier of what.</summary>
    [Fact]
    public async Task DerivePlanAsync_AQuestionAboutSelectedMessages_TellsTheModelHowManyAndNothingThatNamesThem()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(
            """{\"intent\": \"findFact\", \"lookups\": [{\"queryText\": \"invoice\"}]}"""));
        var planner = provider.PlannerOver();
        var selected = StoredEmailId.Create(new Guid("99999999-9999-9999-9999-999999999999"));
        var question = new MailQuestion(
            MailQuestionText.Create("which supplier quoted least"),
            WholeMailbox.NarrowedToEmails([selected]));

        // Act
        await planner.DerivePlanAsync(question, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("1 individually selected messages", provider.RequestBodies[0], StringComparison.Ordinal);
        Assert.DoesNotContain(selected.Value.ToString(), provider.RequestBodies[0], StringComparison.Ordinal);
    }

    /// <summary>No mail is shown to a derivation, so one call is the whole of what a question costs here.</summary>
    [Fact]
    public async Task DerivePlanAsync_AnyQuestion_ReachesTheProviderExactlyOnce()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(
            """{\"intent\": \"findFact\", \"lookups\": [{\"queryText\": \"invoice\"}]}"""));
        var planner = provider.PlannerOver();

        // Act
        await planner.DerivePlanAsync(Question(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, provider.RequestCount);
    }

    private static MailQuestion Question() =>
        new(MailQuestionText.Create("which supplier quoted least"), WholeMailbox);

    /// <summary>Builds the chat-completion payload a provider answers with.</summary>
    private static string Completion(string content) =>
        "{\"id\":\"chatcmpl-1\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"a-chat-model\","
        + "\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\""
        + content
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

        public DiscoveryPlanningAgent PlannerOver(
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

            return new DiscoveryPlanningAgent(
                ChatDeclarations.Plan(),
                EmailKnowledgeBounds.Default,
                credentialSource,
                new OpenAiCompatibleClientFactory(),
                transportFactory,
                operationRunner,
                Substitute.For<IAiProviderHealthRecorder>(),
                egressGuard ?? SensitiveContentEgressGuards.Inactive(),
                new EmptyAgentInstructionEnvelope(),
                NullLoggerFactory.Instance,
                NullLogger<DiscoveryPlanningAgent>.Instance);
        }

        public void Dispose() => this.handler.Dispose();
    }
}
