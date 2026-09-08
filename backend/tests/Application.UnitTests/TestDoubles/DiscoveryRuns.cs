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
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.Synchronization.Administration;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Application.UnitTests.Discovery.Presentation;
using MailFathom.Domain.Access;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Composes a Discover run over a deployment a test states one switch of at a time.</summary>
/// <remarks>
/// A run reaches its refusals through a capability composed of an embedding profile, two provider health readings, and
/// a chat declaration, so arranging one is several objects rather than a constructor. It is composed here so that the
/// tests covering the run and the tests covering the stream over it exercise the same deployment, and so that a test
/// varying one switch states only that switch.
/// </remarks>
internal static class DiscoveryRuns
{
    /// <summary>When the composed deployment last read each provider's health.</summary>
    public static readonly DateTimeOffset Now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    private static readonly EmbeddingProfileId ProfileId =
        EmbeddingProfileId.Create(new Guid("0f9d6b0b-2f1e-4c2a-9a3d-7c8e5f4a1b20"));

    /// <summary>Composes the run one question is answered through.</summary>
    /// <param name="planner">The derivation, or <see langword="null" /> for a deployment that composes no chat agent.</param>
    /// <param name="search">This deployment's retrieval.</param>
    /// <param name="embeddingProfileActive">Whether a profile exists for a question to be placed beside mail with.</param>
    /// <param name="chatState">What the chat provider's health last read as.</param>
    /// <param name="authorization">Who reached the use case, defaulting to a caller granted the asking permission.</param>
    /// <param name="egressGuard">What this user's posture withholds, defaulting to a deployment that scans nobody.</param>
    /// <param name="composer">What the run composes its answer through, defaulting to one returning the contract's own example.</param>
    /// <param name="folders">How current each folder the run read was, defaulting to a scope reporting none.</param>
    /// <param name="ledger">What this run has spent, defaulting to an untouched ledger under the deployment's default ceilings.</param>
    /// <param name="spendLedger">What the current period has spent, defaulting to a period that admits the run.</param>
    /// <returns>The composed run.</returns>
    public static DiscoveryRun Composing(
        IDiscoveryRunPlanner? planner,
        IEmailKnowledgeSearch search,
        bool embeddingProfileActive = true,
        AiProviderHealthState chatState = AiProviderHealthState.Serving,
        AccessAuthorization? authorization = null,
        SensitiveContentEgressGuard? egressGuard = null,
        IDiscoveryResultComposer? composer = null,
        IReadOnlyList<MailboxFolderFreshness>? folders = null,
        MailAnsweringRunLedger? ledger = null,
        IMailAnsweringSpendLedger? spendLedger = null)
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

