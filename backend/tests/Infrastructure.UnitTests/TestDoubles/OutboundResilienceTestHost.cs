// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using MailFathom.Application.Resilience;
using MailFathom.Infrastructure.Resilience;
using MailFathom.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MailFathom.Infrastructure.UnitTests.TestDoubles;

/// <summary>Builds a container holding the real pipelines over a clock the test controls.</summary>
internal sealed class OutboundResilienceTestHost : IDisposable
{
    /// <summary>How many turns the loop offers the scheduler while nothing is waiting on the clock yet.</summary>
    /// <remarks>
    /// One yield is not enough: a pipeline reaches the wait it will sit on several thread-pool hops after the call
    /// that started it, and a clock moved before then measures a delay nobody had armed. A batch of turns settles
    /// that, and the loop stops spending them as soon as something is waiting on the clock.
    /// </remarks>
    private const int SchedulerTurnsBeforeAStep = 32;

    /// <summary>How long the work of one step may take to surface before the execution is reported as hung.</summary>
    private static readonly TimeSpan ObservationBound = TimeSpan.FromSeconds(30);

    private readonly ServiceProvider services;
    private readonly IConfigurationRoot configuration;
    private readonly MemoryConfigurationProvider configuredSettings;

    private OutboundResilienceTestHost(IEnumerable<KeyValuePair<string, string?>> configuredSettings)
    {
        this.configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configuredSettings)
            .Build();
        this.configuredSettings = this.configuration.Providers.OfType<MemoryConfigurationProvider>().Single();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<TimeProvider>(this.TimeProvider);
        serviceCollection.AddLogging(logging => logging
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(this.Logs));
        serviceCollection.AddOutboundResiliencePipelines(this.configuration.GetSection("Resilience"));

