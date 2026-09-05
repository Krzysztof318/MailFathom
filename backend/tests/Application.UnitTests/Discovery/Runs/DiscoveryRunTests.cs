// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Discovery.Planning;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Runs;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Discovery.Runs;

/// <summary>Covers what one Discover run refuses, what it derives, and what it records having done.</summary>
public sealed class DiscoveryRunTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    private static readonly EmbeddingProfileId ProfileId =
        EmbeddingProfileId.Create(new Guid("0f9d6b0b-2f1e-4c2a-9a3d-7c8e5f4a1b20"));

    private static readonly MailQuestion Question = new(
        MailQuestionText.Create("which supplier quoted least"),
        MailboxScope.Create(SyntheticMailOwner.Deployment, [MailAccountId.Create("primary")], []));

    /// <summary>Both halves of what a run decided are its record, so neither is discarded once the other exists.</summary>
    [Fact]
    public async Task RunAsync_ADeploymentThatAnswersQuestions_RecordsBothThePlanAndWhatItRetrieved()
    {
        // Arrange
        var search = new ScriptedEmailKnowledgeSearch()
            .Returning("quotation", ScriptedEmailKnowledgeSearch.Passage("the quotation"));
        var run = RunOver(PlannerDeriving(DiscoveryIntent.CompareTerms, "quotation"), search);

        // Act
        var result = await run.RunAsync(Question, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(DiscoveryIntent.CompareTerms, result.Plan.Intent);
        Assert.Equal(PresentationBlockType.FactTable, result.Plan.Composition[0]);
        Assert.Equal(["the quotation"], result.Evidence.Passages.Select(passage => passage.Text));
    }

    /// <summary>The plan the derivation produced is what runs, so the lookups reaching retrieval are its own.</summary>
    [Fact]
    public async Task RunAsync_ADerivedPlan_RunsTheLookupsThatPlanNames()
    {
        // Arrange
        var search = new ScriptedEmailKnowledgeSearch();
        var run = RunOver(PlannerDeriving(DiscoveryIntent.FindFact, "quotation", "oferta"), search);

        // Act
        await run.RunAsync(Question, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["quotation", "oferta"], search.Lookups.Select(lookup => lookup.QueryText));
    }

    /// <summary>An instance that declared no chat endpoint answers no question and says so rather than failing to resolve.</summary>
    [Fact]
    public async Task RunAsync_NoPlannerRegistered_RefusesAsNotServed()
    {
        // Arrange
        var run = RunOver(planner: null, new ScriptedEmailKnowledgeSearch());

        // Act
        var refusal = await Assert.ThrowsAsync<MailAnsweringUnavailableException>(() =>
            run.RunAsync(Question, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailAnsweringAvailability.Inactive, refusal.Availability);
    }

    /// <summary>A deployment with no embedding profile cannot place a question beside mail, which is the same refusal.</summary>
    [Fact]
    public async Task RunAsync_NoEmbeddingProfileConfigured_RefusesAsNotServed()
    {
        // Arrange
        var run = RunOver(
            PlannerDeriving(DiscoveryIntent.FindFact, "quotation"),
            new ScriptedEmailKnowledgeSearch(),
            embeddingProfileActive: false);

        // Act
        var refusal = await Assert.ThrowsAsync<MailAnsweringUnavailableException>(() =>
            run.RunAsync(Question, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailAnsweringAvailability.Inactive, refusal.Availability);
    }

    /// <summary>A provider refusing right now is a different answer from a deployment that answers nothing at all.</summary>
    [Fact]
    public async Task RunAsync_AChatProviderRefusingRecently_RefusesAsTemporarilyUnable()
    {
        // Arrange
        var run = RunOver(
            PlannerDeriving(DiscoveryIntent.FindFact, "quotation"),
            new ScriptedEmailKnowledgeSearch(),
            chatState: AiProviderHealthState.Unavailable);

        // Act
        var refusal = await Assert.ThrowsAsync<MailAnsweringUnavailableException>(() =>
            run.RunAsync(Question, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailAnsweringAvailability.Degraded, refusal.Availability);
    }

    /// <summary>A run reads mail and sends it to a provider, so it is the asking grant that publishes it.</summary>
    [Fact]
    public async Task RunAsync_ACallerGrantedReadingAlone_IsRefused()
    {
        // Arrange
        var run = RunOver(
            PlannerDeriving(DiscoveryIntent.FindFact, "quotation"),
            new ScriptedEmailKnowledgeSearch(),
            authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            run.RunAsync(Question, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailAsk, refusal.RequiredPermission);
    }

    /// <summary>Nothing runs before the grant is read, so a caller that may not ask reaches no derivation.</summary>
    [Fact]
    public async Task RunAsync_ACallerGrantedNothing_ReachesNoDerivation()
    {
        // Arrange
        var planner = PlannerDeriving(DiscoveryIntent.FindFact, "quotation");
        var run = RunOver(
            planner,
            new ScriptedEmailKnowledgeSearch(),
            authorization: AccessAuthorizations.ForCallerGranted());

        // Act
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            run.RunAsync(Question, TestContext.Current.CancellationToken));

        // Assert
        Assert.Empty(planner.ReceivedCalls());
    }

    private static IDiscoveryRunPlanner PlannerDeriving(DiscoveryIntent intent, params string[] queries)
    {
        var planner = Substitute.For<IDiscoveryRunPlanner>();
        planner.DerivePlanAsync(Arg.Any<MailQuestion>(), Arg.Any<CancellationToken>())
            .Returns(DiscoveryRunPlan.Compose(
                intent,
                RetrievalPlan.Create(
                    EmailKnowledgeBounds.Default,
                    [.. queries.Select(EmailKnowledgeQuery.ForText)],
                    sufficientPassages: 5)));

        return planner;
    }

    private static DiscoveryRun RunOver(
        IDiscoveryRunPlanner? planner,
        IEmailKnowledgeSearch search,
        bool embeddingProfileActive = true,
        AiProviderHealthState chatState = AiProviderHealthState.Serving,
        AccessAuthorization? authorization = null)
    {
        // Both roles are read through one reader, as the host composes them, so a test that varies one states the other.
        var healthReader = Substitute.For<IAiProviderHealthReader>();
        healthReader.Read(AiProviderRole.Embedding)
            .Returns(new AiProviderHealth(AiProviderRole.Embedding, AiProviderHealthState.Serving, Now));
        healthReader.Read(AiProviderRole.Chat)
            .Returns(new AiProviderHealth(AiProviderRole.Chat, chatState, Now));

        var profileReader = Substitute.For<IActiveEmbeddingProfileReader>();
        profileReader.FindActiveProfileAsync(Arg.Any<CancellationToken>())
            .Returns(embeddingProfileActive ? new RegisteredEmbeddingProfile(ProfileId, Identity()) : null);

        var timeProvider = new FakeTimeProvider(Now);

        return new DiscoveryRun(
            new MailAnsweringCapability(
                new SemanticEmailSearch(
                    profileReader,
                    new InMemoryEmailVectorSearchIndex(),
                    healthReader,
                    timeProvider,
                    new ScriptedTextEmbeddingGenerator(Identity(), maximumPassagesPerCall: 8)),
                healthReader,
                timeProvider,
                // The answering agent stands in for the chat half of the configuration, which is the half the
                // capability reads: this deployment registers the planner and the answerer behind one declaration.
                planner is null ? null : new RecordingMailQuestionAnswerer()),
            new PlannedMailRetrieval(search),
            authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailAsk),
            planner);
    }

    private static EmbeddingProfileIdentity Identity() => EmbeddingProfileIdentity.Create(
        "a-provider",
        "a-model",
        modelVersion: null,
        dimension: 8,
        EmbeddingDistanceMetric.Cosine,
        EmbeddingInputPreparation.Create(2_000, passageInstruction: null, normalizesVector: true));
}