        var freshnessReader = Substitute.For<ISynchronizationFreshnessReader>();
        freshnessReader.ReadAsync(Arg.Any<MailboxScope>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(folders ?? []));

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
            new PlannedMailRetrieval(search, ledger ?? NewRunLedger()),
            authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailAsk),
            egressGuard ?? SensitiveContentEgressGuards.Inactive(),
            new DiscoveryCoverageReader(freshnessReader, new MailSynchronizationRunLedger(timeProvider)),
            spendLedger ?? PeriodAdmitting(),
            planner,
            // The two halves of one deployment's chat configuration: an instance that derives a plan composes a result
            // from it, and an instance that declared no endpoint has neither.
            planner is null ? null : composer ?? ComposerReturning(PresentationPlanExample.Compose()));
    }

    /// <summary>Builds a ledger for one run under this deployment's default ceilings, or under ceilings a test states.</summary>
    /// <param name="retrievedCharacters">What one run may draw out of the mailbox.</param>
    /// <param name="providerCalls">What one run may call.</param>
    /// <param name="tokens">What one run may consume.</param>
    /// <returns>The ledger.</returns>
    public static MailAnsweringRunLedger NewRunLedger(
        int retrievedCharacters = 20_000,
        int providerCalls = 8,
        long tokens = 80_000) =>
        new(MailAnsweringRunBounds.Create(retrievedCharacters, providerCalls, tokens));

    /// <summary>Stands in for a period with allowance left, which is what every test but the refusal's own arranges.</summary>
    /// <returns>The ledger.</returns>
    public static IMailAnsweringSpendLedger PeriodAdmitting()
    {
        var ledger = Substitute.For<IMailAnsweringSpendLedger>();
        ledger.TryAdmitRun().Returns(true);

        return ledger;
    }

    /// <summary>Stands in for a period that has spent its allowance, so the next question is refused before it runs.</summary>
    /// <returns>The ledger.</returns>
    public static IMailAnsweringSpendLedger PeriodSpent()
    {
        var ledger = Substitute.For<IMailAnsweringSpendLedger>();
        ledger.TryAdmitRun().Returns(false);

        return ledger;
    }

    /// <summary>Stands in for the composition, answering with the plan a test wants streamed rather than reading anything.</summary>
    /// <param name="presentation">The plan the composition answers with.</param>
    /// <returns>The composition.</returns>
    public static IDiscoveryResultComposer ComposerReturning(PresentationPlan presentation)
    {
        var composer = Substitute.For<IDiscoveryResultComposer>();
        composer.ComposeAsync(
                Arg.Any<MailQuestion>(),
                Arg.Any<DiscoveryRunPlan>(),
                Arg.Any<DiscoveryEvidence>(),
                Arg.Any<IReadOnlyList<AccountCoverage>>(),
                Arg.Any<CancellationToken>())
            .Returns(presentation);

        return composer;
    }

    /// <summary>Stands in for the derivation, producing the plan a test wants run rather than reading the question.</summary>
    /// <param name="intent">What the plan says the question asks for.</param>
    /// <param name="sufficientPassages">How much evidence the plan calls enough.</param>
    /// <param name="queries">The lookups the plan names, in the order it names them.</param>
    /// <returns>The derivation.</returns>
    public static IDiscoveryRunPlanner PlannerDeriving(
        DiscoveryIntent intent,
        int sufficientPassages,
        params string[] queries)
    {
        var planner = Substitute.For<IDiscoveryRunPlanner>();
        planner.DerivePlanAsync(Arg.Any<MailQuestion>(), Arg.Any<CancellationToken>())
            .Returns(DiscoveryRunPlan.Compose(
                intent,
                RetrievalPlan.Create(
                    EmailKnowledgeBounds.Default,
                    [.. queries.Select(EmailKnowledgeQuery.ForText)],
                    sufficientPassages)));

        return planner;
    }

    /// <summary>Stands in for a derivation that reached a ceiling, which is where a run's own spend refusal comes from.</summary>
    /// <param name="refusal">The refusal the derivation raises.</param>
    /// <returns>The derivation.</returns>
    /// <remarks>
    /// The chat client the agents wrap is what counts a call against the run and the period, so a ceiling reached mid-run
    /// arrives at the run as this exception out of whichever port was calling. Raising it from the derivation exercises
    /// that path without composing a provider.
    /// </remarks>
    public static IDiscoveryRunPlanner PlannerRefusing(MailAnsweringBudgetExhaustedException refusal)
    {
        var planner = Substitute.For<IDiscoveryRunPlanner>();
        planner.DerivePlanAsync(Arg.Any<MailQuestion>(), Arg.Any<CancellationToken>())
            .Returns<DiscoveryRunPlan>(_ => throw refusal);

        return planner;
    }

    private static EmbeddingProfileIdentity Identity() => EmbeddingProfileIdentity.Create(
        "a-provider",
        "a-model",
        modelVersion: null,
        dimension: 8,
        EmbeddingDistanceMetric.Cosine,
        EmbeddingInputPreparation.Create(2_000, passageInstruction: null, normalizesVector: true));
}
