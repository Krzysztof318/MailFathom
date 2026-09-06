// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Discovery.Planning;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Discovery.Runs;

/// <summary>Covers what one Discover run refuses, what it derives, and what it records having done.</summary>
public sealed class DiscoveryRunTests
{
    /// <summary>The literal the scanner in the guarded-egress test reports, standing in for a credential in a question.</summary>
    private const string Marker = "AKIAEXAMPLEKEY";

    /// <summary>How much evidence the derived plan calls enough, which no test here varies.</summary>
    private const int SufficientPassages = 5;

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
        var run = DiscoveryRuns.Composing(DiscoveryRuns.PlannerDeriving(DiscoveryIntent.CompareTerms, SufficientPassages, "quotation"), search);

        // Act
        var result = await run.RunAsync(Question, progress: null, TestContext.Current.CancellationToken);

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
        var run = DiscoveryRuns.Composing(DiscoveryRuns.PlannerDeriving(DiscoveryIntent.FindFact, SufficientPassages, "quotation", "oferta"), search);

        // Act
        await run.RunAsync(Question, progress: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["quotation", "oferta"], search.Lookups.Select(lookup => lookup.QueryText));
    }

    /// <summary>An instance that declared no chat endpoint answers no question and says so rather than failing to resolve.</summary>
    [Fact]
    public async Task RunAsync_NoPlannerRegistered_RefusesAsNotServed()
    {
        // Arrange
        var run = DiscoveryRuns.Composing(planner: null, new ScriptedEmailKnowledgeSearch());

        // Act
        var refusal = await Assert.ThrowsAsync<MailAnsweringUnavailableException>(() =>
            run.RunAsync(Question, progress: null, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailAnsweringAvailability.Inactive, refusal.Availability);
    }

    /// <summary>A deployment with no embedding profile cannot place a question beside mail, which is the same refusal.</summary>
    [Fact]
    public async Task RunAsync_NoEmbeddingProfileConfigured_RefusesAsNotServed()
    {
        // Arrange
        var run = DiscoveryRuns.Composing(
            DiscoveryRuns.PlannerDeriving(DiscoveryIntent.FindFact, SufficientPassages, "quotation"),
            new ScriptedEmailKnowledgeSearch(),
            embeddingProfileActive: false);

        // Act
        var refusal = await Assert.ThrowsAsync<MailAnsweringUnavailableException>(() =>
            run.RunAsync(Question, progress: null, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailAnsweringAvailability.Inactive, refusal.Availability);
    }

    /// <summary>A provider refusing right now is a different answer from a deployment that answers nothing at all.</summary>
    [Fact]
    public async Task RunAsync_AChatProviderRefusingRecently_RefusesAsTemporarilyUnable()
    {
        // Arrange
        var run = DiscoveryRuns.Composing(
            DiscoveryRuns.PlannerDeriving(DiscoveryIntent.FindFact, SufficientPassages, "quotation"),
            new ScriptedEmailKnowledgeSearch(),
            chatState: AiProviderHealthState.Unavailable);

        // Act
        var refusal = await Assert.ThrowsAsync<MailAnsweringUnavailableException>(() =>
            run.RunAsync(Question, progress: null, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailAnsweringAvailability.Degraded, refusal.Availability);
    }

    /// <summary>A run reads mail and sends it to a provider, so it is the asking grant that publishes it.</summary>
    [Fact]
    public async Task RunAsync_ACallerGrantedReadingAlone_IsRefused()
    {
        // Arrange
        var run = DiscoveryRuns.Composing(
            DiscoveryRuns.PlannerDeriving(DiscoveryIntent.FindFact, SufficientPassages, "quotation"),
            new ScriptedEmailKnowledgeSearch(),
            authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            run.RunAsync(Question, progress: null, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailAsk, refusal.RequiredPermission);
    }

    /// <summary>Nothing runs before the grant is read, so a caller that may not ask reaches no derivation.</summary>
    [Fact]
    public async Task RunAsync_ACallerGrantedNothing_ReachesNoDerivation()
    {
        // Arrange
        var planner = DiscoveryRuns.PlannerDeriving(DiscoveryIntent.FindFact, SufficientPassages, "quotation");
        var run = DiscoveryRuns.Composing(
            planner,
            new ScriptedEmailKnowledgeSearch(),
            authorization: AccessAuthorizations.ForCallerGranted());

        // Act
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            run.RunAsync(Question, progress: null, TestContext.Current.CancellationToken));

        // Assert
        Assert.Empty(planner.ReceivedCalls());
    }

    /// <summary>
    /// The question a derivation sends is this owner's own text, and the guard refuses to judge any text on a flow
    /// acting for nobody wherever the deployment scans somebody. So the run states the owner before it derives: without
    /// that, a deployment with a scanner switched on would refuse every Discover run before it reached the model.
    /// </summary>
    [Fact]
    public async Task RunAsync_ADeploymentThatScansSomebody_GuardsTheQuestionUnderTheCallersOwnPosture()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        var guarding = new GuardingDiscoveryRunPlanner(egress.Guard);
        var run = DiscoveryRuns.Composing(guarding, new ScriptedEmailKnowledgeSearch(), egressGuard: egress.Guard);
        var question = new MailQuestion(
            MailQuestionText.Create($"what about the key {Marker} a colleague sent"),
            Question.Scope);

        // Act
        await run.RunAsync(question, progress: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(guarding.Guarded);
        Assert.DoesNotContain(Marker, guarding.Guarded, StringComparison.Ordinal);
    }

    /// <summary>A derivation as the real one behaves at the egress point, without a provider behind it.</summary>
    private sealed class GuardingDiscoveryRunPlanner(SensitiveContentEgressGuard egressGuard) : IDiscoveryRunPlanner
    {
        public string? Guarded { get; private set; }

        public async Task<DiscoveryRunPlan> DerivePlanAsync(
            MailQuestion question,
            CancellationToken cancellationToken)
        {
            this.Guarded = await egressGuard.GuardAsync(
                SensitiveContentEgressPoint.ChatPrompt,
                question.Text.Value,
                cancellationToken);

            return DiscoveryRunPlan.Compose(
                DiscoveryIntent.Unclassified,
                RetrievalPlan.Create(
                    EmailKnowledgeBounds.Default,
                    [EmailKnowledgeQuery.ForText("quotation")],
                    sufficientPassages: 5));
        }
    }
}
