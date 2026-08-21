// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Persistence;
using MailFathom.Application.Rules.Evaluation;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Access;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.IntegrationTests.Orchestration;
using MailFathom.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Idempotency;

/// <summary>Proves the four idempotency claims against the race they are made about, rather than against a repeat.</summary>
/// <remarks>
/// <para>
/// Every claim here is asserted elsewhere by running the operation twice, in sequence or from a pair of callers the
/// scheduler is free to serialize. That establishes that the second caller recognizes what the first committed, which
/// is the half no defect has been found in. What a deployment does is reach the operation from several workers at once,
/// where none of them can see another's uncommitted write and the application's own read-before-write answers nothing —
/// so the effect is decided by a unique index, a locking clause, or a conditional update, and by nothing a substitute
/// could stand in for.
/// </para>
/// <para>
/// The four are the ones the cross-boundary invariants name: a send offered twice, an occurrence stored by two runs, a
/// job reached by two workers, and a rule pass finishing with one message. Each states its single effect as a count, so
/// a partial duplicate fails as the number of effects rather than as an exception nobody can size.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedConcurrentIdempotencyTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>The alias this class owns, so its rows are not disturbed by another class's writes.</summary>
    private const string FolderAlias = "concurrent-idempotency";

    /// <summary>
    /// How many callers race for one effect. Enough that a caller reliably loses — a pair can be serialized by the
    /// scheduler alone — and small enough that four tests of it cost the suite seconds rather than a container's worth
    /// of time.
    /// </summary>
    private const int ConcurrentWriters = 6;

    /// <summary>The identity every attempt at the one authored send is made under, which is what makes them one send.</summary>
    private const string SendRequesterIdentity = "concurrent-idempotency-send";

    /// <summary>The address the stored messages of this class were sent to.</summary>
    private const string RecipientAddress = "recipient@mailfathom.test";

    /// <summary>The UID this class's stored-occurrence race writes, from a block its other tests do not use.</summary>
    private const uint StoredOccurrenceUid = 9601;

    /// <summary>The UID of the message the rule race stamps.</summary>
    private const uint EvaluatedUid = 9602;

    /// <summary>The UID of the message the rule race must leave in the arrival queue.</summary>
    private const uint UnevaluatedUid = 9603;

    /// <summary>The UID the job this class's workers race for is enqueued about.</summary>
    private const uint LeasedJobUid = 9604;

    /// <summary>A lease long enough that nothing expires underneath a claim while the other callers are still arriving.</summary>
    private static readonly TimeSpan HeldLease = TimeSpan.FromMinutes(10);

    /// <summary>When the rule pass is recorded as having finished, which is the value a stamped row is counted by.</summary>
    private static readonly DateTimeOffset EvaluatedAt = new(2026, 6, 2, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// One authored send offered by every caller at once leaves one record. The identity index is what closes the
    /// window between the store's read and its insert, and the retry behind it is what turns the loser's refusal into
    /// the winner's record — which is the whole of what stops one authored request putting two copies of a message in
    /// somebody else's mailbox.
    /// </summary>
    [Fact]
    public async Task EnqueueAsync_ManyCallersOfferingOneAuthoredSend_RecordsItOnce()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        // Act
        var attempts = await ConcurrentIdempotency.RunAsync(
            $"{nameof(MailOutbox)}.{nameof(MailOutbox.EnqueueAsync)}",
            ConcurrentWriters,
            (_, token) => EnqueueAsync(services, token),
            cancellationToken);

        // Assert
        attempts.AssertSingleEffect(await CountRecordedSendsAsync(services, cancellationToken));

        // Every caller was answered with the record that exists, so a duplicate cannot hide behind an identifier one of
        // them was handed by an attempt that then rolled back.
        Assert.Single(attempts.Results.Select(record => record.Id).Distinct());
    }

    /// <summary>
    /// One remote occurrence stored by every run at once leaves one row and one search document. The repository
    /// deduplicates what it can see, and none of these runs can see another's staged insert, so the unique index is the
    /// only thing refusing the duplicates — and a run that loses that race fails rather than storing a second row.
    /// </summary>
    [Fact]
    public async Task UpsertMetadataAsync_ManyRunsStoringOneRemoteOccurrence_LeavesOneRow()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, StoredOccurrenceUid);

        // Act
        var attempts = await ConcurrentIdempotency.RunAsync(
            $"{nameof(IEmailMetadataRepository)}.{nameof(IEmailMetadataRepository.UpsertMetadataAsync)}",
            ConcurrentWriters,
            (ordinal, token) => StoreAsync(services, occurrenceId, $"concurrent-store-{ordinal}", token),
            cancellationToken);

        // Assert
        attempts.AssertSingleEffect(await CountOccurrenceRowsAsync(services, occurrenceId, cancellationToken));
        Assert.Contains(PersistenceCommitResult.Committed, attempts.Results);
        Assert.Equal(1, await CountSearchDocumentsAsync(services, occurrenceId, cancellationToken));
    }

    /// <summary>
    /// One due job reached by every worker at once is leased to one of them. The claim selects and stamps in a single
    /// statement under <c>FOR UPDATE SKIP LOCKED</c>, so the workers that lose take nothing rather than waiting for the
    /// winner's transaction and then taking the row it already holds — which is one job run six times.
    /// </summary>
    [Fact]
    public async Task ClaimAsync_ManyWorkersReachingOneDueJob_LeasesItToOneOfThem()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await DrainClaimableJobsAsync(services, cancellationToken);
        var enqueued = await EnqueueOneJobAsync(services, cancellationToken);

        // Act
        var attempts = await ConcurrentIdempotency.RunAsync(
            $"{nameof(IJobStore)}.{nameof(IJobStore.ClaimAsync)}",
            ConcurrentWriters,
            (_, token) => services.InScopeAsync(ClaimOneAsync, token),
            cancellationToken);

        // Assert
        var leased = attempts.Results.SelectMany(claim => claim).ToArray();
        attempts.AssertSingleEffect(leased.Length);
        Assert.Equal(enqueued, Assert.Single(leased).JobId);
    }

    /// <summary>
    /// One message finished with by every pass at once carries one stamp, and the message beside it stays in the
    /// arrival queue. The stamp is a set-based update over the identities the pass names, so a condition that widened
    /// under concurrent writers would take mail out of the queue that no rule was ever evaluated against — which is
    /// mail a rule silently never applies to.
    /// </summary>
    [Fact]
    public async Task RecordEvaluatedAsync_ManyPassesFinishingWithOneEmail_StampsThatEmailAlone()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var evaluated = await StoreOneMessageAsync(services, binding, EvaluatedUid, cancellationToken);
        var stillQueued = await StoreOneMessageAsync(services, binding, UnevaluatedUid, cancellationToken);

        // Act
        var attempts = await ConcurrentIdempotency.RunAsync(
            $"{nameof(IMailRuleEvaluationStore)}.{nameof(IMailRuleEvaluationStore.RecordEvaluatedAsync)}",
            ConcurrentWriters,
            (_, token) => RecordEvaluatedAsync(services, evaluated, token),
            cancellationToken);

        // Assert
        attempts.AssertSingleEffect(await CountStampedMessagesAsync(services, cancellationToken));

        // The control. The same count would have reported the second message had the update reached past the one
        // identity it was given, so what is asserted above is an observation rather than an assumption.
        Assert.Null(await ReadStampAsync(services, stillQueued, cancellationToken));
    }

    /// <summary>Offers the one authored send this class races for, under the identity that makes every offer the same send.</summary>
    private static async Task<OutgoingEmailRecord> EnqueueAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, "anna@example.test", out var recipient));

        var request = OutgoingEmailRequest.Create(
            SyntheticMailAccount.AccountId,
            OutgoingEmailRequester.Command(SendRequesterIdentity),
            [OutgoingRecipient.Create(recipient, OutgoingRecipientRole.To, contact: null)]);

        var mime = Encoding.ASCII.GetBytes(
            $"Message-ID: <{SendRequesterIdentity}@example.test>\r\nSubject: {SendRequesterIdentity}\r\n\r\nSynthetic body.\r\n");

        var opened = await services.AsCallerInScopeAsync(
            (scope, token) => scope.GetRequiredService<MailOutbox>().EnqueueAsync(request, mime.AsMemory(), token),
            [MailFathomPermission.MailSend],
            cancellationToken);

        return opened.Record;
    }

    private static Task<int> CountRecordedSendsAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().OutgoingEmails
                .AsNoTracking()
                .CountAsync(
                    message => message.MailboxAccountId == SyntheticMailAccount.AccountId.Value
                        && message.RequesterIdentity == SendRequesterIdentity,
                    token),
            cancellationToken);

    private static Task<PersistenceCommitResult> StoreAsync(
        OrchestratedMailFathomServices services,
        EmailOccurrenceId occurrenceId,
        string subject,
        CancellationToken cancellationToken) => services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IEmailMetadataRepository>().UpsertMetadataAsync(
                session,
                SyntheticEmail.RemoteMetadataOf(occurrenceId, subject),
                extractedMetadata: null,
                StoredEmailContentAvailability.ExceededSizeLimit,
                token),
            cancellationToken);

    /// <summary>Counts every row naming one occurrence, so a duplicate is reported as a number rather than assumed away.</summary>
    private static Task<int> CountOccurrenceRowsAsync(
        OrchestratedMailFathomServices services,
        EmailOccurrenceId occurrenceId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => OccurrenceRowsOf(scope, occurrenceId).CountAsync(token),
            cancellationToken);

    private static Task<int> CountSearchDocumentsAsync(
        OrchestratedMailFathomServices services,
        EmailOccurrenceId occurrenceId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                var storedEmailIds = await OccurrenceRowsOf(scope, occurrenceId)
                    .Select(storedEmail => storedEmail.Id)
                    .ToArrayAsync(token);

                return await scope.GetRequiredService<MailFathomDbContext>().EmailSearchDocuments
                    .AsNoTracking()
                    .CountAsync(document => storedEmailIds.Contains(document.StoredEmailId), token);
            },
            cancellationToken);

    private static IQueryable<StoredEmailEntity> OccurrenceRowsOf(
        IServiceProvider scope,
        EmailOccurrenceId occurrenceId)
    {
        var alias = occurrenceId.FolderResolutionId.Alias.Value;
        var generation = occurrenceId.FolderResolutionId.Generation.Value;
        var uidValidity = occurrenceId.UidValidity.Value;
        var uid = occurrenceId.Uid.Value;

        return scope.GetRequiredService<MailFathomDbContext>().StoredEmails
            .AsNoTracking()
            .Where(storedEmail => storedEmail.MailFolder.MailboxAccountId == SyntheticMailAccount.AccountId.Value
                && storedEmail.MailFolder.Alias == alias
                && storedEmail.MailFolder.ResolutionGeneration == generation
                && storedEmail.UidValidity == uidValidity
                && storedEmail.Uid == uid);
    }

    /// <summary>Takes everything claimable and completes it, so the race below is run against one due job and no other.</summary>
    private static async Task DrainClaimableJobsAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var claimed = await services.InScopeAsync(
                (scope, token) => ClaimAsync(scope, batchSize: 100, token),
                cancellationToken);

            if (claimed.Count == 0)
            {
                return;
            }

            foreach (var job in claimed)
            {
                await services.InScopeAsync(
                    (scope, token) => scope.GetRequiredService<IJobStore>()
                        .CompleteAsync(job.JobId, job.Lease.Owner, token),
                    cancellationToken);
            }
        }
    }

    private static async Task<JobId> EnqueueOneJobAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken)
    {
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var payload = ClassifyEmailSpamJobPayload.For(SyntheticEmail.OccurrenceIn(binding, LeasedJobUid));
        var request = JobEnqueueRequest.Create(
            JobIdempotencyKey.Create($"{FolderAlias}/{LeasedJobUid}"),
            payload,
            SyntheticMailAccount.AccountId);

        var enqueued = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IJobStore>().EnqueueAsync(request, token),
            cancellationToken);

        // Asserted rather than assumed: a refused enqueue names no job, and every assertion below would then be made
        // about a queue this test never filled.
        Assert.Equal(JobEnqueueOutcome.Created, enqueued.Outcome);

        return enqueued.JobId!.Value;
    }

    private static Task<IReadOnlyList<LeasedJob>> ClaimOneAsync(
        IServiceProvider scope,
        CancellationToken cancellationToken) => ClaimAsync(scope, batchSize: 1, cancellationToken);

    private static Task<IReadOnlyList<LeasedJob>> ClaimAsync(
        IServiceProvider scope,
        int batchSize,
        CancellationToken cancellationToken) => scope.GetRequiredService<IJobStore>().ClaimAsync(
            JobClaimRequest.Create(
                [JobType.ClassifyEmailSpam],
                batchSize,
                HeldLease,
                JobLeaseOwner.NewAttempt()),
            cancellationToken);

    /// <summary>
    /// Stores one message of the shape an account run leaves: extracted, so a message this class stamps and abandons
    /// is one the rest of the suite already carries rather than a shape only this harness produces.
    /// </summary>
    private static async Task<StoredEmailId> StoreOneMessageAsync(
        OrchestratedMailFathomServices services,
        MailFolderResolution binding,
        uint uid,
        CancellationToken cancellationToken)
    {
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, uid);
        var subject = $"concurrent-idempotency-{uid}";
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
                        SyntheticEmail.BodyTextContaining(subject, wordCount: 20),
                        RecipientAddress),
                    StoredEmailContentAvailability.Available,
                    token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        return storedEmailId;
    }

    private static Task<PersistenceCommitResult> RecordEvaluatedAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IMailRuleEvaluationStore>()
                .RecordEvaluatedAsync(session, [storedEmailId], EvaluatedAt, token),
            cancellationToken);

    /// <summary>Counts this class's messages that carry the stamp, which the message left queued would be reported by.</summary>
    private static Task<int> CountStampedMessagesAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().StoredEmails
                .AsNoTracking()
                .CountAsync(
                    storedEmail => storedEmail.MailFolder.MailboxAccountId == SyntheticMailAccount.AccountId.Value
                        && storedEmail.MailFolder.Alias == FolderAlias
                        && storedEmail.RulesEvaluatedAt == EvaluatedAt,
                    token),
            cancellationToken);

    private static Task<DateTimeOffset?> ReadStampAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken)
    {
        var identity = storedEmailId.Value;

        return services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().StoredEmails
                .AsNoTracking()
                .Where(storedEmail => storedEmail.Id == identity)
                .Select(storedEmail => storedEmail.RulesEvaluatedAt)
                .SingleAsync(token),
            cancellationToken);
    }
}
