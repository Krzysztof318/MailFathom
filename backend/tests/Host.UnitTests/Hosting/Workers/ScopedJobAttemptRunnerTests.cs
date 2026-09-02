// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Common.Observability;
using MailFathom.Host.Hosting.Workers;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Observability;
using MailFathom.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Workers;

public sealed class ScopedJobAttemptRunnerTests : IDisposable
{
    private const string EnqueuedTraceId = "4bf92f3577b34da6a3ce929d0e0e4736";
    private const string EnqueuedSpanId = "1a2b3c4d5e6f7081";
    private const string EnqueuedTraceParent = $"00-{EnqueuedTraceId}-{EnqueuedSpanId}-01";

    private static readonly DateTimeOffset Noon = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly ConcurrentQueue<IJobStore> resolvedStores = new();
    private readonly ConcurrentQueue<string?> spansTheAttemptRanInside = new();
    private readonly ActivityListener listener;

    // Without a listener that samples, the source starts no activity at all, so the nesting this class asserts would
    // be indistinguishable from a runner that opened no span.
    public ScopedJobAttemptRunnerTests() => this.listener = SampledMailFathomSpans.Sampling();

    public void Dispose() => this.listener.Dispose();

    /// <summary>
    /// Two attempts running at once must not write through one persistence session, so each is given a scope of its
    /// own. Reading the store each attempt resolved is how that is observable: one scope would hand both the same one.
    /// </summary>
    [Fact]
    public async Task RunAsync_TwoAttempts_GivesEachOneItsOwnScope()
    {
        // Arrange
        await using var services = this.ComposedServices();
        var runner = new ScopedJobAttemptRunner(
            services.GetRequiredService<IServiceScopeFactory>(),
            new JobQueueTelemetry());

        // Act
        await runner.RunAsync(LeasedJobFor(1), CancellationToken.None);
        await runner.RunAsync(LeasedJobFor(2), CancellationToken.None);

        // Assert
        Assert.Equal(2, this.resolvedStores.Distinct().Count());
    }

    /// <summary>The attempt still reports what it did, because the scope is all this adds to running one.</summary>
    [Fact]
    public async Task RunAsync_AJobNoHandlerRuns_ReportsWhatTheAttemptDid()
    {
        // Arrange
        await using var services = this.ComposedServices();
        var runner = new ScopedJobAttemptRunner(
            services.GetRequiredService<IServiceScopeFactory>(),
            new JobQueueTelemetry());
        var job = LeasedJobFor(3);

        // Act
        var result = await runner.RunAsync(job, CancellationToken.None);

        // Assert
        Assert.Equal(job.JobId, result.JobId);
        Assert.Equal(JobExecutionOutcome.HandlerMissing, result.Outcome);
    }

    /// <summary>Everything the attempt reaches for runs inside the attempt's own span, which is what makes it a parent.</summary>
    /// <remarks>
    /// Asserted from inside the scope rather than from the published span, because a span that was opened and closed
    /// around nothing would look identical from outside. What the defect this guards against looks like is a database
    /// command with no parent, so the claim has to be about what was current while the work ran.
    /// </remarks>
    [Fact]
    public async Task RunAsync_AnAttempt_RunsTheWorkInsideTheAttemptSpan()
    {
        // Arrange
        await using var services = this.ComposedServices();
        var runner = new ScopedJobAttemptRunner(
            services.GetRequiredService<IServiceScopeFactory>(),
            new JobQueueTelemetry());

        // Act
        await runner.RunAsync(LeasedJobFor(4), CancellationToken.None);

        // Assert
        Assert.Equal([JobQueueTelemetry.AttemptSpanName], this.spansTheAttemptRanInside);
    }

    /// <summary>
    /// The trace a job was enqueued inside reaches the attempt through this runner and nowhere else, so a parameter
    /// dropped here would leave every attempt unlinked while the telemetry below it went on passing its own tests.
    /// </summary>
    /// <remarks>
    /// The link is found by the span identity this test minted rather than by being the only one published, because
    /// the activity source is the process's and another class may be running an attempt at the same moment.
    /// </remarks>
    [Fact]
    public async Task RunAsync_AJobEnqueuedInsideATrace_LinksTheAttemptToThatTrace()
    {
        // Arrange
        var enqueuedContexts = new ConcurrentQueue<ActivityContext>();
        using var attemptSpans = new ActivityListener
        {
            ShouldListenTo = source => StringComparer.Ordinal.Equals(source.Name, Telemetry.Name),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (!StringComparer.Ordinal.Equals(activity.OperationName, JobQueueTelemetry.AttemptSpanName))
                {
                    return;
                }

                foreach (var link in activity.Links)
                {
                    enqueuedContexts.Enqueue(link.Context);
                }
            },
        };