        this.services = serviceCollection.BuildServiceProvider();
    }

    internal WaitRecordingTimeProvider TimeProvider { get; } = new();

    internal RecordingLoggerProvider Logs { get; } = new();

    internal IServiceProvider Services => this.services;

    internal OutboundOperationExecutor Executor => this.services.GetRequiredService<OutboundOperationExecutor>();

    internal ITransientFailureClassifier TransientFailureClassifier => this.services.GetRequiredService<ITransientFailureClassifier>();

    /// <summary>Builds a host whose configuration overrides the named settings, given as <c>Dependency:Setting</c> pairs.</summary>
    internal static OutboundResilienceTestHost WithConfiguredSettings(params (string SettingPath, string Value)[] configuredSettings) =>
        new(configuredSettings.Select(setting =>
            new KeyValuePair<string, string?>($"Resilience:{setting.SettingPath}", setting.Value)));

    /// <summary>Replaces one configured setting and reloads the configuration, as an operator editing a file would.</summary>
    internal void ReloadWithSetting(string settingPath, string value)
    {
        this.configuredSettings.Set($"Resilience:{settingPath}", value);
        this.configuration.Reload();
    }

    /// <summary>Runs an execution to completion on virtual time, so a backoff of minutes costs a test no real delay.</summary>
    /// <remarks>
    /// <para>
    /// The clock is advanced in steps rather than in one jump because each step must let the pipeline observe the
    /// previous one: an operation resumes, fails again, and schedules its next wait between two advances. The step
    /// therefore also sets how precisely a recorded delay can be measured.
    /// </para>
    /// <para>
    /// A step that brought a wait due is not taken as observed until the execution has answered it, by finishing or by
    /// arming the wait it will sit on next. The work a due wait starts runs on the thread pool, so a loaded runner
    /// answers later rather than differently: the loop holds the clock and takes its turn behind that work instead of
    /// running ahead into a later budget and letting that expire first. What a step guarantees is therefore the answer
    /// to the step before it, never a fixed amount of scheduling.
    /// </para>
    /// <para>
    /// Nothing here waits on the real clock: the loop spends scheduler turns, and elapsed real time is read only to
    /// end an execution that has stopped answering. Both bounds are escapes from a hang rather than part of any
    /// behavior a test asserts on, and both end such an execution as a failure naming what it was doing rather than as
    /// a suite that times out.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the execution stops answering the clock, or does not complete within <paramref name="maximumAdvances" />.
    /// </exception>
    internal async Task CompleteOnVirtualTimeAsync(
        Task execution,
        TimeSpan advanceStep,
        int maximumAdvances = 20000)
    {
        for (var advance = 0; advance < maximumAdvances && !execution.IsCompleted; advance++)
        {
            for (var turn = 0;
                turn < SchedulerTurnsBeforeAStep
                    && !execution.IsCompleted
                    && this.TimeProvider.OutstandingWaits == 0;
                turn++)
            {
                await Task.Yield();
            }

            var waitsArmedBeforeTheStep = this.TimeProvider.ScheduledWaits;
            var waitsElapsedBeforeTheStep = this.TimeProvider.ElapsedWaits;

            this.TimeProvider.Advance(advanceStep);

            if (this.TimeProvider.ElapsedWaits != waitsElapsedBeforeTheStep)
            {
                await this.ObserveTheStepAsync(execution, waitsArmedBeforeTheStep, advanceStep);
            }
        }

        if (!execution.IsCompleted)
        {
            throw new InvalidOperationException(
                $"The execution did not complete within {maximumAdvances} advances of {advanceStep} on virtual time.");
        }

        await execution;
    }

    /// <summary>Runs a result-producing execution to completion on virtual time.</summary>
    internal async Task<TResult> CompleteOnVirtualTimeAsync<TResult>(
        Task<TResult> execution,
        TimeSpan advanceStep,
        int maximumAdvances = 20000)
    {
        await this.CompleteOnVirtualTimeAsync((Task)execution, advanceStep, maximumAdvances);

        return await execution;
    }

    /// <summary>Holds the clock until the execution has answered the wait that just came due.</summary>
    /// <remarks>
    /// An answer is the execution finishing or a new wait being armed, which is every way a pipeline reacts to a
    /// budget expiring. Waiting for one costs turns of the scheduler rather than time on any clock, and the elapsed
    /// real time is read for one purpose only: ending an execution that has stopped answering as a failure.
    /// </remarks>
    private async Task ObserveTheStepAsync(Task execution, long waitsArmedBeforeTheStep, TimeSpan advanceStep)
    {
        var startedWaiting = Stopwatch.GetTimestamp();

        bool Answered() =>
            execution.IsCompleted || this.TimeProvider.ScheduledWaits != waitsArmedBeforeTheStep;

        while (!Answered())
        {
            if (Stopwatch.GetElapsedTime(startedWaiting) > ObservationBound)
            {
                throw new InvalidOperationException(
                    $"The execution did not answer a wait that came due within {ObservationBound}, so the clock was "
                    + $"held at {this.TimeProvider.GetUtcNow():O} rather than advanced by another {advanceStep}.");
            }

            await TakeATurnBehindTheQueuedWorkAsync();
        }
    }

    /// <summary>Returns through the pool's global queue, so the work already queued runs before the loop looks again.</summary>
    /// <remarks>
    /// <see cref="Task.Yield" /> is the wrong primitive to wait on here, and it is the one the whole race came from:
    /// it requeues the continuation on the pumping thread's own queue, which that thread serves before it takes
    /// anything else, so a loop built on it keeps handing itself the turns it is waiting for the chain to use. Queuing
    /// without that preference puts the loop behind the work already waiting for a thread instead, which is what makes
    /// a saturated pool slow the loop down rather than starve the chain under it.
    /// </remarks>
    private static Task TakeATurnBehindTheQueuedWorkAsync()
    {
        var resumed = new TaskCompletionSource();

        ThreadPool.UnsafeQueueUserWorkItem(
            static waiting => waiting.SetResult(),
            resumed,
            preferLocal: false);

        return resumed.Task;
    }

    public void Dispose()
    {
        this.services.Dispose();
        this.Logs.Dispose();
    }
}
