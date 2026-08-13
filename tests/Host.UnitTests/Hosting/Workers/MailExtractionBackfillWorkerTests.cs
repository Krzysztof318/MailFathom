// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Persistence;
using MailFathom.Common.Observability;
using MailFathom.Domain.Emails;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Hosting.Workers;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Observability;
using MailFathom.TestSupport;
using Microsoft.Extensions.DependencyInjection;
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

    private static MailExtractionBackfillWorker CreateWorker(
        MailExtractionBackfillOptions settings,
        IStoredEmailExtractionBackfillStore backfillStore,
        out RecordingLogger<MailExtractionBackfillWorker> logger)
    {
        logger = new RecordingLogger<MailExtractionBackfillWorker>();
        var timeProvider = new FakeTimeProvider();

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(timeProvider);
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
            timeProvider);
    }
}
