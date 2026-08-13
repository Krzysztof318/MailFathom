// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves the two guarantees the job store gets from PostgreSQL rather than from its own code.</summary>
/// <remarks>
/// <para>
/// Both are structural and neither is reachable from a unit test. Enqueuing is idempotent because a unique index
/// refuses the second insert, so two callers racing for one execution produce one job; and a claim is exclusive because
/// one statement selects and stamps under <c>FOR UPDATE SKIP LOCKED</c>, so two workers claiming at the same moment
/// take different jobs instead of waiting on each other. A conflict target naming the wrong columns, a locking clause
/// lost in an edit, or a compare-and-set that stopped comparing would all pass a unit test and would reach an operator
/// as work done twice.
/// </para>
/// <para>
/// The suite shares one database and nothing else in it enqueues, so each test drains the queue before it acts. Without
/// that, a claim — which takes the oldest due row of a type rather than a row a test names — would return whatever an
/// earlier test left behind.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedJobStoreTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>The alias this class binds, so the account row its jobs point at exists without disturbing another test's folder.</summary>
    private const string FolderAlias = "job-store";

    /// <summary>A lease long enough that nothing in a test expires underneath it.</summary>
    private static readonly TimeSpan HeldLease = TimeSpan.FromMinutes(10);

    /// <summary>A lease that has run out by the time the next statement reaches the database, which is how a crash looks.</summary>
    private static readonly TimeSpan ExpiredLease = TimeSpan.FromMilliseconds(1);

    /// <summary>
    /// Two enqueues of one execution, dispatched together from separate scopes, and the queue holds one job. Only the
    /// unique index closes the window between reading and writing, so this is what separates the constraint that exists
    /// from a check the application could have made instead.
    /// </summary>
    [Fact]
    public async Task EnqueueAsync_TwoConcurrentCallersAskingForOneExecution_CreatesOneJobAndAnswersBoth()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await DrainAsync(services, cancellationToken);
        var request = await RequestAsync(services, uid: 101, cancellationToken);

        // Act
        var results = await services.InTwoScopesAsync(
            (firstScope, secondScope, token) => Task.WhenAll(
                firstScope.GetRequiredService<IJobStore>().EnqueueAsync(request, token),
                secondScope.GetRequiredService<IJobStore>().EnqueueAsync(request, token)),
            cancellationToken);

        // Assert
        Assert.Equal(results[0].JobId, results[1].JobId);
        Assert.Equal(
            [JobEnqueueOutcome.AlreadyEnqueued, JobEnqueueOutcome.Created],
            results.Select(result => result.Outcome).Order());
        Assert.Equal(1, await CountJobsWithKeyAsync(services, request.Key, cancellationToken));
    }

    /// <summary>
    /// Two workers claiming one batch each at the same moment take different jobs. Without <c>SKIP LOCKED</c> the second
    /// would wait for the first's transaction and then take the same row, which is one job run twice.
    /// </summary>
    [Fact]
    public async Task ClaimAsync_TwoWorkersClaimingAtOnce_TakeDisjointJobsRatherThanBlockingOnEachOther()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await DrainAsync(services, cancellationToken);
        await EnqueueAsync(services, uid: 201, cancellationToken);
        await EnqueueAsync(services, uid: 202, cancellationToken);

        // Act
        var claims = await services.InTwoScopesAsync(
            (firstScope, secondScope, token) => Task.WhenAll(
                ClaimAsync(firstScope, batchSize: 1, HeldLease, token),
                ClaimAsync(secondScope, batchSize: 1, HeldLease, token)),
            cancellationToken);

        // Assert
        var claimedJobs = claims.SelectMany(claim => claim).ToArray();
        Assert.Equal(2, claimedJobs.Length);
        Assert.Equal(2, claimedJobs.Select(job => job.JobId).Distinct().Count());
        Assert.Equal(2, claimedJobs.Select(job => job.Lease.Owner).Distinct().Count());

        // Each attempt is the first for its own job, because the claim counts one attempt per job rather than per call.
        Assert.All(claimedJobs, job => Assert.Equal(1, job.AttemptCount));
    }

    /// <summary>
    /// The claim hands back the references the enqueuer wrote, read as the contract its type names, so a handler
    /// resolves committed local state rather than anything copied into the queue.
    /// </summary>
    [Fact]
    public async Task ClaimAsync_AClaimedJob_CarriesTheOccurrenceAndTheAccountItWasEnqueuedWith()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await DrainAsync(services, cancellationToken);
        var request = await RequestAsync(services, uid: 301, cancellationToken);
        await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IJobStore>().EnqueueAsync(request, token),
            cancellationToken);

        // Act
        var claimed = await services.InScopeAsync(
            (scope, token) => ClaimAsync(scope, batchSize: 10, HeldLease, token),
            cancellationToken);

        // Assert
        var job = Assert.Single(claimed);
        Assert.Equal(JobType.ClassifyEmailSpam, job.JobType);
        Assert.Equal(request.Key, job.Key);
        Assert.Equal(SyntheticMailAccount.AccountId, job.AccountId);
        Assert.Equal(
            ((EmailOccurrenceJobPayload)request.Payload).ToOccurrenceId(),
            Assert.IsType<EmailOccurrenceJobPayload>(job.Payload).ToOccurrenceId());
    }

    /// <summary>
    /// A worker that died holding a job leaves a lease that runs out, and the next claim takes the job with a second
    /// attempt counted. Nothing is told the process is gone: an expired lease and an abandoned one are the same row.
    /// The attempt that was displaced then writes nothing through either terminal operation, which is the exclusivity
    /// the whole store is built around.
    /// </summary>
    [Fact]
    public async Task ClaimAsync_AJobHeldUnderAnExpiredLease_IsReclaimedAndTheDisplacedAttemptWritesNothing()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await DrainAsync(services, cancellationToken);
        var jobId = await EnqueueAsync(services, uid: 401, cancellationToken);
        var abandoned = await services.InScopeAsync(
            (scope, token) => ClaimAsync(scope, batchSize: 10, ExpiredLease, token),
            cancellationToken);
        Assert.Equal(jobId, Assert.Single(abandoned).JobId);

        // Act
        var reclaimed = await services.InScopeAsync(
            (scope, token) => ClaimAsync(scope, batchSize: 10, HeldLease, token),
            cancellationToken);

        // Assert
        var job = Assert.Single(reclaimed);
        Assert.Equal(jobId, job.JobId);
        Assert.Equal(2, job.AttemptCount);
        Assert.NotEqual(abandoned[0].Lease.Owner, job.Lease.Owner);

        // Releasing on shutdown is what the displaced attempt is most likely to reach, and it is the more damaging of
        // the two: a release that ignored the owner would put a job the second attempt is actively running back into
        // Pending, and a third attempt would then run it alongside.
        Assert.False(await ReleaseAsync(services, jobId, abandoned[0].Lease.Owner, cancellationToken));
        Assert.Equal(nameof(JobState.Claimed), await ReadStateAsync(services, jobId, cancellationToken));
        Assert.Equal(job.Lease.Owner.Value, await ReadLeaseOwnerAsync(services, jobId, cancellationToken));

        // Completing is the same compare-and-set, which is what stops a slow worker finishing late and overwriting the
        // outcome of the attempt that replaced it.
        Assert.False(await CompleteAsync(services, jobId, abandoned[0].Lease.Owner, cancellationToken));
        Assert.Equal(nameof(JobState.Claimed), await ReadStateAsync(services, jobId, cancellationToken));
    }

    /// <summary>A job scheduled for later is not work a claim may bring forward.</summary>
    [Fact]
    public async Task ClaimAsync_AJobWhoseAvailableInstantHasNotPassed_IsLeftWhereItIs()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await DrainAsync(services, cancellationToken);
        var request = await RequestAsync(services, uid: 501, cancellationToken);
        var scheduled = JobEnqueueRequest.CreateAvailableAt(
            request.Key,
            request.Payload,
            request.AccountId,
            TimeProvider.System.GetUtcNow().AddHours(1));
        await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IJobStore>().EnqueueAsync(scheduled, token),
            cancellationToken);

        // Act
        var claimed = await services.InScopeAsync(
            (scope, token) => ClaimAsync(scope, batchSize: 10, HeldLease, token),
            cancellationToken);

        // Assert
        Assert.Empty(claimed);
    }

    /// <summary>
    /// A job whose type the running build does not declare is left for a replica that does. The absence of a handler is
    /// a fact about the deployment rather than about the work, so a rolling deployment must not consume it and fail it.
    /// </summary>
    [Fact]
    public async Task ClaimAsync_AJobOfATypeThisBuildDoesNotDeclare_IsLeftForAReplicaThatDoes()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await DrainAsync(services, cancellationToken);
        var laterBuildsJobId = Guid.CreateVersion7();
        var laterBuildsJobKey = laterBuildsJobId.ToString();
        var laterBuildsJobType = "a-type-a-later-build-declares";
        var emptyDocument = "{}";
        var enqueuedAt = TimeProvider.System.GetUtcNow();
        var pending = nameof(JobState.Pending);
        await ExecuteAsync(
            services,
            $"""
             INSERT INTO jobs (
                 "Id", "JobType", "IdempotencyKey", "Payload", "MailboxAccountId",
                 "State", "AvailableAt", "EnqueuedAt", "StateChangedAt", "AttemptCount")
             VALUES (
                 {laterBuildsJobId}, {laterBuildsJobType}, {laterBuildsJobKey},
                 CAST({emptyDocument} AS jsonb), NULL,
                 {pending}, {enqueuedAt}, {enqueuedAt}, {enqueuedAt}, 0)
             """,
            cancellationToken);

        // Act
        var claimed = await services.InScopeAsync(
            (scope, token) => ClaimAsync(scope, batchSize: 10, HeldLease, token),
            cancellationToken);

        // Assert
        Assert.Empty(claimed);
        Assert.Equal(nameof(JobState.Pending), await ReadStateAsync(services, laterBuildsJobId, cancellationToken));
    }

    /// <summary>
    /// Renewal is what keeps a long execution from being reclaimed underneath it, and it is conditional on the holder:
    /// an attempt that lost its lease is told so rather than pushing another attempt's expiry out.
    /// </summary>
    [Fact]
    public async Task RenewLeaseAsync_ByTheHolderAndByAnyoneElse_PushesTheExpiryOutOnlyForTheHolder()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await DrainAsync(services, cancellationToken);
        var jobId = await EnqueueAsync(services, uid: 601, cancellationToken);
        var claimed = await services.InScopeAsync(
            (scope, token) => ClaimAsync(scope, batchSize: 10, HeldLease, token),
            cancellationToken);
        var holder = Assert.Single(claimed).Lease;

        // Act
        var renewed = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IJobStore>()
                .RenewLeaseAsync(jobId, holder.Owner, TimeSpan.FromMinutes(30), token),
            cancellationToken);
        var refused = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IJobStore>()
                .RenewLeaseAsync(jobId, JobLeaseOwner.NewAttempt(), TimeSpan.FromMinutes(30), token),
            cancellationToken);

        // Assert
        Assert.NotNull(renewed);
        Assert.Equal(holder.Owner, renewed.Owner);
        Assert.True(renewed.ExpiresAt > holder.ExpiresAt);
        Assert.Null(refused);
    }

    /// <summary>
    /// A completed job is terminal and keeps its key, which is what stops the same trigger enqueuing the same work
    /// again. Freeing the key on completion would let a repeating trigger redo work that has already been done.
    /// </summary>
    [Fact]
    public async Task CompleteAsync_ByTheHolder_LeavesATerminalRowThatStillRefusesTheSameExecution()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await DrainAsync(services, cancellationToken);
        var request = await RequestAsync(services, uid: 701, cancellationToken);
        var enqueued = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IJobStore>().EnqueueAsync(request, token),
            cancellationToken);
        var claimed = await services.InScopeAsync(
            (scope, token) => ClaimAsync(scope, batchSize: 10, HeldLease, token),
            cancellationToken);

        // Act
        var completed = await CompleteAsync(
            services,
            enqueued.JobId,
            Assert.Single(claimed).Lease.Owner,
            cancellationToken);

        // Assert
        Assert.True(completed);
        Assert.Equal(nameof(JobState.Succeeded), await ReadStateAsync(services, enqueued.JobId, cancellationToken));

        var reEnqueued = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IJobStore>().EnqueueAsync(request, token),
            cancellationToken);
        Assert.Equal(JobEnqueueOutcome.AlreadyEnqueued, reEnqueued.Outcome);
        Assert.Equal(enqueued.JobId, reEnqueued.JobId);

        // A terminal job is no longer due, so nothing claims it again.
        Assert.Empty(await services.InScopeAsync(
            (scope, token) => ClaimAsync(scope, batchSize: 10, HeldLease, token),
            cancellationToken));
    }

    /// <summary>
    /// Releasing is what a shutdown does with work it was holding: the job is claimable again at once rather than after
    /// its lease runs out, and the attempt it spent stays counted.
    /// </summary>
    [Fact]
    public async Task ReleaseAsync_ByTheHolder_MakesTheJobClaimableAgainWithoutWaitingOutItsLease()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await DrainAsync(services, cancellationToken);
        var jobId = await EnqueueAsync(services, uid: 801, cancellationToken);
        var claimed = await services.InScopeAsync(
            (scope, token) => ClaimAsync(scope, batchSize: 10, HeldLease, token),
            cancellationToken);

        // Act
        var released = await ReleaseAsync(services, jobId, Assert.Single(claimed).Lease.Owner, cancellationToken);

        // Assert
        Assert.True(released);
        Assert.Equal(nameof(JobState.Pending), await ReadStateAsync(services, jobId, cancellationToken));

        var reclaimed = await services.InScopeAsync(
            (scope, token) => ClaimAsync(scope, batchSize: 10, HeldLease, token),
            cancellationToken);
        Assert.Equal(jobId, Assert.Single(reclaimed).JobId);
        Assert.Equal(2, reclaimed[0].AttemptCount);
    }

    /// <summary>
    /// A failed job is terminal in exactly the way a completed one is. It has to be, or one job whose work cannot
    /// finish would be handed out again as fast as the queue can do it — and it keeps its key, so the trigger that
    /// enqueued it is answered with the failed job rather than allowed to enqueue the same work behind it.
    /// </summary>
    [Fact]
    public async Task FailAsync_ByTheHolder_LeavesATerminalRowNoClaimTakesAgain()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await DrainAsync(services, cancellationToken);
        var request = await RequestAsync(services, uid: 901, cancellationToken);
        var enqueued = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IJobStore>().EnqueueAsync(request, token),
            cancellationToken);
        var claimed = await services.InScopeAsync(
            (scope, token) => ClaimAsync(scope, batchSize: 10, HeldLease, token),
            cancellationToken);

        // Act
        var failed = await FailAsync(services, enqueued.JobId, Assert.Single(claimed).Lease.Owner, cancellationToken);

        // Assert
        Assert.True(failed);
        Assert.Equal(nameof(JobState.Failed), await ReadStateAsync(services, enqueued.JobId, cancellationToken));

        Assert.Empty(await services.InScopeAsync(
            (scope, token) => ClaimAsync(scope, batchSize: 10, HeldLease, token),
            cancellationToken));

        var reEnqueued = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IJobStore>().EnqueueAsync(request, token),
            cancellationToken);
        Assert.Equal(JobEnqueueOutcome.AlreadyEnqueued, reEnqueued.Outcome);
        Assert.Equal(enqueued.JobId, reEnqueued.JobId);
    }

    /// <summary>
    /// Failure is conditional on the lease owner for the same reason completion is: an attempt that was reclaimed and
    /// then gave up must not mark the row failed under the attempt that is still working on it.
    /// </summary>
    [Fact]
    public async Task FailAsync_ByAnAttemptThatNoLongerHoldsTheJob_WritesNothing()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await DrainAsync(services, cancellationToken);
        var jobId = await EnqueueAsync(services, uid: 902, cancellationToken);
        await services.InScopeAsync(
            (scope, token) => ClaimAsync(scope, batchSize: 10, HeldLease, token),
            cancellationToken);

        // Act
        var failed = await FailAsync(services, jobId, JobLeaseOwner.NewAttempt(), cancellationToken);

        // Assert
        Assert.False(failed);
        Assert.Equal(nameof(JobState.Claimed), await ReadStateAsync(services, jobId, cancellationToken));
    }

    /// <summary>Enqueues one job about a synthetic occurrence this class owns, and answers with its identifier.</summary>
    private static async Task<JobId> EnqueueAsync(
        OrchestratedMailFathomServices services,
        uint uid,
        CancellationToken cancellationToken)
    {
        var request = await RequestAsync(services, uid, cancellationToken);
        var enqueued = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IJobStore>().EnqueueAsync(request, token),
            cancellationToken);

        Assert.Equal(JobEnqueueOutcome.Created, enqueued.Outcome);

        return enqueued.JobId;
    }

    /// <summary>Composes one execution about an occurrence in this class's own folder binding.</summary>
    /// <remarks>
    /// The binding is committed first because the job's account column is a foreign key: a queue holding work for an
    /// account that is gone is exactly what the key exists to prevent, so the account has to be there to point at.
    /// </remarks>
    private static async Task<JobEnqueueRequest> RequestAsync(
        OrchestratedMailFathomServices services,
        uint uid,
        CancellationToken cancellationToken)
    {
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var payload = EmailOccurrenceJobPayload.For(EmailOccurrenceId.Create(
            SyntheticMailAccount.AccountId,
            binding.Id,
            ImapUidValidity.Create(90_001),
            ImapUid.Create(uid)));

        return JobEnqueueRequest.Create(
            JobIdempotencyKey.Create($"{FolderAlias}/{uid}"),
            payload,
            SyntheticMailAccount.AccountId);
    }

    private static Task<IReadOnlyList<LeasedJob>> ClaimAsync(
        IServiceProvider scope,
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken) => scope.GetRequiredService<IJobStore>().ClaimAsync(
            JobClaimRequest.Create(
                [JobType.ClassifyEmailSpam],
                batchSize,
                leaseDuration,
                JobLeaseOwner.NewAttempt()),
            cancellationToken);

    private static Task<bool> CompleteAsync(
        OrchestratedMailFathomServices services,
        JobId jobId,
        JobLeaseOwner owner,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IJobStore>().CompleteAsync(jobId, owner, token),
            cancellationToken);

    private static Task<bool> FailAsync(
        OrchestratedMailFathomServices services,
        JobId jobId,
        JobLeaseOwner owner,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IJobStore>().FailAsync(jobId, owner, token),
            cancellationToken);

    private static Task<bool> ReleaseAsync(
        OrchestratedMailFathomServices services,
        JobId jobId,
        JobLeaseOwner owner,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IJobStore>().ReleaseAsync(jobId, owner, token),
            cancellationToken);

    /// <summary>Takes everything claimable and completes it, so a test acts on a queue holding only its own work.</summary>
    private static async Task DrainAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var claimed = await services.InScopeAsync(
                (scope, token) => ClaimAsync(scope, batchSize: 100, HeldLease, token),
                cancellationToken);

            if (claimed.Count == 0)
            {
                return;
            }

            foreach (var job in claimed)
            {
                await CompleteAsync(services, job.JobId, job.Lease.Owner, cancellationToken);
            }
        }
    }

    private static Task<string?> ReadStateAsync(
        OrchestratedMailFathomServices services,
        JobId jobId,
        CancellationToken cancellationToken) => ReadStateAsync(services, jobId.Value, cancellationToken);

    private static Task<string?> ReadStateAsync(
        OrchestratedMailFathomServices services,
        Guid jobId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().Database
                .SqlQuery<string?>($"""SELECT "State" AS "Value" FROM jobs WHERE "Id" = {jobId}""")
                .SingleOrDefaultAsync(token),
            cancellationToken);

    /// <summary>Reads the lease holder straight from the row, so an assertion about it depends on no other operation.</summary>
    private static Task<string?> ReadLeaseOwnerAsync(
        OrchestratedMailFathomServices services,
        JobId jobId,
        CancellationToken cancellationToken)
    {
        var jobIdValue = jobId.Value;

        return services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().Database
                .SqlQuery<string?>($"""SELECT "LeaseOwner" AS "Value" FROM jobs WHERE "Id" = {jobIdValue}""")
                .SingleOrDefaultAsync(token),
            cancellationToken);
    }

    private static Task<int> CountJobsWithKeyAsync(
        OrchestratedMailFathomServices services,
        JobIdempotencyKey key,
        CancellationToken cancellationToken)
    {
        var keyValue = key.Value;

        return services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().Database
                .SqlQuery<int>($"""SELECT COUNT(*)::int AS "Value" FROM jobs WHERE "IdempotencyKey" = {keyValue}""")
                .SingleAsync(token),
            cancellationToken);
    }

    private static Task<int> ExecuteAsync(
        OrchestratedMailFathomServices services,
        FormattableString statement,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().Database
                .ExecuteSqlAsync(statement, token),
            cancellationToken);
}
