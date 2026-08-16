// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;
using MailFathom.Host.Hosting.Workers;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Workers;

public sealed class ScopedJobAttemptRunnerTests : IDisposable
{
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

    private static LeasedJob LeasedJobFor(int uid) => new(
        JobId.Create(Guid.CreateVersion7(Noon.AddSeconds(uid))),
        JobType.ClassifyEmailSpam,
        JobIdempotencyKey.Create($"account-a/inbox/1/{uid}"),
        new EmailOccurrenceJobPayload
        {
            AccountId = "account-a",
            FolderAlias = "inbox",
            FolderResolutionGeneration = 1,
            UidValidity = 1,
            Uid = (uint)uid,
        },
        AccountId: null,
        AttemptCount: 1,
        new JobLease(JobLeaseOwner.Create("attempt-a"), Noon.AddMinutes(5)));

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
