// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Application.Jobs.Scheduling;
using MailFathom.Domain.Accounts;
using MailFathom.Host.Configuration.Jobs;
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

public sealed class JobWorkerTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
    private const int BatchSize = 2;

    /// <summary>An operator who wants a replica serving MCP reads and nothing else gets exactly that.</summary>
    [Fact]
    public async Task ExecuteAsync_TheWorkerSwitchedOff_ClaimsNothingAndEnds()
    {
        // Arrange
        using var world = CreateWorld(new JobQueueOptions { Enabled = false }, WithAHandler);

        // Act
        await world.Worker.StartAsync(TestContext.Current.CancellationToken);
        await world.Worker.ExecuteTask!;

        // Assert
        await world.Store.DidNotReceiveWithAnyArgs().ClaimAsync(
            Arg.Any<JobClaimRequest>(),
            Arg.Any<CancellationToken>());
        Assert.Contains(
            world.Logger.Messages,
            message => message.Contains("durable job worker is switched off", StringComparison.Ordinal));
    }

    /// <summary>
    /// A build whose consumers have not arrived can run no declared type, and a claim filtered to none of them would be
    /// taking work it would have to hand straight back.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_NoRegisteredHandler_SaysSoAndClaimsNothing()
    {
        // Arrange
        using var world = CreateWorld(new JobQueueOptions(), WithNoHandler);

        // Act
        await world.Worker.StartAsync(TestContext.Current.CancellationToken);
        await world.Worker.ExecuteTask!;

        // Assert
        await world.Store.DidNotReceiveWithAnyArgs().ClaimAsync(
            Arg.Any<JobClaimRequest>(),
            Arg.Any<CancellationToken>());
        Assert.Contains(
            world.Logger.Messages,
            message => message.Contains("No job handler is registered", StringComparison.Ordinal));
    }

    /// <summary>
    /// A queue with work in it is drained rather than polled, so a pass that filled its batch is followed by another at
    /// once. The clock is deliberately never advanced here: a worker that waited would never reach the second claim.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_APassThatFilledItsBatch_ClaimsAgainWithoutWaitingOutTheInterval()
    {
        // Arrange
        using var world = CreateWorld(new JobQueueOptions { BatchSize = BatchSize }, WithAHandler);
        var claimedAgain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var claimCount = 0;

        world.Store.ClaimAsync(Arg.Any<JobClaimRequest>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            claimCount++;

            if (claimCount > 1)
            {
                claimedAgain.TrySetResult();

                return Task.FromResult<IReadOnlyList<LeasedJob>>([]);
            }

            return Task.FromResult<IReadOnlyList<LeasedJob>>(
                [.. Enumerable.Range(0, BatchSize).Select(LeasedJobFor)]);
        });
        world.Store
            .CompleteAsync(Arg.Any<JobId>(), Arg.Any<JobLeaseOwner>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await world.Worker.StartAsync(TestContext.Current.CancellationToken);
        await claimedAgain.Task;

        // Assert
        Assert.False(world.Worker.ExecuteTask!.IsCompleted);
        await world.Worker.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>What a report of a job names is the queue's own vocabulary, so no subject or address can reach a log line.</summary>
    [Fact]
    public async Task ExecuteAsync_AJobItRan_ReportsItByTypeAndAttempt()
    {
        // Arrange
        using var world = CreateWorld(new JobQueueOptions { BatchSize = BatchSize }, WithAHandler);

        world.Store
            .ClaimAsync(Arg.Any<JobClaimRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<LeasedJob>>([LeasedJobFor(0)]));
        world.Store
            .CompleteAsync(Arg.Any<JobId>(), Arg.Any<JobLeaseOwner>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await world.Worker.StartAsync(TestContext.Current.CancellationToken);
        await world.Logger.WaitForOccurrences(
            $"Ran a {JobType.ClassifyEmailSpam.Name} job",
            occurrences: 1,
            TestContext.Current.CancellationToken);
        await world.Worker.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(
            world.Logger.Messages,
            message => message.Contains("account-a", StringComparison.Ordinal));
    }

    /// <summary>
    /// A database that is briefly unavailable says nothing about whether there is work to do, and anything the pass had
    /// claimed is claimable again once its lease expires, so the worker stays alive.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_AClaimThatFails_LogsItWithoutEndingTheWorker()
    {
        // Arrange
        using var world = CreateWorld(new JobQueueOptions(), WithAHandler);

        world.Store
            .ClaimAsync(Arg.Any<JobClaimRequest>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<LeasedJob>>(_ => throw new InvalidOperationException("the database is unavailable"));

        // Act
        await world.Worker.StartAsync(TestContext.Current.CancellationToken);
        await world.Logger.WaitForOccurrences(
            "durable job pass failed",
            occurrences: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(world.Worker.ExecuteTask!.IsCompleted);
        await world.Worker.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>An instance that starts with a backlog publishes it before it begins draining, not one interval later.</summary>
    [Fact]
    public async Task ExecuteAsync_TheFirstPass_MeasuresTheQueueDepthOfEveryTypeItCanRun()
    {
        // Arrange
        using var world = CreateWorld(new JobQueueOptions(), WithAHandler);
        var measured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        world.DepthReader
            .ReadWaitingDepthsAsync(Arg.Any<IReadOnlyList<JobType>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                measured.TrySetResult();

                return Task.FromResult<IReadOnlyList<JobQueueDepthReading>>([]);
            });

        // Act
        await world.Worker.StartAsync(TestContext.Current.CancellationToken);
        await measured.Task;
        await world.Worker.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        await world.DepthReader.Received().ReadWaitingDepthsAsync(
            Arg.Is<IReadOnlyList<JobType>>(types => types != null && types.Contains(JobType.ClassifyEmailSpam)),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Nothing about running the work depends on the measurement, so a failed one leaves the last published depth where
    /// it was and the pass behind it claims exactly as it would have.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_AQueueDepthMeasurementThatFails_StillClaimsAndKeepsTheWorkerAlive()
    {
        // Arrange
        using var world = CreateWorld(new JobQueueOptions(), WithAHandler);
        var claimed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        world.Store
            .ClaimAsync(Arg.Any<JobClaimRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                claimed.TrySetResult();

                return Task.FromResult<IReadOnlyList<LeasedJob>>([]);
            });
        world.DepthReader
            .ReadWaitingDepthsAsync(Arg.Any<IReadOnlyList<JobType>>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<JobQueueDepthReading>>(_ =>
                throw new InvalidOperationException("the database is unavailable"));

        // Act
        await world.Worker.StartAsync(TestContext.Current.CancellationToken);
        await world.Logger.WaitForOccurrences(
            "could not be measured",
            occurrences: 1,
            TestContext.Current.CancellationToken);
        await claimed.Task;

        // Assert
        await world.Store.Received().ClaimAsync(Arg.Any<JobClaimRequest>(), Arg.Any<CancellationToken>());
        Assert.False(world.Worker.ExecuteTask!.IsCompleted);
        await world.Worker.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>A schedule is dispatched by the worker that already runs, which is what keeps it out of a loop of its own.</summary>
    [Fact]
    public async Task ExecuteAsync_TheFirstPass_DispatchesTheSchedulesThisInstanceDeclares()
    {
        // Arrange
        using var world = CreateWorld(new JobQueueOptions(), WithAHandler);
        world.Schedules.ReadSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ScheduledJob>>(
                [ScheduledJobFor("mail-rules:work:housekeeping")]));

        // Act
        await world.Worker.StartAsync(TestContext.Current.CancellationToken);
        await world.Logger.WaitForOccurrences(
            "seen for the first time",
            occurrences: 1,
            TestContext.Current.CancellationToken);
        await world.Worker.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        var seeded = Assert.Single(world.ScheduleStore.Saves);
        Assert.Equal("mail-rules:work:housekeeping", seeded.Id.Value);
        Assert.Null(seeded.LastOccurrenceAt);
    }

    /// <summary>A dispatch that could not read its schedules advances nothing and leaves the worker claiming.</summary>
    [Fact]
    public async Task ExecuteAsync_ARecurringDispatchThatFails_LogsItWithoutEndingTheWorker()
    {
        // Arrange
        using var world = CreateWorld(new JobQueueOptions(), WithAHandler);
        var claimed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        world.Store
            .ClaimAsync(Arg.Any<JobClaimRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                claimed.TrySetResult();

                return Task.FromResult<IReadOnlyList<LeasedJob>>([]);
            });
        world.Schedules
            .ReadSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<ScheduledJob>>>(
                _ => throw new InvalidOperationException("the rules are unreadable"));

        // Act
        await world.Worker.StartAsync(TestContext.Current.CancellationToken);
        await world.Logger.WaitForOccurrences(
            "no schedule advanced",
            occurrences: 1,
            TestContext.Current.CancellationToken);
        await claimed.Task;

        // Assert
        await world.Store.Received().ClaimAsync(Arg.Any<JobClaimRequest>(), Arg.Any<CancellationToken>());
        Assert.False(world.Worker.ExecuteTask!.IsCompleted);
        await world.Worker.StopAsync(TestContext.Current.CancellationToken);
    }

    private static ScheduledJob ScheduledJobFor(string identity)
    {
        Assert.True(JobRecurrence.TryParse("Daily at 03:00", out var recurrence, out _));

        return new ScheduledJob(
            JobScheduleId.Create(identity),
            RunScheduledMailRulesJobPayload.For(
                MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("work"))),
            recurrence!);
    }

    private static LeasedJob LeasedJobFor(int index) => new(
        JobId.Create(Guid.CreateVersion7(Noon.AddSeconds(index))),
        JobType.ClassifyEmailSpam,
        JobIdempotencyKey.Create($"account-a/inbox/1/{index}"),
        new ClassifyEmailSpamJobPayload
        {
            OwnerId = SyntheticMailOwner.Deployment.Value,
            AccountId = "account-a",
            FolderAlias = "inbox",
            FolderResolutionGeneration = 1,
            UidValidity = 1,
            Uid = (uint)(index + 1),
        },
        AccountId: null,
        AttemptCount: 1,
        new JobLease(JobLeaseOwner.Create("attempt-a"), Noon.AddMinutes(10)),
        EnqueuedTrace: null);

    private static void WithAHandler(IServiceCollection services) =>
        services.AddSingleton<IJobHandler>(new NoOpJobHandler());

    private static void WithNoHandler(IServiceCollection services)
    {
        // Registering nothing is the arrangement: the container answers an empty enumerable, exactly as a build with no
        // consumer does.
    }

    private static WorkerWorld CreateWorld(JobQueueOptions settings, Action<IServiceCollection> registerHandlers)
    {
        var world = new WorkerWorld();

        world.Store
            .ClaimAsync(Arg.Any<JobClaimRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<LeasedJob>>([]));
        world.DepthReader
            .ReadWaitingDepthsAsync(Arg.Any<IReadOnlyList<JobType>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<JobQueueDepthReading>>([]));
        world.Schedules.ReadSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ScheduledJob>>([]));

        var queueTelemetry = new JobQueueTelemetry();

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(world.TimeProvider);
        services.AddSingleton(world.Store);
        services.AddSingleton(JobExecutionSettings.Create(
            settings.BatchSize,
            settings.LeaseDuration,
            settings.ExecutionTimeout,
            settings.MaxAttempts,
            settings.RetryBaseDelay,
            settings.RetryMaxDelay));
        services.AddSingleton(JobCapacitySettings.Create(
            settings.MaxConcurrentJobs,
            settings.MaxConcurrentJobsPerType,
            settings.MaxQueueDepthPerType));
        services.AddSingleton(Substitute.For<IJobFailureClassifier>());
        services.AddSingleton(world.DepthReader);
        services.AddSingleton(world.Schedules);
        services.AddSingleton<IJobScheduleStore>(world.ScheduleStore);
        services.AddScoped<JobSchedulePass>();
        services.AddSingleton<JobConcurrencyGate>();

        // The one instance both the worker and the attempt runner report through, so the span an attempt opens and the
        // measurement the pass records come from the same publisher rather than from two that share a meter.
        services.AddSingleton(queueTelemetry);
        services.AddSingleton<IJobAttemptRunner, ScopedJobAttemptRunner>();
        services.AddScoped<JobHandlerRegistry>();
        services.AddScoped<JobExecutor>();
        services.AddScoped<JobQueuePass>();
        registerHandlers(services);

        var serviceProvider = services.BuildServiceProvider();

        world.Attach(
            serviceProvider,
            new JobWorker(
                serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(settings),
                queueTelemetry,
                world.Logger,
                world.TimeProvider));

        return world;
    }

    /// <summary>The durable state of the schedules this worker dispatched, kept where a test can read it.</summary>
    internal sealed class RecordingJobScheduleStore : IJobScheduleStore
    {
        private readonly Dictionary<string, JobScheduleState> states = new(StringComparer.Ordinal);
        private readonly List<JobScheduleState> saves = [];

        public IReadOnlyList<JobScheduleState> Saves => this.saves;

        public Task<IReadOnlyDictionary<string, JobScheduleState>> ReadAsync(
            IReadOnlyCollection<JobScheduleId> ids,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, JobScheduleState>>(
                ids.Select(id => this.states.GetValueOrDefault(id.Value))
                    .OfType<JobScheduleState>()
                    .ToDictionary(state => state.Id.Value, StringComparer.Ordinal));

        public Task SaveAsync(JobScheduleState state, CancellationToken cancellationToken)
        {
            this.states[state.Id.Value] = state;
            this.saves.Add(state);

            return Task.CompletedTask;
        }
    }

    /// <summary>A handler that does nothing, because these tests are about the loop rather than about any work.</summary>
    private sealed class NoOpJobHandler : IJobHandler
    {
        public JobType JobType => JobType.ClassifyEmailSpam;

        public Task RunAsync(IJobPayload payload, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>The worker under test and the collaborators one pass works against.</summary>
    private sealed class WorkerWorld : IDisposable
    {
        private ServiceProvider? serviceProvider;
        private JobWorker? worker;

        public AwaitingLogger<JobWorker> Logger { get; } = new();

        public FakeTimeProvider TimeProvider { get; } = new(Noon);

        public IJobStore Store { get; } = Substitute.For<IJobStore>();

        public IJobQueueDepthReader DepthReader { get; } = Substitute.For<IJobQueueDepthReader>();

        public IScheduledJobSource Schedules { get; } = Substitute.For<IScheduledJobSource>();

        public RecordingJobScheduleStore ScheduleStore { get; } = new();

        public JobWorker Worker => this.worker!;

        public void Attach(ServiceProvider provider, JobWorker attachedWorker)
        {
            this.serviceProvider = provider;
            this.worker = attachedWorker;
        }

        public void Dispose()
        {
            this.worker?.Dispose();
            this.serviceProvider?.Dispose();
        }
    }
}
