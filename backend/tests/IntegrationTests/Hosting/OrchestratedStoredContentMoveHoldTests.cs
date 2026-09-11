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
using Xunit;

namespace MailFathom.IntegrationTests.Hosting;

/// <summary>Proves that two replicas carrying one move against one database put each payload into the bucket once.</summary>
/// <remarks>
/// <para>
/// Each replica is a service graph of its own over the orchestrated database and endpoint, and each runs the worker a
/// deployment runs. Without the move's lease both would carry passes over the same position, and a payload both reached
/// would be put into the bucket twice and pointed at once — which the run's own counts cannot show, because a row is
/// repointed only while it is still database-backed. So what is counted is the objects the endpoint holds.
/// </para>
/// <para>
/// The walk spans every class's content in the one shared database, so it is started just past the newest payload this
/// class did not write, and each pass of this suite carries one payload. The move is paused as soon as this class's
/// payloads are carried, which is inside the interval the holder keeps the move for after every pass.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedStoredContentMoveHoldTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderAlias = "stored-content-move-hold";

    private const int PayloadCount = 3;

    private const uint FirstUid = 71;

    /// <summary>Long enough to pause the move between two passes by polling, and short enough that three passes take seconds.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private static readonly TimeSpan MoveDeadline = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task ExecuteAsync_TwoReplicasCarryingOneMove_PutEachPayloadIntoTheBucketOnce()
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
        var payloadIds = await StoreInDatabaseAsync(firstReplica, cancellationToken);
        var objectsBefore = await CountObjectsAsync(firstReplica, cancellationToken);
        await StartMoveAfterOtherPayloadsAsync(firstReplica, payloadIds, cancellationToken);
        using var firstWorker = await CreateWorkerAsync(firstReplica, cancellationToken);
        using var secondWorker = await CreateWorkerAsync(secondReplica, cancellationToken);

        // Act
        await firstWorker.StartAsync(cancellationToken);
        await secondWorker.StartAsync(cancellationToken);
        var run = await WaitUntilCarriedAsync(firstReplica, cancellationToken);
        await PauseAsync(secondReplica, cancellationToken);
        await firstWorker.StopAsync(cancellationToken);
        await secondWorker.StopAsync(cancellationToken);

        // Assert
        Assert.Equal(PayloadCount, run.CopiedPayloadCount);
        Assert.Equal(PayloadCount, await CountObjectsAsync(firstReplica, cancellationToken) - objectsBefore);
        Assert.All(
            await ReadBackendsAsync(firstReplica, payloadIds, cancellationToken),
            backend => Assert.Equal(ContentStorageBackend.ObjectStorage, backend));
    }

    /// <summary>Stores this class's payloads in the database, one occurrence each, which is what the move carries.</summary>
    private static async Task<Guid[]> StoreInDatabaseAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken)
    {
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var payloadIds = new Guid[PayloadCount];

        for (var index = 0; index < PayloadCount; index++)
        {
            var subject = $"stored-content-move-hold-{index}";
            var rawMime = SyntheticEmail.RawMimeOf(subject, 4_096);
            var occurrenceId = SyntheticEmail.OccurrenceIn(binding, FirstUid + (uint)index);

            payloadIds[index] = await services.CommitProducingAsync(
                async (scope, session, token) =>
                {
                    var storedEmailId = await scope.GetRequiredService<IEmailMetadataRepository>().UpsertMetadataAsync(
                        session,
                        SyntheticMailAccount.User,
                        SyntheticEmail.RemoteMetadataOf(occurrenceId, subject, rawMime.Length),
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

        return payloadIds;
    }

    /// <summary>Records a running move positioned just past the newest incoming payload this class did not write.</summary>
    /// <remarks>
    /// Identities are ordered by when they were written, and PostgreSQL is asked for the order because it is the order the
    /// walk itself follows. Everything this class stores is therefore after the position, and nothing another class stored
    /// is.
    /// </remarks>
    private static async Task StartMoveAfterOtherPayloadsAsync(
        OrchestratedMailFathomServices services,
        Guid[] payloadIds,
        CancellationToken cancellationToken)
    {
        var position = await services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<MailFathomDbContext>()
                .EmailMessageContents
                .AsNoTracking()
                .Where(content => !payloadIds.Contains(content.StoredEmailId))
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
        CancellationToken cancellationToken) => new(
            await replica.InScopeAsync(
                (scope, _) => Task.FromResult(scope.GetRequiredService<IServiceScopeFactory>()),
                cancellationToken),
            Options.Create(new ContentMoveOptions { Interval = Interval }),
            NullLogger<StoredContentMoveWorker>.Instance,
            NullLoggerFactory.Instance,
            TimeProvider.System);

    /// <summary>Waits until the move has recorded this class's payloads as carried, however many passes that took.</summary>
    private static async Task<StoredContentMoveRun> WaitUntilCarriedAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(MoveDeadline);

        while (true)
        {
            var run = await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IStoredContentMoveRunStore>().FindAsync(token),
                deadline.Token);

            if (run is not null && run.CopiedPayloadCount >= PayloadCount)
            {
                return run;
            }

            await Task.Delay(PollInterval, deadline.Token);
        }
    }

    /// <summary>Stops the move before its next pass could reach another class's payloads, as an operator stops it.</summary>
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

    private static Task<ContentStorageBackend[]> ReadBackendsAsync(
        OrchestratedMailFathomServices services,
        Guid[] payloadIds,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<MailFathomDbContext>()
                .EmailMessageContents
                .AsNoTracking()
                .Where(content => payloadIds.Contains(content.StoredEmailId))
                .Select(content => content.Backend)
                .ToArrayAsync(token),
            cancellationToken);
}
