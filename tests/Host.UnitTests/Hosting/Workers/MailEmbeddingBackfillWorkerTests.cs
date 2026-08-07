// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Backfill;
using MailFathom.Application.Emails.Embeddings.Generation;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Emails;
using MailFathom.Host.Configuration.Embeddings;
using MailFathom.Host.Hosting.Workers;
using MailFathom.Infrastructure.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Workers;

public sealed class MailEmbeddingBackfillWorkerTests
{
    /// <summary>Guards against a hung worker. No assertion depends on how long the run actually takes.</summary>
    private static readonly TimeSpan DeadlockGuard = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan IdleSweepInterval = TimeSpan.FromMinutes(15);

    [Fact]
    public async Task ExecuteAsync_BackfillDisabled_NeverReadsAStoredEmail()
    {
        // Arrange
        using var world = CreateWorld(new EmbeddingBackfillOptions { Enabled = false });

        // Act
        await world.Worker.StartAsync(CancellationToken.None);
        await world.Worker.ExecuteTask!;

        // Assert
        await world.BackfillStore.DidNotReceiveWithAnyArgs().FindResumePositionAsync(CancellationToken.None);
        Assert.Contains(
            world.Logger.Messages,
            message => message.Contains("embedding backfill is disabled", StringComparison.Ordinal));
    }

    /// <summary>
    /// The walk is a repeating sweep rather than one that finishes, so reaching the end starts another pass instead of
    /// ending the worker — which is what makes the promise that a refused call and a full live queue are reached later
    /// something this keeps.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_SweepReachesTheEnd_StartsAnotherSweepInsteadOfEnding()
    {
        // Arrange
        using var world = CreateWorld(new EmbeddingBackfillOptions());

        // Act
        await world.Worker.StartAsync(CancellationToken.None);
        await world.Logger.WaitForOccurrences("reached the end of the stored mail", occurrences: 1);
        await world.AdvanceUntilLogged("reached the end of the stored mail", occurrences: 2);

        // Assert
        Assert.False(world.Worker.ExecuteTask!.IsCompleted);
        await world.Worker.StopAsync(CancellationToken.None);
    }

    /// <summary>An instance that has activated no profile is a supported state, so it is reported without a warning and costs no walk.</summary>
    [Fact]
    public async Task ExecuteAsync_NoActiveProfile_ReportsItWithoutWalkingTheMail()
    {
        // Arrange
        using var world = CreateWorld(new EmbeddingBackfillOptions(), activeProfile: null);

        // Act
        await world.Worker.StartAsync(CancellationToken.None);
        await world.Logger.WaitForOccurrences("No embedding profile is active", occurrences: 1);
        await world.Worker.StopAsync(CancellationToken.None);

        // Assert
        await world.BackfillStore.DidNotReceiveWithAnyArgs().GetEmailsAwaitingEmbeddingAsync(
            Arg.Any<StoredEmailId?>(),
            Arg.Any<EmbeddingProfileId>(),
            Arg.Any<int>(),
            TestContext.Current.CancellationToken);
    }

    /// <summary>A failed run says nothing about whether messages remain, so the worker stays alive to resume next interval.</summary>
    [Fact]
    public async Task ExecuteAsync_RunFails_LogsItWithoutEndingTheWorker()
    {
        // Arrange
        using var world = CreateWorld(new EmbeddingBackfillOptions());
        world.BackfillStore
            .FindResumePositionAsync(Arg.Any<CancellationToken>())
            .Returns<StoredEmailId?>(_ => throw new InvalidOperationException("the database is unavailable"));

        // Act
        await world.Worker.StartAsync(CancellationToken.None);
        await world.Logger.WaitForOccurrences("backfill run failed", occurrences: 1);

        // Assert
        Assert.False(world.Worker.ExecuteTask!.IsCompleted);
        await world.Worker.StopAsync(CancellationToken.None);
    }

    /// <summary>A conflict with a competing writer is reported as a deferral rather than as a failure of the sweep.</summary>
    [Fact]
    public async Task ExecuteAsync_ConcurrencyConflict_ReportsADeferral()
    {
        // Arrange
        using var world = CreateWorld(new EmbeddingBackfillOptions());
        world.BackfillStore
            .FindResumePositionAsync(Arg.Any<CancellationToken>())
            .Returns<StoredEmailId?>(
                _ => throw new PersistenceConcurrencyConflictException("A competing writer won the race."));

        // Act
        await world.Worker.StartAsync(CancellationToken.None);
        await world.Logger.WaitForOccurrences("optimistic concurrency conflict", occurrences: 1);
        await world.Worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.Contains(
            world.Logger.Messages,
            message => message.Contains("optimistic concurrency conflict", StringComparison.Ordinal));
    }

    private static EmbeddingProfileIdentity CreateIdentity() =>
        EmbeddingProfileIdentity.Create(
            "a-provider",
            "a-model",
            modelVersion: null,
            dimension: 8,
            EmbeddingDistanceMetric.Cosine,
            EmbeddingInputPreparation.Create(2_000, passageInstruction: null, normalizesVector: true));

    private static WorkerWorld CreateWorld(EmbeddingBackfillOptions settings) =>
        CreateWorld(
            settings,
            new ActiveEmbeddingProfile(EmbeddingProfileId.Create(Guid.CreateVersion7()), CreateIdentity()));

