// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Resilience;
using MailFathom.Infrastructure.Resilience;
using MailFathom.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace MailFathom.Infrastructure.UnitTests;

/// <summary>Builds a container holding the real pipelines over a clock the test controls.</summary>
internal sealed class OutboundResilienceTestHost : IDisposable
{
    /// <summary>How many times the pumping loop offers the scheduler a turn before it moves the clock again.</summary>
    /// <remarks>
    /// One yield is not enough. An abandoned attempt resumes through a cancellation callback several thread-pool hops
    /// away, and a loop that yields once per advance keeps requeueing itself ahead of that chain and runs the clock
    /// past every remaining limit. Offering a batch of turns lets the chain finish while the clock stands still.
    /// </remarks>
    private const int SchedulerTurnsPerAdvance = 32;

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

    internal FakeTimeProvider TimeProvider { get; } = new();

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
    /// Nothing here waits on the real clock. Between advances the loop only offers the scheduler turns, so a test
    /// costs what its continuations cost and never a fixed delay per step. Both bounds are escapes from a hang rather
    /// than part of any behavior a test asserts on.
    /// </para>
    /// </remarks>
    internal async Task CompleteOnVirtualTimeAsync(
        Task execution,
        TimeSpan advanceStep,
        int maximumAdvances = 20000)
    {
        for (var advance = 0; advance < maximumAdvances && !execution.IsCompleted; advance++)
        {
            for (var turn = 0; turn < SchedulerTurnsPerAdvance && !execution.IsCompleted; turn++)
            {
                await Task.Yield();
            }

            this.TimeProvider.Advance(advanceStep);
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

    public void Dispose()
    {
        this.services.Dispose();
        this.Logs.Dispose();
    }
}
