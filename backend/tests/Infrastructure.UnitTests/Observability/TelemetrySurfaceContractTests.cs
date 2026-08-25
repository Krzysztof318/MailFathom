// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.AiProviders;
using MailFathom.Application.Chat;
using MailFathom.Application.Contacts.Collection;
using MailFathom.Application.EmailContent.Move;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Backfill;
using MailFathom.Application.Emails.Embeddings.Generations;
using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.Emails.Embeddings.Vectorization;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;
using MailFathom.Application.Jobs.Scheduling;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Application.Mail.Delivery.Filing;
using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Mail.Maintenance;
using MailFathom.Application.Mail.Mutations.Convergence;
using MailFathom.Application.Observability;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.SensitiveContent.Redaction;
using MailFathom.Application.Spam.Gating;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Answering.Audit;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Mutations.Audit;
using MailFathom.Infrastructure.Embeddings;
using MailFathom.Infrastructure.ObjectStorage;
using MailFathom.Infrastructure.Observability;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

/// <summary>Asserts the redaction contract over everything this assembly publishes, rather than per publisher.</summary>
/// <remarks>
/// <para>
/// Each publisher's own tests assert what it measured and what it tagged, and that is the right level for a feature.
/// It is the wrong level for the privacy rule, which is a property of the surface as a whole: a rule checked once per
/// publisher is a rule the next publisher is not covered by, and the whole reason the surface is worth a contract is
/// that a signal added in a year is the one nobody will remember to check.
/// </para>
/// <para>
/// So this drives every publisher the assembly has, with every string a caller or a message could supply replaced by a
/// sentinel, and asserts over what came out. A publisher that put a subject, a remote folder path, an IMAP command, or
/// a caller's text into a name or a dimension fails here whatever its own tests say about it, and a publisher nobody
/// added to the drive fails the last test in the class rather than going unasserted.
/// </para>
/// <para>
/// The publishers are static and built once. Their instruments live on the process-wide meter and an observable gauge
/// registered there answers for the rest of the run, so an instance per test would leave one gauge per test reporting
/// this suite's numbers into whatever observes the meter next. Building them once, in a collection that runs by itself
/// after everything else, is what bounds that to one instance nobody observes afterwards.
/// </para>
/// </remarks>
[Collection(TelemetrySurfaceCollectionDefinition.Name)]
public sealed class TelemetrySurfaceContractTests
{
    /// <summary>An account alias with nothing real in it, which the contract permits a dimension to carry.</summary>
    private static readonly MailAccountId Account =
        MailAccountId.Create(TelemetryRedactionContract.ConfiguredAliasSentinel);

    /// <summary>A folder alias, which is the operator's own word and therefore also permitted.</summary>
    private static readonly MailFolderAlias FolderAlias =
        MailFolderAlias.Create(TelemetryRedactionContract.ConfiguredAliasSentinel);