        ActivitySource.AddActivityListener(attemptSpans);

        await using var services = this.ComposedServices();
        var runner = new ScopedJobAttemptRunner(
            services.GetRequiredService<IServiceScopeFactory>(),
            new JobQueueTelemetry());
        var job = LeasedJobFor(5) with
        {
            EnqueuedTrace = JobTraceContext.FromTraceParent(EnqueuedTraceParent, traceState: null),
        };

        // Act
        await runner.RunAsync(job, CancellationToken.None);

        // Assert
        var linked = Assert.Single(
            enqueuedContexts,
            context => StringComparer.Ordinal.Equals(context.SpanId.ToHexString(), EnqueuedSpanId));

        Assert.Equal(EnqueuedTraceId, linked.TraceId.ToHexString());
        Assert.True(linked.IsRemote);
    }

    /// <summary>The one job whose row records nothing to link to, which is every row written before the columns existed.</summary>
    [Fact]
    public async Task RunAsync_AJobWhoseRowRecordsNoTrace_OpensTheAttemptWithNoLink()
    {
        // Arrange
        var linkCounts = new ConcurrentQueue<int>();
        using var attemptSpans = new ActivityListener
        {
            ShouldListenTo = source => StringComparer.Ordinal.Equals(source.Name, Telemetry.Name),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (StringComparer.Ordinal.Equals(activity.OperationName, JobQueueTelemetry.AttemptSpanName))
                {
                    linkCounts.Enqueue(activity.Links.Count());
                }
            },
        };

        ActivitySource.AddActivityListener(attemptSpans);

        await using var services = this.ComposedServices();
        var runner = new ScopedJobAttemptRunner(
            services.GetRequiredService<IServiceScopeFactory>(),
            new JobQueueTelemetry());

        // Act
        await runner.RunAsync(LeasedJobFor(6), CancellationToken.None);

        // Assert
        Assert.All(linkCounts, linkCount => Assert.Equal(0, linkCount));
        Assert.NotEmpty(linkCounts);
    }

    private static LeasedJob LeasedJobFor(int uid) => new(
        JobId.Create(Guid.CreateVersion7(Noon.AddSeconds(uid))),
        JobType.ClassifyEmailSpam,
        JobIdempotencyKey.Create($"account-a/inbox/1/{uid}"),
        new ClassifyEmailSpamJobPayload
        {
            OwnerId = SyntheticMailOwner.Deployment.Value,
            AccountId = "account-a",
            FolderAlias = "inbox",
            FolderResolutionGeneration = 1,
            UidValidity = 1,
            Uid = (uint)uid,
        },
        AccountId: null,
        AttemptCount: 1,
        new JobLease(JobLeaseOwner.Create("attempt-a"), Noon.AddMinutes(5)),
        EnqueuedTrace: null);

    /// <summary>Composes the smallest container an attempt resolves out of: a scoped store, and the executor over it.</summary>
    private ServiceProvider ComposedServices()
    {
        var services = new ServiceCollection();

        services.AddScoped(_ =>
        {
            var store = Substitute.For<IJobStore>();

            store
                .DeadLetterAsync(
                    Arg.Any<JobId>(),
                    Arg.Any<JobLeaseOwner>(),
                    Arg.Any<JobFailureRecord>(),
                    Arg.Any<CancellationToken>())
                .Returns(true);

            this.resolvedStores.Enqueue(store);
            this.spansTheAttemptRanInside.Enqueue(Activity.Current?.OperationName);

            return store;
        });
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Noon));
        services.AddSingleton(Substitute.For<IJobFailureClassifier>());
        services.AddSingleton(JobExecutionSettings.Create(
            batchSize: 5,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(2),
            maxAttempts: 5,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(30)));
        services.AddScoped<JobHandlerRegistry>();
        services.AddScoped<JobExecutor>();

        return services.BuildServiceProvider();
    }
}
