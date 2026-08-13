// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Evaluation;
using MailFathom.Application.Rules.History;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>
/// Proves the two state stores a rule pass runs on against a real schema: the arrival queue that shrinks as evaluations
/// are recorded, the keyset walk over a whole mailbox that resumes past a committed position, and the one run row an
/// account may have outstanding.
/// </summary>
/// <remarks>
/// None of it is reachable without a real server. The queue is a partial index over a nullable timestamp, the two walks
/// order and compare <c>uuid</c> values under PostgreSQL's collation rather than the CLR's, the evaluations are written
/// as one <c>UPDATE</c> over a batch instead of through tracked entities, and the run's revision lives in a fixed-length
/// character column that would pad a shorter value. A substitute for the database would report whatever the test told it
/// to, whatever the translation actually produced.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedMailRuleEvaluationTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderAlias = "rule-evaluation";

    /// <summary>The folder whose synchronization is switched off after it has stored mail, which is what retention is.</summary>
    private const string ParkedFolderAlias = "rule-evaluation-parked";

    private const string RecipientAddress = "recipient@mailfathom.test";

    /// <summary>How much of a walk one read takes, small enough that paging is what the test observes.</summary>
    private const int BatchSize = 2;

    /// <summary>How much of the queue one drain reads, large enough that arrangement costs a query or two.</summary>
    private const int DrainBatchSize = 200;

    /// <summary>Bounds every paging loop. A walk that has not ended by then is a defect rather than a slow database.</summary>
    private const int MaximumBatches = 200;

    private static readonly DateTimeOffset EvaluatedAt = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset RequestedAt = new(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset EndedAt = new(2026, 6, 1, 11, 0, 0, TimeSpan.Zero);

    /// <summary>An identity of the shape the compiler derives, restored rather than computed so the column is what is under test.</summary>
    private static readonly MailRuleSetRevision Revision = MailRuleSetRevision.Restore("0a1b2c3d4e5f");

    /// <summary>
    /// The whole arrival path over a real schema: mail nobody has evaluated is what the queue holds, the fact surface
    /// comes back projected from the row rather than loaded from it, and recording an evaluation is what takes an email
    /// out — which is what makes a rule apply to mail arriving from now on rather than to a mailbox's history.
    /// </summary>
    [Fact]
    public async Task TheArrivalQueue_MailNoPassHasEvaluated_HoldsItUntilTheEvaluationsAreRecorded()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await DrainArrivalQueueAsync(services, cancellationToken);
        var evaluated = await StoreOneMessageAsync(services, uid: 9401, cancellationToken);
        var awaiting = await StoreOneMessageAsync(services, uid: 9402, cancellationToken);

        // Act
        var queued = await ReadArrivalQueueAsync(services, resumeAfter: null, DrainBatchSize, cancellationToken);
        var bodyText = await ReadExtractedBodyTextAsync(services, evaluated, cancellationToken);
        var recorded = await RecordEvaluatedAsync(services, [evaluated], cancellationToken);
        var afterRecording = await ReadArrivalQueueAsync(services, resumeAfter: null, DrainBatchSize, cancellationToken);

        // Assert
        Assert.Equal(
            new HashSet<StoredEmailId>([evaluated, awaiting]),
            queued.Select(candidate => candidate.StoredEmailId).ToHashSet());

        var arrival = Assert.Single(queued, candidate => candidate.StoredEmailId == evaluated);
        Assert.Equal(SyntheticMailAccount.AccountId.Value, arrival.Facts.Account);

        // The alias in its normalized form, which is the one thing a condition compares against: MailFolderAlias
        // upper-cases what configuration wrote, and the fact surface publishes what the row holds rather than what a
        // test typed.
        Assert.Equal(MailFolderAlias.Create(FolderAlias).Value, arrival.Facts.Folder);
        // Both addresses for the same reason as the alias: EmailAddress upper-cases what a header wrote, and the fact
        // surface publishes the comparison form a condition is matched against rather than the spelling of the message.
        Assert.Equal(NormalizedAddress(SyntheticEmail.DefaultSenderAddress), arrival.Facts.SenderAddress);
        Assert.Contains(NormalizedAddress(RecipientAddress), arrival.Facts.RecipientAddresses);
        Assert.Equal(SyntheticEmail.ReceivedAt, arrival.Facts.ReceivedAt);
        Assert.True(arrival.Facts.HasExtractedContent);

        // Text was extracted, so nothing about this message is still expected and a body-text condition reads it now.
        Assert.False(arrival.AwaitsExtraction);
        Assert.NotNull(bodyText);
        Assert.Contains(SubjectOf(9401), bodyText, StringComparison.Ordinal);

        Assert.Equal(PersistenceCommitResult.Committed, recorded);
        Assert.Equal(awaiting, Assert.Single(afterRecording).StoredEmailId);
    }

    /// <summary>
    /// The three ways an email can be sitting in the queue without extracted text, which the projection has to tell
    /// apart: content the ceiling has not had headroom for is still coming, content above the size limit never is, and
    /// content that was fetched whole and could not be parsed never is either.
    /// </summary>
    /// <remarks>
    /// The distinction decides whether a message is waited for or evaluated with the body-text fact absent, and getting
    /// it wrong is silent — the email is stamped as evaluated, leaves the queue for good, and a rule naming its text
    /// never sees it. It is a <c>CASE</c> PostgreSQL evaluates over a string-converted enum column beside a correlated
    /// existence check, so only a real database settles what it produces. The third message is the one no column
    /// separates from the first two: its payload was stored, so its availability says <c>Available</c>, and what says
    /// nothing was read out of it is the source recorded on the document its envelope alone produced.
    /// </remarks>
    [Fact]
    public async Task TheArrivalQueue_MailWhoseTextWasNeverExtracted_WaitsOnlyForContentThatIsStillComing()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await DrainArrivalQueueAsync(services, cancellationToken);
        var headroomPending = await StoreOneUnextractedMessageAsync(
            services,
            uid: 9431,
            StoredEmailContentAvailability.AwaitingStorageHeadroom,
            cancellationToken);
        var oversized = await StoreOneUnextractedMessageAsync(
            services,
            uid: 9432,
            StoredEmailContentAvailability.ExceededSizeLimit,
            cancellationToken);

        // The payload was fetched whole and nothing could be read out of its MIME, which is what a synchronization run
        // stores when parsing fails inside the limits. Nothing about the row says so except the document its envelope
        // produced, and no later pass reads that MIME differently — so it is evaluated now rather than waited on.
        var unparseable = await StoreOneUnextractedMessageAsync(
            services,
            uid: 9433,
            StoredEmailContentAvailability.Available,
            cancellationToken);

        // Act
        var queued = await ReadArrivalQueueAsync(services, resumeAfter: null, DrainBatchSize, cancellationToken);

        // Assert
        var waited = Assert.Single(queued, candidate => candidate.StoredEmailId == headroomPending);
        var evaluatedWithoutText = Assert.Single(queued, candidate => candidate.StoredEmailId == oversized);
        var evaluatedUnparseable = Assert.Single(queued, candidate => candidate.StoredEmailId == unparseable);

        Assert.False(waited.Facts.HasExtractedContent);
        Assert.False(evaluatedWithoutText.Facts.HasExtractedContent);

        // The one a document's existence cannot answer: this message has one, so reading the row rather than the source
        // recorded on it would report text that was never derived.
        Assert.False(evaluatedUnparseable.Facts.HasExtractedContent);

        // The control the assertions below need: all three rows look identical to a reader of the search document, so
        // an observation that reported nothing would pass the assertions above and fail the first of these.
        Assert.True(waited.AwaitsExtraction);
        Assert.False(evaluatedWithoutText.AwaitsExtraction);
        Assert.False(evaluatedUnparseable.AwaitsExtraction);
    }

    /// <summary>
    /// The whole-mailbox walk against a real schema: it selects mail a pass has already evaluated, it never hands the
    /// same email to two batches, and the position one batch commits is exactly what the next resumes past.
    /// </summary>
    /// <remarks>
    /// The order is PostgreSQL's over <c>uuid</c>, which is not the order the CLR compares two <see cref="Guid" /> values
    /// in, so the assertions compare the walk against itself rather than against an order stated here.
    /// </remarks>
    [Fact]
    public async Task TheWholeMailboxWalk_APositionABatchCommitted_ResumesPastItOverMailAlreadyEvaluated()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var seeded = new List<StoredEmailId>();
        foreach (var uid in (uint[])[9411, 9412, 9413])
        {
            seeded.Add(await StoreOneMessageAsync(services, uid, cancellationToken));
        }

        // Nothing is left in the arrival queue, so what the walk finds cannot be the queue under another name.
        await DrainArrivalQueueAsync(services, cancellationToken);
        var emptyQueue = await ReadArrivalQueueAsync(services, resumeAfter: null, DrainBatchSize, cancellationToken);

        // Act
        var walked = await WalkWholeMailboxAsync(services, cancellationToken);
        var resumed = await ReadWholeMailboxAsync(services, walked[0], walked.Count, cancellationToken);

        // Assert
        Assert.Empty(emptyQueue);
        Assert.Equal(walked.Count, walked.Distinct().Count());
        Assert.All(seeded, storedEmailId => Assert.Contains(storedEmailId, walked));
        Assert.Equal(walked.Skip(1), resumed.Select(candidate => candidate.StoredEmailId));
    }

    /// <summary>
    /// The one run row an account may have outstanding, through the store that owns it: a request is found afterwards,
    /// the progress a batch commits replaces it rather than appending a second row, and an ended run is outstanding no
    /// longer.
    /// </summary>
    /// <remarks>
    /// The revision is the part only a real column settles. It is stored in a fixed-length character type, which pads a
    /// shorter value with spaces on the way out, so a round trip that compares equal is the evidence that a run reads
    /// back bound to the rule set it was bound to rather than to a value nothing matches.
    /// </remarks>
    [Fact]
    public async Task TheOutstandingRun_ARequestCarriedAndThenEnded_ReadsBackAsOneRowPerAccount()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var position = await StoreOneMessageAsync(services, uid: 9421, cancellationToken);
        await EndAnyOutstandingRunAsync(services, cancellationToken);
        var request = new MailRuleEvaluationRun
        {
            AccountId = SyntheticMailAccount.AccountId,
            RequestedAt = RequestedAt,
            Trigger = MailRuleExecutionTrigger.ScheduledRun,
        };

        // Act
        var requested = await SaveRunAsync(services, request, cancellationToken);
        var afterRequest = await FindOutstandingRunAsync(services, cancellationToken);
        var carried = await SaveRunAsync(
            services,
            afterRequest! with
            {
                Revision = Revision,
                Position = position,
                EvaluatedEmailCount = 2,
                MatchedEmailCount = 1,
                SkippedEmailCount = 1,
            },
            cancellationToken);
        var afterProgress = await FindOutstandingRunAsync(services, cancellationToken);
        var ended = await SaveRunAsync(
            services,
            afterProgress! with { EndedAt = EndedAt, Ending = MailRuleEvaluationRunEnding.Completed },
            cancellationToken);
        var afterEnding = await FindOutstandingRunAsync(services, cancellationToken);

        // Assert
        Assert.Equal(PersistenceCommitResult.Committed, requested);
        Assert.Equal(PersistenceCommitResult.Committed, carried);
        Assert.Equal(PersistenceCommitResult.Committed, ended);

        Assert.NotNull(afterRequest);
        Assert.Equal(RequestedAt, afterRequest.RequestedAt);
        Assert.Equal(MailRuleExecutionTrigger.ScheduledRun, afterRequest.Trigger);
        Assert.False(afterRequest.Revision.IsSpecified);
        Assert.Null(afterRequest.Position);
        Assert.True(afterRequest.IsOutstanding);

        Assert.NotNull(afterProgress);
        Assert.Equal(MailRuleExecutionTrigger.ScheduledRun, afterProgress.Trigger);
        Assert.Equal(Revision, afterProgress.Revision);
        Assert.Equal(position, afterProgress.Position);
        Assert.Equal(2, afterProgress.EvaluatedEmailCount);
        Assert.Equal(1, afterProgress.MatchedEmailCount);
        Assert.Equal(1, afterProgress.SkippedEmailCount);
        Assert.True(afterProgress.IsOutstanding);

        Assert.Null(afterEnding);
    }

    /// <summary>
    /// A start decides from the row it is about to write over, so a run already in front of the account is answered with
    /// rather than replaced — and the account keeps the walk it had, with the position and counts that walk reached.
    /// </summary>
    /// <remarks>
    /// Only a real database settles this. The decision is a read the write's own session performs against a row another
    /// transaction has already committed, and the version token EF Core puts in the <c>UPDATE</c> catches a row that
    /// changed after that read rather than a decision taken before it. A substitute would report whatever the test told
    /// it about a row that never existed.
    /// </remarks>
    [Fact]
    public async Task TheOutstandingRun_AScheduledStartMeetingARunAlreadyCommitted_LeavesThatRunAndItsProgressAlone()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await EndAnyOutstandingRunAsync(services, cancellationToken);

        // Act
        var claimedByTheRequest = await TryStartRunAsync(
            services,
            new MailRuleEvaluationRun
            {
                AccountId = SyntheticMailAccount.AccountId,
                RequestedAt = RequestedAt,
                Trigger = MailRuleExecutionTrigger.RequestedRun,
                EvaluatedEmailCount = 7,
            },
            cancellationToken);
        var claimedByTheSchedule = await TryStartRunAsync(
            services,
            new MailRuleEvaluationRun
            {
                AccountId = SyntheticMailAccount.AccountId,
                RequestedAt = RequestedAt.AddMinutes(1),
                Trigger = MailRuleExecutionTrigger.ScheduledRun,
            },
            cancellationToken);
        var outstanding = await FindOutstandingRunAsync(services, cancellationToken);

        // Assert
        Assert.Null(claimedByTheRequest);

        Assert.NotNull(claimedByTheSchedule);
        Assert.Equal(MailRuleExecutionTrigger.RequestedRun, claimedByTheSchedule.Trigger);
        Assert.Equal(RequestedAt, claimedByTheSchedule.RequestedAt);

        Assert.NotNull(outstanding);
        Assert.Equal(MailRuleExecutionTrigger.RequestedRun, outstanding.Trigger);
        Assert.Equal(RequestedAt, outstanding.RequestedAt);
        Assert.Equal(7, outstanding.EvaluatedEmailCount);
    }

    /// <summary>
    /// Mail a folder kept after its synchronization was switched off is out of both walks, and out of them because the
    /// database left it out rather than because the rows are gone: the same mail is stored first under a deployment
    /// that mirrors the folder, and a message in a folder that stayed mirrored comes back from both walks beside it.
    /// </summary>
    /// <remarks>
    /// The exclusion is a clause PostgreSQL evaluates over an account and an alias together, so only a real database
    /// settles whether it narrows anything. It matters most to the arrival queue: a message the walk returned and the
    /// pass then declined to evaluate would sit at the head of that queue for the rest of the deployment's life, since
    /// recording an evaluation is the only thing that takes an email out of it.
    /// </remarks>
    [Fact]
    public async Task BothWalks_MailAFolderKeptAfterItStoppedBeingMirrored_LeaveItOutAndKeepReadingTheRest()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        StoredEmailId parked;
        StoredEmailId mirrored;

        await using (var whileMirrored =
            await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken))
        {
            // Numbers of this test's own, because a UID names an occurrence: repeating one another test stored in the
            // same folder would upsert that row rather than write one, and the message would arrive already carrying
            // whatever that test left on it — an evaluation stamp above all, which takes it out of the arrival queue.
            parked = await StoreOneMessageAsync(whileMirrored, uid: 9451, cancellationToken, ParkedFolderAlias);
            mirrored = await StoreOneMessageAsync(whileMirrored, uid: 9452, cancellationToken);
        }

        await using var services = await OrchestratedMailFathomServices.StartAsync(
            orchestration,
            cancellationToken,
            foldersNotMirrored:
            [
                new MailFolderIdentity(SyntheticMailAccount.AccountId, MailFolderAlias.Create(ParkedFolderAlias)),
            ]);

        // Act
        var queued = await ReadArrivalQueueAsync(services, resumeAfter: null, DrainBatchSize, cancellationToken);
        var walked = await WalkWholeMailboxAsync(services, cancellationToken);

        // Assert
        var queuedIds = queued.Select(candidate => candidate.StoredEmailId).ToArray();
        Assert.Contains(mirrored, queuedIds);
        Assert.DoesNotContain(parked, queuedIds);
        Assert.Contains(mirrored, walked);
        Assert.DoesNotContain(parked, walked);
    }

    private static string SubjectOf(uint uid) => $"{FolderAlias}-{uid}";

    /// <summary>Normalizes an address the way extraction does, which is the form a stored row holds.</summary>
    /// <remarks>
    /// Asked of the domain type rather than upper-cased here, so that a test states the rule's own answer instead of a
    /// second copy of it that would survive the rule changing.
    /// </remarks>
    private static string NormalizedAddress(string address)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, address, out var emailAddress));

        return emailAddress.NormalizedAddress;
    }

    /// <summary>Stores one synthetic message, whose extraction the same session derives its search document from.</summary>
    private static async Task<StoredEmailId> StoreOneMessageAsync(
        OrchestratedMailFathomServices services,
        uint uid,
        CancellationToken cancellationToken,
        string folderAlias = FolderAlias)
    {
        var binding = await OrchestratedFolderBinding.CommitAsync(services, folderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, uid);
        var subject = SubjectOf(uid);
        var storedEmailId = default(StoredEmailId);

        var commitResult = await services.CommitAsync(
            async (scope, session, token) => storedEmailId = await scope
                .GetRequiredService<IEmailMetadataRepository>()
                .UpsertMetadataAsync(
                    session,
                    SyntheticEmail.RemoteMetadataOf(occurrenceId, subject),
                    SyntheticEmail.ExtractionOf(
                        occurrenceId,
                        subject,
                        SyntheticEmail.BodyTextContaining(subject, wordCount: 40),
                        RecipientAddress),
                    StoredEmailContentAvailability.Available,
                    token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        return storedEmailId;
    }

    /// <summary>Stores one synthetic message whose MIME nothing read, under the content state a test names.</summary>
    private static async Task<StoredEmailId> StoreOneUnextractedMessageAsync(
        OrchestratedMailFathomServices services,
        uint uid,
        StoredEmailContentAvailability contentAvailability,
        CancellationToken cancellationToken)
    {
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, uid);
        var storedEmailId = default(StoredEmailId);

        var commitResult = await services.CommitAsync(
            async (scope, session, token) => storedEmailId = await scope
                .GetRequiredService<IEmailMetadataRepository>()
                .UpsertMetadataAsync(
                    session,
                    SyntheticEmail.RemoteMetadataOf(occurrenceId, SubjectOf(uid)),
                    extractedMetadata: null,
                    contentAvailability,
                    token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        return storedEmailId;
    }

    /// <summary>
    /// Records an evaluation for everything the arrival queue holds, so a test observes its own mail rather than
    /// whatever an earlier class in this collection left behind.
    /// </summary>
    private static async Task DrainArrivalQueueAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<StoredEmailAwaitingRuleEvaluation> queued;
        var batch = 0;

        do
        {
            queued = await ReadArrivalQueueAsync(services, resumeAfter: null, DrainBatchSize, cancellationToken);

            if (queued.Count > 0)
            {
                var commitResult = await RecordEvaluatedAsync(
                    services,
                    [.. queued.Select(candidate => candidate.StoredEmailId)],
                    cancellationToken);

                Assert.Equal(PersistenceCommitResult.Committed, commitResult);
            }

            batch++;
        }
        while (queued.Count > 0 && batch < MaximumBatches);

        Assert.Empty(queued);
    }

    /// <summary>
    /// Neither walk reaches mail stored under an alias no mapping names, so a folder an operator withdrew is not mail
    /// a rule may move, file, or flag.
    /// </summary>
    /// <remarks>
    /// Both walks are asserted because they select different mail — one the arrival queue, the other the whole mailbox
    /// — and a narrowing applied to only one of them would leave a requested run acting on exactly the folder the
    /// account run left alone. The mapped message beside it is the control the two absences need.
    /// </remarks>
    [Fact]
    public async Task BothWalks_MailInAFolderNoMappingNames_LeaveItOutWhileItStaysStored()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await DrainArrivalQueueAsync(services, cancellationToken);
        var unmapped = await StoreOneMessageAsync(
            services,
            uid: 9441,
            cancellationToken,
            SyntheticMailAccount.UnmappedFolderAlias);
        var mapped = await StoreOneMessageAsync(services, uid: 9442, cancellationToken);

        // Act
        var queued = await ReadArrivalQueueAsync(services, resumeAfter: null, DrainBatchSize, cancellationToken);
        var walked = await WalkWholeMailboxAsync(services, cancellationToken);

        // Assert
        Assert.DoesNotContain(unmapped, queued.Select(candidate => candidate.StoredEmailId));
        Assert.DoesNotContain(unmapped, walked);
        Assert.Contains(mapped, queued.Select(candidate => candidate.StoredEmailId));
        Assert.Contains(mapped, walked);
    }

    /// <summary>Pages the whole-mailbox walk to its end, the way a requested run does across account runs.</summary>
    private static async Task<IReadOnlyList<StoredEmailId>> WalkWholeMailboxAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken)
    {
        var walked = new List<StoredEmailId>();
        StoredEmailId? position = null;
        IReadOnlyList<StoredEmailAwaitingRuleEvaluation> candidates;
        var batch = 0;

        do
        {
            candidates = await ReadWholeMailboxAsync(services, position, BatchSize, cancellationToken);
            walked.AddRange(candidates.Select(candidate => candidate.StoredEmailId));
            position = candidates.Count == 0 ? position : candidates[^1].StoredEmailId;
            batch++;
        }
        while (candidates.Count > 0 && batch < MaximumBatches);

        Assert.Empty(candidates);

        return walked;
    }

    private static Task<IReadOnlyList<StoredEmailAwaitingRuleEvaluation>> ReadArrivalQueueAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId? resumeAfter,
        int batchSize,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IMailRuleEvaluationStore>()
                .GetEmailsAwaitingFirstEvaluationAsync(
                    SyntheticMailAccount.AccountId,
                    resumeAfter,
                    batchSize,
                    token),
            cancellationToken);

    private static Task<IReadOnlyList<StoredEmailAwaitingRuleEvaluation>> ReadWholeMailboxAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId? resumeAfter,
        int batchSize,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IMailRuleEvaluationStore>()
                .GetStoredEmailsAsync(SyntheticMailAccount.AccountId, resumeAfter, batchSize, token),
            cancellationToken);

    private static Task<string?> ReadExtractedBodyTextAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IMailRuleEvaluationStore>()
                .ReadExtractedBodyTextAsync(storedEmailId, token),
            cancellationToken);

    private static Task<PersistenceCommitResult> RecordEvaluatedAsync(
        OrchestratedMailFathomServices services,
        IReadOnlyList<StoredEmailId> storedEmailIds,
        CancellationToken cancellationToken) => services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IMailRuleEvaluationStore>()
                .RecordEvaluatedAsync(session, storedEmailIds, EvaluatedAt, token),
            cancellationToken);

    private static Task<MailRuleEvaluationRun?> FindOutstandingRunAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IMailRuleEvaluationRunStore>()
                .FindOutstandingAsync(SyntheticMailAccount.AccountId, token),
            cancellationToken);

    private static Task<PersistenceCommitResult> SaveRunAsync(
        OrchestratedMailFathomServices services,
        MailRuleEvaluationRun run,
        CancellationToken cancellationToken) => services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IMailRuleEvaluationRunStore>()
                .SaveAsync(session, run, token),
            cancellationToken);

    /// <summary>Starts a run the way a request does, and reports the run the account already had where it has one.</summary>
    private static async Task<MailRuleEvaluationRun?> TryStartRunAsync(
        OrchestratedMailFathomServices services,
        MailRuleEvaluationRun run,
        CancellationToken cancellationToken)
    {
        MailRuleEvaluationRun? claimed = null;
        var commitResult = await services.CommitAsync(
            async (scope, session, token) => claimed = await scope
                .GetRequiredService<IMailRuleEvaluationRunStore>()
                .TryStartAsync(session, run, token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        return claimed;
    }

    /// <summary>Ends whatever run the account carries, so a test arranges the request it is about to make.</summary>
    private static async Task EndAnyOutstandingRunAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken)
    {
        var outstanding = await FindOutstandingRunAsync(services, cancellationToken);

        if (outstanding is null)
        {
            return;
        }

        var commitResult = await SaveRunAsync(
            services,
            outstanding with { EndedAt = EndedAt, Ending = MailRuleEvaluationRunEnding.Completed },
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);
    }
}