    private static readonly DateTimeOffset Moment = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);

    private static readonly FakeTimeProvider Clock = new(Moment);

    private static readonly AiProviderHealthTracker ProviderHealth =
        new(Clock, NullLogger<AiProviderHealthTracker>.Instance);

    private static readonly AuthorizationRefusalTelemetry AuthorizationRefusals =
        new(NullLogger<AuthorizationRefusalTelemetry>.Instance);

    private static readonly ContactCollectionTelemetry ContactCollection = new();
    private static readonly ContentObjectReclamationTelemetry ContentObjectReclamation = new();
    private static readonly DerivedWorkGateTelemetry DerivedWorkGate = new();
    private static readonly EmailEmbeddingBackfillTelemetry EmbeddingBackfill = new();
    private static readonly EmailEmbeddingTelemetry Embedding = new();
    private static readonly JobQueueTelemetry JobQueue = new();

    private static readonly MailAnsweringAuditTelemetry AnsweringAudit =
        new(NullLogger<MailAnsweringAuditTelemetry>.Instance);

    private static readonly MailAnsweringRunTelemetry AnsweringRun = new();

    private static readonly MailAnsweringSpendTracker AnsweringSpend = new(
        MailAnsweringPeriodBounds.Create(TimeSpan.FromHours(1), maximumRuns: 30, maximumTokens: 300_000),
        Clock,
        NullLogger<MailAnsweringSpendTracker>.Instance);

    private static readonly MailDeliveryTelemetry Delivery = new(Clock);

    private static readonly MailboxContentVolumeTelemetry ContentVolume =
        new(NullLogger<MailboxContentVolumeTelemetry>.Instance);

    private static readonly MailboxConvergenceTelemetry Convergence =
        new(NullLogger<MailboxConvergenceTelemetry>.Instance, Clock);

    private static readonly MailboxMutationAuditTelemetry MutationAudit =
        new(NullLogger<MailboxMutationAuditTelemetry>.Instance);

    private static readonly MailboxMutationTelemetry Mutation =
        new(NullLogger<MailboxMutationTelemetry>.Instance, Clock);

    private static readonly MailboxReadTelemetry MailboxRead = new();
    private static readonly MailExtractionBackfillTelemetry ExtractionBackfill = new();
    private static readonly MailSynchronizationTelemetry Synchronization = new(Clock);
    private static readonly PersistenceCommitTelemetry PersistenceCommits = new();
    private static readonly SensitiveContentDerivationTelemetry Derivation = new();
    private static readonly SensitiveContentEgressTelemetry Egress = new();
    private static readonly ObjectStorageTelemetry ObjectStorage = new(Clock);
    private static readonly StoredContentMoveTelemetry ContentMove = new(Clock);
    private static readonly StoredEmailContentTelemetry StoredContent = new(Clock);
    private static readonly StoredMailRederivationTelemetry Rederivation = new(Clock);

    private static readonly BoundedEmailEmbeddingBacklog EmbeddingBacklog =
        new(new EmailEmbeddingBacklogOptions { Capacity = 4 });

    /// <summary>Every publisher this suite drives, which the discovery test holds the assembly against.</summary>
    private static readonly Type[] DrivenPublishers =
    [
        typeof(AiProviderHealthTracker),
        typeof(AuthorizationRefusalTelemetry),
        typeof(BoundedEmailEmbeddingBacklog),
        typeof(ContactCollectionTelemetry),
        typeof(ContentObjectReclamationTelemetry),
        typeof(DerivedWorkGateTelemetry),
        typeof(EmailEmbeddingBackfillTelemetry),
        typeof(EmailEmbeddingTelemetry),
        typeof(JobQueueTelemetry),
        typeof(MailAnsweringAuditTelemetry),
        typeof(MailAnsweringRunTelemetry),
        typeof(MailAnsweringSpendTracker),
        typeof(MailDeliveryTelemetry),
        typeof(MailboxContentVolumeTelemetry),
        typeof(MailboxConvergenceTelemetry),
        typeof(MailboxMutationAuditTelemetry),
        typeof(MailboxMutationTelemetry),
        typeof(MailboxReadTelemetry),
        typeof(MailExtractionBackfillTelemetry),
        typeof(MailSynchronizationTelemetry),
        typeof(ObjectStorageTelemetry),
        typeof(PersistenceCommitTelemetry),
        typeof(SensitiveContentDerivationTelemetry),
        typeof(SensitiveContentEgressTelemetry),
        typeof(StoredContentMoveTelemetry),
        typeof(StoredEmailContentTelemetry),
        typeof(StoredMailRederivationTelemetry),
    ];

    /// <summary>The drive really emits the surface, and the poison really travels through it.</summary>
    /// <remarks>
    /// Every other assertion in this class is that something is <b>absent</b>, and an absence proves nothing unless
    /// the same observation would report it present. This is that control. It establishes both halves the others rest
    /// on: that the drive produced a surface at all rather than a listener over nothing, and that a poisoned string
    /// does reach a dimension when the contract permits it — so the four sentinels the others look for are sentinels
    /// that would have been seen.
    /// </remarks>
    [Fact]
    public void EmittedSurface_EveryPublisherDriven_ReportsASurfaceWithThePoisonedInputInIt()
    {
        // Arrange
        using var surface = DriveEveryPublisher();

        // Act

        // Assert
        Assert.Contains(MailSynchronizationTelemetry.AccountRunSpanName, surface.Spans.Select(span => span.Name));
        Assert.Contains("mailfathom.mail.sync.run.duration", surface.InstrumentNames);
        Assert.Contains(surface.EmittedTags, tag => CarriesTheAlias(tag, MailSynchronizationTelemetry.AccountTagName));
        Assert.Contains(surface.EmittedTags, tag => CarriesTheAlias(tag, MailSynchronizationTelemetry.FolderTagName));
    }

    /// <summary>Nothing this process publishes is named after a message, a person, or a secret.</summary>
    [Fact]
    public void EmittedSurface_EveryPublisherDriven_IsNamedAfterNothingInAMailbox()
    {
        // Arrange
        using var surface = DriveEveryPublisher();

        // Act — the drive is the act; what is asserted is everything it emitted.

        // Assert
        TelemetryRedactionContract.AssertNothingIsNamedAfterMailOrASecret(surface.EmittedNames);
    }

    /// <summary>Every instrument and every dimension sits under the one name an operator filters this process by.</summary>
    [Fact]
    public void EmittedSurface_EveryPublisherDriven_IsNamespacedUnderMailFathom()
    {
        // Arrange
        using var surface = DriveEveryPublisher();

        // Act

        // Assert
        TelemetryRedactionContract.AssertEveryDimensionIsNamespacedUnderMailFathom(surface.InstrumentNames, surface.EmittedTags);
    }

    /// <summary>Every span is named after the operation it reports rather than after anything that operation saw.</summary>
    [Fact]
    public void EmittedSurface_EveryPublisherDriven_NamesEverySpanAfterItsOperation()
    {
        // Arrange
        using var surface = DriveEveryPublisher();

        // Act

        // Assert
        TelemetryRedactionContract.AssertEverySpanIsNamedAfterItsOperation(surface.SpanNames);
    }

    /// <summary>No caller's text and nothing read out of a message reached a name, a key, or a value.</summary>
    /// <remarks>
    /// This is the assertion the others cannot make. Every string handed to a publisher above is a sentinel, so where
    /// one surfaces is exactly where that class of input reaches an exporter — and an alias is the only class allowed
    /// to, on the dimensions named for it.
    /// </remarks>
    [Fact]
    public void EmittedSurface_EveryPublisherDrivenWithPoisonedInput_LetsNoneOfItReachAnExporter()
    {
        // Arrange
        using var surface = DriveEveryPublisher();

        // Act

        // Assert
        TelemetryRedactionContract.AssertNoPoisonedInputEscaped(surface.EmittedNames, surface.EmittedTags);
    }

    /// <summary>A publisher nobody added to the drive fails here rather than going unasserted.</summary>
    [Fact]
    public void EveryPublisherInTheAssembly_WhateverItIsCalled_IsDrivenByThisSuite() =>
        TelemetryRedactionContract.AssertEveryPublisherInTheAssemblyIsDriven(
            typeof(MailSynchronizationTelemetry).Assembly,
            DrivenPublishers);

    /// <summary>Reports whether one dimension came out carrying the alias the drive supplied.</summary>
    /// <remarks>
    /// Case-insensitively, because a folder alias is upper-cased on its way into the domain value: the sentinel that
    /// went in as one word comes out as the same word in the canonical casing, which is the alias arriving rather than
    /// a different string.
    /// </remarks>
    private static bool CarriesTheAlias(KeyValuePair<string, object?> tag, string dimension) =>
        StringComparer.Ordinal.Equals(tag.Key, dimension)
        && StringComparer.OrdinalIgnoreCase.Equals(
            tag.Value?.ToString(),
            TelemetryRedactionContract.ConfiguredAliasSentinel);

    /// <summary>Puts every publisher through the work it reports, with every string it accepts poisoned.</summary>
    private static EmittedTelemetrySurface DriveEveryPublisher()
    {
        var surface = new EmittedTelemetrySurface();

        DriveProviderHealth();
        DriveAuthorizationRefusals();
        DriveContactCollection();
        DriveDerivedWorkGate();
        DriveEmbedding();
        DriveJobQueue();
        DriveAnswering();
        DriveMailbox();
        DriveMutations();
        DriveDelivery();
        DriveSynchronization();
        DriveContentStore();
        DriveContentObjectReclamation();
        DriveContentMove();
        DriveObjectStorage();
        DriveSensitiveContent();
        DriveStoredMailRederivation();

        PersistenceCommits.RecordCommitted();
        PersistenceCommits.RecordConcurrencyConflict();
        EmbeddingBacklog.TryEnqueue(StoredEmailId.Create(Guid.CreateVersion7()));

        surface.ObserveGauges();

        return surface;
    }

    /// <summary>Drives a pass that carried a payload, refused one for every stated reason, and reached the end.</summary>
    /// <remarks>
    /// Every refusal is driven rather than one, because the reason is the dimension: a member added later and named off
    /// something a message carried would only reach an exporter through the branch that names it.
    /// </remarks>
    private static void DriveContentMove()
    {
        using var pass = ContentMove.BeginPass();

        pass.Copied(61_027);

        foreach (var failure in Enum.GetValues<StoredContentMoveFailure>())
        {
            pass.Failed(failure);
        }

        pass.ReachedEndOfContent();
    }

    /// <summary>Drives a segment that ended its run and one that handed the rest on, over both shapes of scope.</summary>
    /// <remarks>
    /// A whole-account run reports the publisher's own word for "every folder" rather than an alias, so both are driven
    /// here: the narrowed scope is what proves an alias reaches the dimension, and the wide one what proves the word
    /// standing in for it is not read off anything.
    /// </remarks>
    private static void DriveStoredMailRederivation()
    {
        using (var run = Rederivation.BeginRun(Account, FolderAlias))
        {
            using (var pass = run.BeginPass())
            {
                pass.Completed(new StoredMailRederivationPass(
                    RederivedEmailCount: 61_027,
                    UnreadableEmailCount: 2,
                    MissingContentEmailCount: 3,
                    EmailsRemain: false));
            }

            run.ReachedEndOfScope();
        }

        using (var run = Rederivation.BeginRun(Account, folderAlias: null))
        {
            using var pass = run.BeginPass();

            pass.Completed(new StoredMailRederivationPass(
                RederivedEmailCount: 0,
                UnreadableEmailCount: 0,
                MissingContentEmailCount: 0,
                EmailsRemain: true));
        }
    }

    private static void DriveProviderHealth()
    {
        ProviderHealth.RecordServed(AiProviderRole.Embedding);
        ProviderHealth.RecordUnavailable(AiProviderRole.Chat);
        ProviderHealth.RecordMisconfigured(AiProviderRole.Chat);
    }

    /// <summary>Drives every refusal shape, with the identity poisoned and the operation left as one this repository publishes.</summary>
    /// <remarks>
    /// The identity is the string a caller could influence here — a token brings its own issuer and subject — so it is
    /// a sentinel, and the contract then says it reached no dimension. The operation is not one: the port's contract is
    /// that a boundary reduces the name a request carried to a name this repository publishes before it records one,
    /// and each boundary's own suite asserts that reduction over a name a caller chose.
    /// </remarks>
    private static void DriveAuthorizationRefusals()
    {
        foreach (var surface in Enum.GetValues<ProtectedSurface>())
        {
            AuthorizationRefusals.RecordRefusal(
                surface,
                "list_emails",
                MailFathomPermission.PublishedFor(surface)[0],
                TelemetryRedactionContract.CallerSuppliedSentinel);
        }

        AuthorizationRefusals.RecordRefusal(
            ProtectedSurface.Administration,
            "/api/admin/session",
            default,
            refusedIdentity: null);
    }

    private static void DriveContactCollection()
    {
        foreach (var outcome in Enum.GetValues<ContactCollectionOutcome>())
        {
            ContactCollection.RecordOutcome(outcome);
        }
    }

    private static void DriveDerivedWorkGate()
    {
        foreach (var admission in Enum.GetValues<DerivedWorkAdmission>())
        {
            DerivedWorkGate.RecordAdmission(admission);
        }

        DerivedWorkGate.RecordDiscardedPassages(61_003);
    }

    private static void DriveEmbedding()
    {
        Embedding.RecordEmbeddedMessage(StoredEmailEmbeddingRun.Embedded(3), TimeSpan.FromSeconds(2));
        Embedding.RecordEmbeddedMessage(
            StoredEmailEmbeddingRun.SpendCeilingReached(
                2,
                inputCharacterCount: 61_007,
                Moment,
                EmbeddingSpendBound.Deployment),
            TimeSpan.FromSeconds(1));

        foreach (var failure in Enum.GetValues<EmbeddingGenerationFailure>())
        {
            Embedding.RecordEmbeddedMessage(StoredEmailEmbeddingRun.ProviderFailed(1, failure), TimeSpan.FromSeconds(1));
        }

        Embedding.RecordTruncatedEmbeddingInput(61_009);

        using (var turn = Embedding.BeginMessage())
        {
            turn.Ended(StoredEmailEmbeddingRun.Embedded(3));
        }

        using (var pass = EmbeddingBackfill.BeginPass())
        {
            pass.Ended(new EmbeddingGenerationUpkeepResult(
                new StoredEmailEmbeddingBackfillResult(
                    StoredEmailEmbeddingBackfillOutcome.SweepCompleted,
                    ChunkedEmailCount: 2,
                    EmbeddedEmailCount: 2,
                    EmbeddedChunkCount: 5,
                    CallBudgetExhaustedEmailCount: 0,
                    OwnerSpendCeilingEmailCount: 0,
                    OwnerSpendPeriodEndsAt: null,
                    OutstandingEmailCountAtSweepStart: 61_011,
                    Failure: null,
                    SpendPeriodEndsAt: null),
                EmbeddingGenerationTransition.Switched,
                RemovedSupersededVectorCount: 1));
        }

        EmbeddingBackfill.RecordPass(new EmbeddingGenerationUpkeepResult(
            new StoredEmailEmbeddingBackfillResult(
                StoredEmailEmbeddingBackfillOutcome.SweepCompleted,
                ChunkedEmailCount: 2,
                EmbeddedEmailCount: 2,
                EmbeddedChunkCount: 5,
                CallBudgetExhaustedEmailCount: 0,
                OwnerSpendCeilingEmailCount: 0,
                OwnerSpendPeriodEndsAt: null,
                OutstandingEmailCountAtSweepStart: 61_011,
                Failure: null,
                SpendPeriodEndsAt: null),
            EmbeddingGenerationTransition.None,
            RemovedSupersededVectorCount: 0));
    }

    private static void DriveJobQueue()
    {
        foreach (var outcome in Enum.GetValues<JobExecutionOutcome>())
        {
            var result = new JobExecutionResult(
                JobId.Create(Guid.CreateVersion7()),
                JobType.ClassifyEmailSpam,
                AttemptCount: 1,
                outcome,
                TimeSpan.FromSeconds(1));

            JobQueue.RecordAttempt(result);

            using var attempt = JobQueue.BeginAttempt(result.JobType, enqueuedTrace: null);
            attempt.Ended(result);
        }

        foreach (var outcome in Enum.GetValues<JobScheduleDispatchOutcome>())
        {
            JobQueue.RecordScheduleDispatch(new JobScheduleDispatch(
                JobScheduleId.Create($"mail-rules:{TelemetryRedactionContract.ConfiguredAliasSentinel}:housekeeping"),
                JobType.RunScheduledMailRules,
                outcome,
                Moment,
                SkippedOccurrenceCount: 1));
        }

        JobQueue.RecordQueueDepth([new JobQueueDepthReading(JobType.ClassifyEmailSpam, 61_013)]);
    }

    private static void DriveAnswering()
    {
        AnsweringSpend.TryAdmitRun();
        AnsweringSpend.RecordSpend(new ChatTokenUsage(InputTokens: 61_017, OutputTokens: 61_019));

        var observation = PoisonedAnsweringRun();

        using (AnsweringRun.BeginRun(observation))
        {
            // The span is published when the scope ends, which is what the contract reads.
        }

        AnsweringAudit.RecordRefusedAppend(observation, owedEntryCount: 1, new InvalidOperationException("refused"));
    }

    private static MailAnsweringRunObservation PoisonedAnsweringRun()
    {
        var observation = new MailAnsweringRunObservation(
            MailAnsweringRunId.Create(Guid.CreateVersion7(Moment)),
            MailboxScope.Create([Account], []),
            Moment);

        observation.RecordComposition(TelemetryRedactionContract.ConfiguredAliasSentinel, "0a1b2c3d4e5f");
        observation.RecordRetrieval(new MailAnsweringRetrievalReport(
            Passages: [],
            CandidateCount: 4,
            RelevantCandidateCount: 3,
            MailAnsweringRunDegradation.None));
        observation.RecordOutcome(MailAnsweringRunOutcome.Answered, [], Moment.AddSeconds(9));

        return observation;
    }

    private static void DriveMailbox()
    {
        foreach (var operation in Enum.GetValues<MailboxReadOperation>())
        {
            using var read = MailboxRead.BeginRead(operation, CancellationToken.None);
            read.Completed(3);
        }

        using (var ranking = MailboxRead.BeginSearchRanking(CancellationToken.None))
        {
            ranking.Completed(12);
        }

        foreach (var egressPoint in Enum.GetValues<SensitiveContentEgressPoint>())
        {
            using var guarded = Egress.BeginGuardedOperation(egressPoint, CancellationToken.None);
            guarded.TextGuarded();
            guarded.Completed();
        }

        ContentVolume.Report(
            Account,
            FolderAlias.Value,
            new MailboxContentVolume(
                FetchedBytes: 61_023,
                StoredBytes: 61_027,
                StoredContentBytes: 61_029,
                DeferredForStorageEmailCount: 1,
                DeferredForOwnerStorageEmailCount: 1,
                RefilledEmailCount: 1,
                StoppedForContentBudget: true));

        Convergence.Report(
            Account,
            new MailboxConvergenceReport(
                CompletedCount: 1,
                DeadLetteredCount: 1,
                DeferredCount: 1,
                FailedCount: 1,
                Outstanding:
                [
                    new MailboxMutationLifecycleCount(
                        MailboxMutation.Relocate,
                        MailboxMutationLifecycle.Pending,
                        Count: 1,
                        OldestRecordedAt: Moment.AddMinutes(-5)),
                ]));
    }

    private static void DriveDelivery()
    {
        using (var submission = Delivery.BeginSubmission(Account, OutgoingEmailId.Create(Guid.CreateVersion7())))
        {
            submission.Completed();
        }

        using (Delivery.BeginSubmission(Account, OutgoingEmailId.Create(Guid.CreateVersion7())))
        {
            // Disposed without being completed, which is the shape a submission nobody got an answer to reports.
        }

        Delivery.Report(
            Account,
            new MailOutboxPassReport(
                [
                    .. Enum.GetValues<MailOutboxDeliveryOutcome>().Select(outcome => new MailOutboxDeliveryResult(
                        OutgoingEmailId.Create(Guid.CreateVersion7()),
                        outcome,
                        MailFathomErrorCode.OutgoingEmailRefused,
                        ReplyCode: 550,
                        AttemptCount: 1)),
                ],
                [
                    .. Enum.GetValues<OutgoingMailFilingOutcome>().Select(outcome => new OutgoingMailFilingResult(
                        OutgoingEmailId.Create(Guid.CreateVersion7()),
                        OutgoingMailFiling.Sent,
                        outcome,
                        MailFathomErrorCode.OutgoingEmailFilingFailedUnexpectedly)),
                ],
                [
                    .. Enum.GetValues<MailDraftFilingOutcome>().Select(outcome => new MailDraftFilingResult(
                        MailDraftId.Create(Guid.CreateVersion7()),
                        outcome,
                        MailFathomErrorCode.OutgoingEmailFilingFailedUnexpectedly,
                        MailDraftDivergenceReason.DestinationChanged)),
                ],
                MarkedUnknownCount: 1,
                BatchFilled: true,
                OutstandingByStage:
                [
                    new OutboxStageCount(OutgoingEmailStage.Recorded, Count: 2),
                    new OutboxStageCount(OutgoingEmailStage.TransmissionBegun, Count: 1),
                ]));
    }

    private static void DriveMutations()
    {
        using (var scope = Mutation.Begin(MailboxMutation.Relocate, Account, FolderAlias, CancellationToken.None))
        {
            scope.ProtocolPathChosen(TelemetryRedactionContract.CallerSuppliedSentinel);
            scope.CommandIssued(TelemetryRedactionContract.CallerSuppliedSentinel);
            scope.Completed();
        }

        MutationAudit.RecordRefusedAppend(PoisonedAuditEntry(), new InvalidOperationException("refused"));
    }

    /// <summary>Builds an entry whose every mail-derived field is a sentinel, above all the remote folder path.</summary>
    private static MailboxMutationAuditEntry PoisonedAuditEntry() => new()
    {
        Id = MailboxMutationAuditEntryId.Create(Guid.CreateVersion7()),
        MutationRecordId = MailboxMutationRecordId.Create(Guid.CreateVersion7()),
        AccountId = Account,
        StoredEmailId = StoredEmailId.Create(Guid.CreateVersion7()),
        Mutation = MailboxMutation.Relocate,
        SourceFolderPath = RemoteFolderPath.Create(TelemetryRedactionContract.MailDerivedSentinel),
        SourceUidValidity = ImapUidValidity.Create(1),
        SourceUid = ImapUid.Create(2),
        DestinationFolderPath = RemoteFolderPath.Create(TelemetryRedactionContract.MailDerivedSentinel),
        Placement = RemoteEmailPlacement.NotReported(),
        DesiredSeenState = null,
        Requester = MailboxMutationRequester.Command(TelemetryRedactionContract.CallerSuppliedSentinel),
        RequestedAt = Moment,
        CompletedAt = Moment.AddSeconds(1),
        Outcome = MailboxMutationAuditOutcome.Performed,
        Failure = null,
    };

    private static void DriveSynchronization()
    {
        using (Synchronization.EnterRunQueue())
        {
            // The queue depth is a level rather than an event, so it is observed while something is in it.
        }

        foreach (var phase in Enum.GetValues<MailSynchronizationPhase>())
        {
            using var stage = Synchronization.BeginPhase(phase, CancellationToken.None);
            stage.Completed();
        }

        using (var cycle = Synchronization.BeginAccountRun(Account))
        {
            using (var folder = Synchronization.BeginFolderRun(Account))
            {
                folder.Synchronized(FolderAlias.Value, storedEmailCount: 2, skippedEmailCount: 1);
            }

            using (var failed = Synchronization.BeginFolderRun(Account))
            {
                failed.MailServerUnavailable(FolderAlias.Value);
            }

            using (var unresolved = Synchronization.BeginFolderRun(Account))
            {
                unresolved.AliasUnresolved(FolderAlias.Value);
            }

            cycle.Completed(scheduledFolderCount: 3, failedFolderCount: 1, convergenceFailed: false);
        }

        Synchronization.RecordScheduledDelay(Account, TimeSpan.FromSeconds(61_031), consecutiveFailureCount: 3);
        Synchronization.RecordSupervisionEnded(Account);

        ExtractionBackfill.RecordCompleted(
            new StoredEmailExtractionBackfillResult(
                ExtractedEmailCount: 2,
                UnreadableEmailCount: 1,
                MissingContentEmailCount: 1,
                OutstandingEmailCount: 61_037,
                EmailsRemain: true),
            TimeSpan.FromSeconds(2));
        ExtractionBackfill.RecordDeferred(TimeSpan.FromSeconds(1));
        ExtractionBackfill.RecordFailed(TimeSpan.FromSeconds(1));
        ExtractionBackfill.RecordInterrupted(TimeSpan.FromSeconds(1));
    }

    private static void DriveContentStore()
    {
        using (var found = StoredContent.BeginRead())
        {
            found.Found(61_039);
        }

        using (var absent = StoredContent.BeginRead())
        {
            absent.Absent();
        }

        using var write = StoredContent.BeginWrite();
        write.Stored(61_043);
    }

    /// <summary>Drives both mechanisms that reclaim an object, because the dimension telling them apart is the surface.</summary>
    private static void DriveContentObjectReclamation()
    {
        ContentObjectReclamation.RecordErased(reclaimedCount: 3, failedCount: 1);
        ContentObjectReclamation.RecordSwept(reclaimedCount: 7, reclaimedBytes: 61_051, failedCount: 2);
        ContentObjectReclamation.RecordOldestOrphanAge(TimeSpan.FromHours(37));
    }

    private static void DriveObjectStorage()
    {
        using (var listed = ObjectStorage.Begin(ObjectStorageTelemetry.ListOperationName))
        {
            listed.Succeeded();
        }

        using (var written = ObjectStorage.Begin(ObjectStorageTelemetry.PutOperationName))
        {
            written.Succeeded(61_051);
        }

        // Every classification, because each is a dimension value an exporter sees and the contract is over the whole
        // set rather than over whichever one a failure happened to produce.
        foreach (var classification in ObjectStorageFailure.All)
        {
            using var failed = ObjectStorage.Begin(ObjectStorageTelemetry.DeleteOperationName);
            failed.Failed(classification);
        }
    }

    private static void DriveSensitiveContent()
    {
        var redacted = RedactedText.Create(TelemetryRedactionContract.MailDerivedSentinel, [], omittedCharacterCount: 3);

        Derivation.RecordDerived(redacted, TimeSpan.FromMilliseconds(4));
        Derivation.RecordRefused(SensitiveContentScannerKind.Secrets);

        foreach (var egressPoint in Enum.GetValues<SensitiveContentEgressPoint>())
        {
            Egress.RecordGuarded(egressPoint, redacted, TimeSpan.FromMilliseconds(4));
            Egress.RecordRefused(egressPoint, SensitiveContentScannerKind.Pii);
            Egress.RecordStopped(
                egressPoint,
                SensitiveContentEgressRefusal.ContentFound(
                    SensitiveContentScannerKind.Secrets,
                    SensitiveContentCategory.Create("CloudKey")));
            Egress.RecordStopped(egressPoint, SensitiveContentEgressRefusal.NotFullyScanned());
        }
    }
}