    private static WorkerWorld CreateWorld(EmbeddingBackfillOptions settings, ActiveEmbeddingProfile? activeProfile)
    {
        settings.IdleSweepInterval = IdleSweepInterval;

        var world = new WorkerWorld();

        world.BackfillStore
            .GetEmailsAwaitingEmbeddingAsync(
                Arg.Any<StoredEmailId?>(),
                Arg.Any<EmbeddingProfileId>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<StoredEmailAwaitingEmbedding>>([]));

        var profileReader = Substitute.For<IActiveEmbeddingProfileReader>();
        profileReader.FindActiveProfileAsync(Arg.Any<CancellationToken>()).Returns(activeProfile);

        var textEmbeddingGenerator = Substitute.For<ITextEmbeddingGenerator>();
        textEmbeddingGenerator.Identity.Returns(CreateIdentity());
        textEmbeddingGenerator.MaximumPassagesPerCall.Returns(8);

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(world.TimeProvider);
        services.AddSingleton(world.BackfillStore);
        services.AddSingleton(profileReader);
        services.AddSingleton(Substitute.For<IEmailEmbeddingStore>());
        services.AddSingleton(textEmbeddingGenerator);
        services.AddSingleton(Substitute.For<IPersistenceSessionFactory>());
        services.AddSingleton(new PersistenceConcurrencyOptions());
        services.AddSingleton(new StoredEmailEmbeddingBackfillOptions
        {
            BatchSize = settings.BatchSize,
            MaxBatchesPerRun = settings.MaxBatchesPerRun,
        });
        services.AddScoped<OptimisticConcurrencyRetryPolicy>();
        services.AddScoped<StoredEmailEmbeddingGenerator>();
        services.AddScoped<StoredEmailEmbeddingBackfill>();

        var serviceProvider = services.BuildServiceProvider();
        world.Attach(
            serviceProvider,
            new MailEmbeddingBackfillWorker(
                serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                new EmailEmbeddingBackfillTelemetry(),
                Options.Create(settings),
                world.Logger,
                world.TimeProvider));

        return world;
    }

    /// <summary>The worker under test and the collaborators one run works against.</summary>
    private sealed class WorkerWorld : IDisposable
    {
        private ServiceProvider? serviceProvider;
        private MailEmbeddingBackfillWorker? worker;

        public AwaitingLogger<MailEmbeddingBackfillWorker> Logger { get; } = new();

        public FakeTimeProvider TimeProvider { get; } = new();

        public IStoredEmailEmbeddingBackfillStore BackfillStore { get; } =
            Substitute.For<IStoredEmailEmbeddingBackfillStore>();

        public MailEmbeddingBackfillWorker Worker => this.worker!;

        public void Attach(ServiceProvider provider, MailEmbeddingBackfillWorker attachedWorker)
        {
            this.serviceProvider = provider;
            this.worker = attachedWorker;
        }

        /// <summary>Moves the clock on until the worker has logged the message the given number of times.</summary>
        /// <remarks>
        /// A loop rather than a single advance, because a run's delay is created after the line that ends it is written:
        /// an advance that arrives before the delay exists is simply lost, and the next one fires it. What the loop
        /// proves is that the worker starts another sweep at all, which is the claim this worker's shape rests on.
        /// </remarks>
        public async Task AdvanceUntilLogged(string fragment, int occurrences)
        {
            const int advanceAttempts = 1000;

            var logged = this.Logger.WaitForOccurrences(fragment, occurrences);

            for (var attempt = 0; attempt < advanceAttempts && !logged.IsCompleted; attempt++)
            {
                this.TimeProvider.Advance(IdleSweepInterval);

                await Task.Yield();
            }

            await logged;
        }

        public void Dispose()
        {
            this.worker?.Dispose();
            this.serviceProvider?.Dispose();
        }
    }

    /// <summary>A logger a test can wait on, so an assertion never races the run that produces the line.</summary>
    /// <remarks>
    /// The recording logger beside this one answers what was written once a test already knows the work is done. A
    /// worker that never ends offers no such moment, so the log line itself is the signal here.
    /// </remarks>
    private sealed class AwaitingLogger<TCategory> : ILogger<TCategory>
    {
        private readonly Lock recordedMessages = new();
        private readonly List<string> messages = [];
        private readonly List<Expectation> expectations = [];

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (this.recordedMessages)
                {
                    return [.. this.messages];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            List<Expectation> satisfied;

            lock (this.recordedMessages)
            {
                this.messages.Add(formatter(state, exception));
                satisfied = [.. this.expectations.Where(this.IsSatisfied)];
                this.expectations.RemoveAll(satisfied.Contains);
            }

            // Completed outside the lock, so a continuation that logs cannot re-enter it.
            foreach (var expectation in satisfied)
            {
                expectation.Signal.TrySetResult();
            }
        }

        /// <summary>Waits until a message containing the fragment has been logged the given number of times.</summary>
        public Task WaitForOccurrences(string fragment, int occurrences)
        {
            var expectation = new Expectation(
                fragment,
                occurrences,
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

            lock (this.recordedMessages)
            {
                if (this.IsSatisfied(expectation))
                {
                    return Task.CompletedTask;
                }

                this.expectations.Add(expectation);
            }

            return expectation.Signal.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        }

        private bool IsSatisfied(Expectation expectation) => this.messages
            .Count(message => message.Contains(expectation.Fragment, StringComparison.Ordinal))
            >= expectation.Occurrences;

        private sealed record Expectation(string Fragment, int Occurrences, TaskCompletionSource Signal);
    }
}
