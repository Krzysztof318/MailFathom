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
    /// <param name="egressGuard">What this owner's posture withholds, defaulting to a deployment that scans nobody.</param>
    /// <param name="composer">What the run composes its answer through, defaulting to one returning the contract's own example.</param>
    /// <param name="folders">How current each folder the run read was, defaulting to a scope reporting none.</param>
    /// <returns>The composed run.</returns>
    public static DiscoveryRun Composing(
        IDiscoveryRunPlanner? planner,
        IEmailKnowledgeSearch search,
        bool embeddingProfileActive = true,
        AiProviderHealthState chatState = AiProviderHealthState.Serving,
        AccessAuthorization? authorization = null,
        SensitiveContentEgressGuard? egressGuard = null,
        IDiscoveryResultComposer? composer = null,
        IReadOnlyList<MailboxFolderFreshness>? folders = null)
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
            new PlannedMailRetrieval(search),
            authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailAsk),
            egressGuard ?? SensitiveContentEgressGuards.Inactive(),
            new DiscoveryCoverageReader(freshnessReader, new MailSynchronizationRunLedger(timeProvider)),
            planner,
            // The two halves of one deployment's chat configuration: an instance that derives a plan composes a result
            // from it, and an instance that declared no endpoint has neither.
            planner is null ? null : composer ?? ComposerReturning(PresentationPlanExample.Compose()));
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

    private static EmbeddingProfileIdentity Identity() => EmbeddingProfileIdentity.Create(
        "a-provider",
        "a-model",
        modelVersion: null,
        dimension: 8,
        EmbeddingDistanceMetric.Cosine,
        EmbeddingInputPreparation.Create(2_000, passageInstruction: null, normalizesVector: true));
}
