// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Move;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Host.Configuration.Persistence;
using MailFathom.Host.Hosting.Workers;
using MailFathom.Infrastructure.ObjectStorage;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Orchestration;
using MailFathom.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.IntegrationTests.Hosting;

/// <summary>Proves that two replicas asking for one move in the same instant put its payload into the bucket once.</summary>
/// <remarks>
/// <para>
/// Each replica is a service graph of its own over the orchestrated database and endpoint, and each runs the worker a
/// deployment runs. Without the move's lease both would carry the pass their interval allowed, and the payload both
/// reached would be put into the bucket twice and pointed at once — which the run's own counts cannot show, because a row
/// is repointed only while it is still database-backed. So what is counted is the objects the endpoint holds.
/// </para>
/// <para>
/// The two workers share one clock the test drives, advanced by one interval once both are waiting on it and never again.
/// That is the instant both replicas ask for the move, and it is the only interval that ever elapses: the holder's rest
/// after its pass and the other replica's retry both wait on a clock that no longer moves, so exactly one pass can start
/// however long the orchestrated services take to answer. The walk spans every class's content in the one shared
/// database, so it is started just past the newest payload this class did not write, and each pass of this suite carries
/// one payload.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedStoredContentMoveHoldTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderAlias = "stored-content-move-hold";

    private const uint Uid = 71;

    /// <summary>How many workers have to be waiting on the clock before the one interval is let elapse.</summary>
    private const int ReplicaCount = 2;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    /// <summary>Outlasts the test, so the holder never has to renew against a clock that stops after one interval.</summary>
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromHours(1);

    private static readonly TimeSpan LeaseRenewalInterval = TimeSpan.FromMinutes(30);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private static readonly TimeSpan Deadline = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task ExecuteAsync_TwoReplicasAskingForOneMoveAtOnce_PutItsPayloadIntoTheBucketOnce()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var firstReplica = await OrchestratedMailFathomServices.StartAsync(
            orchestration,
            cancellationToken,
            storesContentInObjectStorage: true);
        await using var secondReplica = await OrchestratedMailFathomServices.StartAsync(
            orchestration,
            cancellationToken,
            storesContentInObjectStorage: true);
        var payloadId = await StoreInDatabaseAsync(firstReplica, cancellationToken);
        var objectsBefore = await CountObjectsAsync(firstReplica, cancellationToken);
        await StartMoveAfterOtherPayloadsAsync(firstReplica, payloadId, cancellationToken);
        var clock = new WaitCountingClock();
        using var firstWorker = await CreateWorkerAsync(firstReplica, clock, cancellationToken);
        using var secondWorker = await CreateWorkerAsync(secondReplica, clock, cancellationToken);

        // Act
        await firstWorker.StartAsync(cancellationToken);
        await secondWorker.StartAsync(cancellationToken);
        await WaitUntilAsync(_ => Task.FromResult(clock.ArmedWaits >= ReplicaCount), cancellationToken);
        clock.Advance(Interval);
        await WaitUntilAsync(
            async token => await ReadRunAsync(firstReplica, token) is { CopiedPayloadCount: > 0 },
            cancellationToken);
        await PauseAsync(secondReplica, cancellationToken);
        await firstWorker.StopAsync(cancellationToken);
        await secondWorker.StopAsync(cancellationToken);
        var run = await ReadRunAsync(firstReplica, cancellationToken);

        // Assert
        Assert.NotNull(run);
        Assert.Equal(1, run.CopiedPayloadCount);
        Assert.Equal(1, await CountObjectsAsync(firstReplica, cancellationToken) - objectsBefore);
        Assert.Equal(ContentStorageBackend.ObjectStorage, await ReadBackendAsync(firstReplica, payloadId, cancellationToken));
    }

    /// <summary>Stores this class's payload in the database, as one occurrence, which is what the move carries.</summary>
    private static async Task<Guid> StoreInDatabaseAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken)
    {
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var rawMime = SyntheticEmail.RawMimeOf(FolderAlias, 4_096);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, Uid);

        return await services.CommitProducingAsync(
            async (scope, session, token) =>
            {
                var storedEmailId = await scope.GetRequiredService<IEmailMetadataRepository>().UpsertMetadataAsync(
                    session,
                    SyntheticMailAccount.User,
                    SyntheticEmail.RemoteMetadataOf(occurrenceId, FolderAlias, rawMime.Length),
                    extractedMetadata: null,
                    StoredEmailContentAvailability.Available,
                    token);

                await scope.GetRequiredService<IEmailContentStore>().SaveContentAsync(
                    session,
                    storedEmailId,
                    occurrenceId,
                    PlacedEmailContent.InDatabase(rawMime),
                    token);

                return storedEmailId.Value;
            },
            cancellationToken);
    }

    /// <summary>Records a running move positioned just past the newest incoming payload this class did not write.</summary>
    /// <remarks>
    /// Identities are ordered by when they were written, and PostgreSQL is asked for the order because it is the order the
    /// walk itself follows. What this class stores is therefore after the position, and nothing another class stored is.
    /// </remarks>
    private static async Task StartMoveAfterOtherPayloadsAsync(
        OrchestratedMailFathomServices services,
        Guid payloadId,
        CancellationToken cancellationToken)
    {
        var position = await services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<MailFathomDbContext>()
                .EmailMessageContents
                .AsNoTracking()
                .Where(content => content.StoredEmailId != payloadId)
                .OrderByDescending(content => content.StoredEmailId)
                .Select(content => (Guid?)content.StoredEmailId)
                .FirstOrDefaultAsync(token),
            cancellationToken);

        var committed = await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IStoredContentMoveRunStore>().SaveAsync(
                session,
                new StoredContentMoveRun
                {
                    RequestedAt = scope.GetRequiredService<TimeProvider>().GetUtcNow(),
                    State = StoredContentMoveState.Running,
                    Kind = EmailContentKind.IncomingMessage,
                    ResumeAfter = position,
                },
                token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, committed);
    }

    private static async Task<StoredContentMoveWorker> CreateWorkerAsync(
        OrchestratedMailFathomServices replica,
        TimeProvider clock,
        CancellationToken cancellationToken) => new(
            await replica.InScopeAsync(
                (scope, _) => Task.FromResult(scope.GetRequiredService<IServiceScopeFactory>()),
                cancellationToken),
            Options.Create(new ContentMoveOptions
            {
                Interval = Interval,
                LeaseDuration = LeaseDuration,
                LeaseRenewalInterval = LeaseRenewalInterval,
            }),
            NullLogger<StoredContentMoveWorker>.Instance,
            NullLoggerFactory.Instance,
            clock);

    /// <summary>Waits for something the replicas do on their own, bounded so a replica that never does it fails the test rather than hanging it.</summary>
    private static async Task WaitUntilAsync(
        Func<CancellationToken, Task<bool>> condition,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Deadline);

        while (!await condition(deadline.Token))
        {
            await Task.Delay(PollInterval, deadline.Token);
        }
    }

    private static Task<StoredContentMoveRun?> ReadRunAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IStoredContentMoveRunStore>().FindAsync(token),
            cancellationToken);

    /// <summary>Leaves the move paused, as an operator leaves one, rather than running for the classes after this one.</summary>
    private static Task<StoredContentMoveRun?> PauseAsync(OrchestratedMailFathomServices services, CancellationToken cancellationToken) =>
        services.AsCallerInScopeAsync(
            (scope, token) => scope.GetRequiredService<StoredContentMoveControl>().PauseAsync(token),
            [MailFathomPermission.AdminOperate],
            cancellationToken);

    /// <summary>Counts every object beneath this deployment's prefix, page by page.</summary>
    private static Task<int> CountObjectsAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                var objectStore = scope.GetRequiredService<IEmailContentObjectStore>();
                var objectCount = 0;
                string? continuationToken = null;

                do
                {
                    var page = await objectStore.ListAsync(continuationToken, maxObjects: 1_000, token);
                    objectCount += page.Objects.Count;
                    continuationToken = page.ContinuationToken;
                }
                while (continuationToken is not null);

                return objectCount;
            },
            cancellationToken);

    private static Task<ContentStorageBackend> ReadBackendAsync(
        OrchestratedMailFathomServices services,
        Guid payloadId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<MailFathomDbContext>()
                .EmailMessageContents
                .AsNoTracking()
                .Where(content => content.StoredEmailId == payloadId)
                .Select(content => content.Backend)
                .SingleAsync(token),
            cancellationToken);

    /// <summary>A controllable clock that also counts the waits armed on it, so the one interval elapses only once every worker waits on it.</summary>
    /// <remarks>
    /// A hosted worker arms its first wait on the thread pool after its start has returned, so an advance made straight
    /// after starting could pass before either worker waited and elapse nothing. An infinite due time arms nothing and is
    /// not counted.
    /// </remarks>
    private sealed class WaitCountingClock : FakeTimeProvider
    {
        private int armedWaits;

        public int ArmedWaits => Volatile.Read(ref this.armedWaits);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime != Timeout.InfiniteTimeSpan)
            {
                Interlocked.Increment(ref this.armedWaits);
            }

            return base.CreateTimer(callback, state, dueTime, period);
        }
    }
}
