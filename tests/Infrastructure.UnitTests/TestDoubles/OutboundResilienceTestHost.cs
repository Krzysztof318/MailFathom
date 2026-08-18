// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
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
    /// <summary>How many times the pumping loop offers the scheduler a turn before it waits on the real clock instead.</summary>
    /// <remarks>
    /// One yield is not enough. An abandoned attempt resumes through a cancellation callback several thread-pool hops
    /// away, and a loop that yields once per advance keeps requeueing itself ahead of that chain. A batch of turns
    /// settles the common case without leaving the pumping thread parked, which is why it comes before the waits.
    /// </remarks>
    private const int SchedulerTurnsPerObservation = 32;

    /// <summary>How often the loop looks again once its scheduler turns are spent.</summary>
    private static readonly TimeSpan ObservationPollInterval = TimeSpan.FromMilliseconds(2);

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
    /// answers later rather than differently: the loop holds the clock and waits on the real one instead of running
    /// ahead into a later budget and letting that expire first. What a step guarantees is therefore the answer to the
    /// step before it, never a fixed amount of scheduling.
    /// </para>
    /// <para>
    /// Both bounds are escapes from a hang rather than part of any behavior a test asserts on, and both end an
    /// execution that stops answering as a failure naming what it was doing rather than as a suite that times out.
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
                turn < SchedulerTurnsPerObservation
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
    /// budget expiring. Scheduler turns come first because a thread pool with room answers within a few of them; the
    /// polling that follows is what a saturated pool needs, and it parks the pumping thread rather than competing with
    /// the chain it is waiting for.
    /// </remarks>
    private async Task ObserveTheStepAsync(Task execution, long waitsArmedBeforeTheStep, TimeSpan advanceStep)
    {
        var startedWaiting = Stopwatch.GetTimestamp();

        bool Answered() =>
            execution.IsCompleted || this.TimeProvider.ScheduledWaits != waitsArmedBeforeTheStep;

        for (var turn = 0; turn < SchedulerTurnsPerObservation && !Answered(); turn++)
        {
            await Task.Yield();
        }

        while (!Answered())
        {
            if (Stopwatch.GetElapsedTime(startedWaiting) > ObservationBound)
            {
                throw new InvalidOperationException(
                    $"The execution did not answer a wait that came due within {ObservationBound}, so the clock was "
                    + $"held at {this.TimeProvider.GetUtcNow():O} rather than advanced by another {advanceStep}.");
            }

            await Task.Delay(ObservationPollInterval);
        }
    }

    public void Dispose()
    {
        this.services.Dispose();
        this.Logs.Dispose();
    }
}
