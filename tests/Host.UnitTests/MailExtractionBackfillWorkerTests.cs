// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent;
using MailFathom.Application.Emails;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Emails;
using MailFathom.Host.Configuration;
using MailFathom.Host.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests;

public sealed class MailExtractionBackfillWorkerTests
{
    /// <summary>Guards against a hung worker. No assertion depends on how long the run actually takes.</summary>
    private static readonly TimeSpan DeadlockGuard = TimeSpan.FromSeconds(30);

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
        using var worker = CreateWorker(new MailExtractionBackfillOptions(), backfillStore, out var logger);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await firstRunDeferred.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.Contains(
            logger.Messages,
            message => message.Contains("optimistic concurrency conflict", StringComparison.Ordinal));
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
            logger,
            timeProvider);
    }
}
