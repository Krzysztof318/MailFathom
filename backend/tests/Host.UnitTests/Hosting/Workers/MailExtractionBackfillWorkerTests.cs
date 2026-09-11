// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics;
using MailFathom.Application.Coordination;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Persistence;
using MailFathom.Common.Observability;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Hosting.Workers;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Observability;
using MailFathom.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Workers;

public sealed class MailExtractionBackfillWorkerTests : IDisposable
{
    private const string RunDurationInstrument = "mailfathom.mail.extraction.backfill.run.duration";

    private const string OutstandingGauge = "mailfathom.mail.extraction.backfill.outstanding";

    private const string OutcomeTagName = "mailfathom.mail.extraction.backfill.outcome";

    /// <summary>Guards against a hung worker. No assertion depends on how long the run actually takes.</summary>
    private static readonly TimeSpan DeadlockGuard = TimeSpan.FromSeconds(30);

    private readonly ConcurrentBag<Activity> publishedRuns = [];
    private readonly ActivityListener listener;

    /// <summary>Listens to the real activity source, narrowed to this worker's own span name.</summary>
    /// <remarks>
    /// The source is the process's and is shared by everything MailFathom publishes, so the name is what keeps a span
    /// another test class produced at the same moment out of these assertions.
    /// </remarks>
    public MailExtractionBackfillWorkerTests()
    {
        this.listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Telemetry.Name,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == MailExtractionBackfillWorker.RunSpanName)
                {
                    this.publishedRuns.Add(activity);
                }
            },
        };

        ActivitySource.AddActivityListener(this.listener);
    }

    public void Dispose() => this.listener.Dispose();

    [Fact]
    public async Task ExecuteAsync_BackfillDisabled_NeverReadsAStoredEmail()
    {
        // Arrange
        var backfillStore = Substitute.For<IStoredEmailExtractionBackfillStore>();
        using var worker = CreateWorker(new MailExtractionBackfillOptions { Enabled = false }, backfillStore, out _);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;

        // Assert
        await backfillStore.DidNotReceiveWithAnyArgs().FindResumePositionAsync(CancellationToken.None);
    }

    /// <summary>Nothing left to extract ends the worker, because every later message is extracted as it is written.</summary>
    [Fact]
    public async Task ExecuteAsync_NoStoredEmailAwaitsExtraction_RunsOnceAndStops()
    {
        // Arrange
        var backfillStore = Substitute.For<IStoredEmailExtractionBackfillStore>();
        backfillStore
            .GetEmailsAwaitingExtractionAsync(Arg.Any<StoredEmailId?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<StoredEmailAwaitingExtraction>>([]));
        using var worker = CreateWorker(new MailExtractionBackfillOptions(), backfillStore, out var logger);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Assert
        await backfillStore.Received(1).FindResumePositionAsync(Arg.Any<CancellationToken>());
        Assert.Contains(logger.Messages, message => message.Contains("reached the end of the stored emails", StringComparison.Ordinal));
    }

    /// <summary>A failed run says nothing about whether work remains, so the worker stays alive to resume next interval.</summary>
    [Fact]
    public async Task ExecuteAsync_RunFails_LogsItWithoutEndingTheWorker()
    {
        // Arrange
        var firstRunFailed = new TaskCompletionSource();
        var backfillStore = Substitute.For<IStoredEmailExtractionBackfillStore>();
        backfillStore
            .FindResumePositionAsync(Arg.Any<CancellationToken>())
            .Returns<StoredEmailId?>(_ =>
            {
                firstRunFailed.TrySetResult();

                throw new InvalidOperationException("the database is unavailable");
            });
        using var worker = CreateWorker(new MailExtractionBackfillOptions(), backfillStore, out var logger);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await firstRunFailed.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.Contains(logger.Messages, message => message.Contains("backfill run failed", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("reached the end of the stored emails", StringComparison.Ordinal));
    }

    /// <summary>A conflict with a competing writer is reported as a deferral rather than as a failure of the walk.</summary>
    [Fact]
    public async Task ExecuteAsync_ConcurrencyConflict_ReportsADeferral()
    {
        // Arrange
        var firstRunDeferred = new TaskCompletionSource();
        var backfillStore = Substitute.For<IStoredEmailExtractionBackfillStore>();
        backfillStore
            .FindResumePositionAsync(Arg.Any<CancellationToken>())
            .Returns<StoredEmailId?>(_ =>
            {
                firstRunDeferred.TrySetResult();

                throw new PersistenceConcurrencyConflictException("A competing writer won the race.");
            });
        using var measurements = new RecordedMailFathomMeasurements(RunDurationInstrument);
        using var worker = CreateWorker(new MailExtractionBackfillOptions(), backfillStore, out var logger);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await firstRunDeferred.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.Contains(
            logger.Messages,
            message => message.Contains("optimistic concurrency conflict", StringComparison.Ordinal));
        Assert.Equal(
            "deferred",
            Assert.Single(this.publishedRuns).GetTagItem(OutcomeTagName));
        Assert.Contains("deferred", measurements.DimensionOf(RunDurationInstrument, OutcomeTagName));
    }

    /// <summary>
    /// Shutdown is not a failure, and the span says which it was: a rolling deployment would otherwise read as a
    /// backfill that broke on every replica it stopped.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_APassTheHostStopped_PublishesItAsInterruptedRatherThanFailed()
    {
        // Arrange
        var firstRunStarted = new TaskCompletionSource();
        var backfillStore = Substitute.For<IStoredEmailExtractionBackfillStore>();
        backfillStore
            .FindResumePositionAsync(Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                firstRunStarted.TrySetResult();

                return BlockedUntilStopped(call.Arg<CancellationToken>());
            });
        using var measurements = new RecordedMailFathomMeasurements(RunDurationInstrument);
        using var worker = CreateWorker(new MailExtractionBackfillOptions(), backfillStore, out _);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await firstRunStarted.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        await worker.StopAsync(CancellationToken.None);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => worker.ExecuteTask!);

        // Assert
        var span = Assert.Single(this.publishedRuns);

        Assert.Equal("interrupted", span.GetTagItem(OutcomeTagName));
        Assert.NotEqual(ActivityStatusCode.Error, span.Status);
        Assert.Contains("interrupted", measurements.DimensionOf(RunDurationInstrument, OutcomeTagName));
    }

    /// <summary>A run that reaches the host's shutdown instead of finishing, which is what the interrupted outcome names.</summary>
    /// <remarks>
    /// It waits on the stopping token rather than on a clock, so nothing here depends on how long the test host takes
    /// to get to <c>StopAsync</c>.
    /// </remarks>
    private static Task<StoredEmailId?> BlockedUntilStopped(CancellationToken stoppingToken)
    {
        var blocked = new TaskCompletionSource<StoredEmailId?>(TaskCreationOptions.RunContinuationsAsynchronously);

        stoppingToken.Register(() => blocked.TrySetCanceled(stoppingToken));

        return blocked.Task;
    }

    /// <summary>
    /// Work an interval caused rather than a request is otherwise a set of parentless database spans competing with the
    /// requests around them, so a pass is published as a span of its own with what it turned out to have done.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_APassThatFinished_PublishesItAsASpanOfCountsAndAnEnding()
    {
        // Arrange
        var backfillStore = Substitute.For<IStoredEmailExtractionBackfillStore>();
        backfillStore
            .GetEmailsAwaitingExtractionAsync(Arg.Any<StoredEmailId?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<StoredEmailAwaitingExtraction>>([]));
        using var worker = CreateWorker(new MailExtractionBackfillOptions(), backfillStore, out _);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Assert
        var span = Assert.Single(this.publishedRuns);

        Assert.Equal("backfill_email_extraction", span.OperationName);
        Assert.Equal(
            [
                ("mailfathom.mail.extraction.backfill.extracted", "0"),
                ("mailfathom.mail.extraction.backfill.unreadable", "0"),
                ("mailfathom.mail.extraction.backfill.missing_content", "0"),
                ("mailfathom.mail.extraction.backfill.remaining", "False"),
                ("mailfathom.mail.extraction.backfill.outcome", "succeeded"),
            ],
            span.TagObjects.Select(tag => (tag.Key, tag.Value?.ToString())));
    }

    /// <summary>A pass that broke is the one worth attributing, so it publishes the ending it reached rather than none.</summary>
    [Fact]
    public async Task ExecuteAsync_APassThatFailed_PublishesTheEndingItReached()
    {
        // Arrange
        var firstRunFailed = new TaskCompletionSource();
        var backfillStore = Substitute.For<IStoredEmailExtractionBackfillStore>();
        backfillStore
            .FindResumePositionAsync(Arg.Any<CancellationToken>())
            .Returns<StoredEmailId?>(_ =>
            {
                firstRunFailed.TrySetResult();

                throw new InvalidOperationException("the database is unavailable");
            });
        using var measurements = new RecordedMailFathomMeasurements(RunDurationInstrument);
        using var worker = CreateWorker(new MailExtractionBackfillOptions(), backfillStore, out _);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await firstRunFailed.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        var span = Assert.Single(
            this.publishedRuns,
            run => run.GetTagItem(OutcomeTagName) is "failed");

        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Contains("failed", measurements.DimensionOf(RunDurationInstrument, OutcomeTagName));
    }

    /// <summary>
    /// The instruments answer over every pass what the span answers about one, so the worker's four endings have to
    /// reach the right one of them: a run that deferred and a run that broke are the same line on a dashboard
    /// otherwise, and the backlog beside them is what says whether the walk is converging at all.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_APassThatFinished_RecordsItsEndingAndTheBacklogItLeftBehind()
    {
        // Arrange
        var backfillStore = Substitute.For<IStoredEmailExtractionBackfillStore>();
        backfillStore
            .GetEmailsAwaitingExtractionAsync(Arg.Any<StoredEmailId?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<StoredEmailAwaitingExtraction>>([]));
        using var measurements = new RecordedMailFathomMeasurements(RunDurationInstrument, OutstandingGauge);
        using var worker = CreateWorker(new MailExtractionBackfillOptions(), backfillStore, out _);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        measurements.ObserveGauges();

        // Assert
        Assert.Contains("succeeded", measurements.DimensionOf(RunDurationInstrument, OutcomeTagName));
        Assert.Contains(0d, measurements.ValuesOf(OutstandingGauge));
    }

    /// <summary>A replica refused the walk reads nothing and keeps its worker, asking again on its own interval.</summary>
    /// <remarks>
    /// A refusal says nothing about whether emails still await extraction, so it must not end the worker the way a run
    /// that found nothing left does: the replica holding the walk may stop before it finishes.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_AnotherReplicaIsWalking_ReadsNoPositionAndAsksAgainOnTheInterval()
    {
        // Arrange
        var settings = new MailExtractionBackfillOptions();
        var backfillStore = Substitute.For<IStoredEmailExtractionBackfillStore>();
        var leases = new ScriptedWorkLeaseStore { HeldElsewhere = true };
        var clock = new FakeTimeProvider();
        using var worker = CreateWorker(settings, backfillStore, out var logger, leases, clock);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await leases.WaitForClaimAsync(TestContext.Current.CancellationToken).WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        await SynchronizationTestHost.AdvanceUntilAsync(
            clock,
            leases.WaitForClaimAsync(TestContext.Current.CancellationToken),
            settings.Interval,
            DeadlockGuard);

        // Assert
        Assert.False(worker.ExecuteTask!.IsCompleted);
        await worker.StopAsync(CancellationToken.None);
        await backfillStore.DidNotReceiveWithAnyArgs().FindResumePositionAsync(CancellationToken.None);
        Assert.Contains(
            logger.Messages,
            message => message.Contains("Another replica is running the extracted-text backfill", StringComparison.Ordinal));
    }

    /// <summary>
    /// A run whose hold is lost stops where it is: the batch it committed stays committed for the next holder to resume
    /// from, and the worker waits for its next interval rather than ending as though the walk were finished.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_HoldLostMidRun_StopsTheRunAndLeavesTheLastCommittedPosition()
    {
        // Arrange
        var settings = new MailExtractionBackfillOptions { BatchSize = 1, MaxBatchesPerRun = 2 };
        var committed = StoredEmailId.Create(Guid.CreateVersion7());
        var secondBatchAsked = new TaskCompletionSource();
        var secondBatchStopped = new TaskCompletionSource();
        var backfillStore = Substitute.For<IStoredEmailExtractionBackfillStore>();
        var leases = new ScriptedWorkLeaseStore();
        var clock = new FakeTimeProvider();

        // The first message's content is gone, so the run steps over it, commits the position past it, and asks for the
        // next batch — which the database never answers, so the run is still going when its hold is lost.
        backfillStore
            .GetEmailsAwaitingExtractionAsync(Arg.Any<StoredEmailId?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromResult<IReadOnlyList<StoredEmailAwaitingExtraction>>([AwaitingExtraction(committed)]),
                call => NeverAnswerUntilStoppedAsync(secondBatchAsked, secondBatchStopped, call.Arg<CancellationToken>()));
        using var worker = CreateWorker(settings, backfillStore, out _, leases, clock);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await leases.WaitForClaimAsync(TestContext.Current.CancellationToken).WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        await secondBatchAsked.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        leases.HeldElsewhere = true;
        clock.Advance(settings.LeaseRenewalInterval);
        await secondBatchStopped.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        await SynchronizationTestHost.AdvanceUntilAsync(
            clock,
            leases.WaitForClaimAsync(TestContext.Current.CancellationToken),
            settings.Interval,
            DeadlockGuard);

        // Assert
        Assert.False(worker.ExecuteTask!.IsCompleted);
        await worker.StopAsync(CancellationToken.None);
        await backfillStore.Received(1).SaveResumePositionAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Any<StoredEmailId>(),
            Arg.Any<CancellationToken>());
        await backfillStore.Received(1).SaveResumePositionAsync(
            Arg.Any<IPersistenceSession>(),
            committed,
            Arg.Any<CancellationToken>());
    }

    /// <summary>A worker that ends itself gives the walk back first, so the replicas still asking are not kept from it.</summary>
    [Fact]
    public async Task ExecuteAsync_NoStoredEmailAwaitsExtraction_GivesTheWalkBackAsItEnds()
    {
        // Arrange
        var backfillStore = Substitute.For<IStoredEmailExtractionBackfillStore>();
        backfillStore
            .GetEmailsAwaitingExtractionAsync(Arg.Any<StoredEmailId?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<StoredEmailAwaitingExtraction>>([]));
        var leases = new ScriptedWorkLeaseStore();
        using var worker = CreateWorker(new MailExtractionBackfillOptions(), backfillStore, out _, leases);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([MailExtractionBackfillWorker.WalkScope], leases.Releases);
        Assert.Empty(leases.HeldScopes);
    }

    private static StoredEmailAwaitingExtraction AwaitingExtraction(StoredEmailId storedEmailId) =>
        new(
            storedEmailId,
            EmailOccurrenceId.Create(
                MailAccountId.Create("primary"),
                new MailFolderResolutionId(MailFolderAlias.Create("inbox"), MailFolderResolutionGeneration.First),
                ImapUidValidity.Create(1),
                ImapUid.Create(1)),
            MailUserId.Create(Guid.CreateVersion7()));

    private static async Task<IReadOnlyList<StoredEmailAwaitingExtraction>> NeverAnswerUntilStoppedAsync(
        TaskCompletionSource asked,
        TaskCompletionSource stopped,
        CancellationToken cancellationToken)
    {
        asked.TrySetResult();

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        finally
        {
            stopped.TrySetResult();
        }

        return [];
    }

    private static MailExtractionBackfillWorker CreateWorker(
        MailExtractionBackfillOptions settings,
        IStoredEmailExtractionBackfillStore backfillStore,
        out RecordingLogger<MailExtractionBackfillWorker> logger,
        ScriptedWorkLeaseStore? leases = null,
        FakeTimeProvider? timeProvider = null)
    {
        logger = new RecordingLogger<MailExtractionBackfillWorker>();
        timeProvider ??= new FakeTimeProvider();

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(timeProvider);
        services.AddSingleton<IWorkLeaseStore>(leases ?? new ScriptedWorkLeaseStore());
        services.AddSingleton(backfillStore);
        services.AddSingleton(Substitute.For<IEmailContentStore>());
        services.AddSingleton(Substitute.For<IEmailMimeReader>());
        services.AddSingleton(Substitute.For<IPersistenceSessionFactory>());
        services.AddSingleton(new PersistenceConcurrencyOptions());
        services.AddSingleton(new StoredEmailExtractionBackfillOptions
        {
            BatchSize = settings.BatchSize,
            MaxBatchesPerRun = settings.MaxBatchesPerRun,
        });
        services.AddScoped<OptimisticConcurrencyRetryPolicy>();
        services.AddScoped<StoredEmailExtractionBackfill>();

        var serviceProvider = services.BuildServiceProvider();

        return new MailExtractionBackfillWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(settings),
            new MailExtractionBackfillTelemetry(),
            logger,
            NullLoggerFactory.Instance,
            timeProvider);
    }
}
