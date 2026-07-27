// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Infrastructure.Resilience;
using MailMcp.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace MailMcp.Infrastructure.UnitTests;

/// <summary>Builds a container holding the real pipelines over a clock the test controls.</summary>
internal sealed class OutboundResilienceTestHost : IDisposable
{
    private static readonly TimeSpan OneSchedulerTick = TimeSpan.FromMilliseconds(1);

    private readonly ServiceProvider services;

    private OutboundResilienceTestHost(IEnumerable<KeyValuePair<string, string?>> configuredSettings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configuredSettings)
            .Build();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<TimeProvider>(this.TimeProvider);
        serviceCollection.AddLogging(logging => logging
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(this.Logs));
        serviceCollection.AddOutboundResiliencePipelines(configuration.GetSection("Resilience"));

        this.services = serviceCollection.BuildServiceProvider();
    }

    internal FakeTimeProvider TimeProvider { get; } = new();

    internal RecordingLoggerProvider Logs { get; } = new();

    internal IServiceProvider Services => this.services;

    internal OutboundOperationExecutor Executor => this.services.GetRequiredService<OutboundOperationExecutor>();

    /// <summary>Builds a host whose configuration overrides the named settings, given as <c>Dependency:Setting</c> pairs.</summary>
    internal static OutboundResilienceTestHost WithConfiguredSettings(params (string SettingPath, string Value)[] configuredSettings) =>
        new(configuredSettings.Select(setting =>
            new KeyValuePair<string, string?>($"Resilience:{setting.SettingPath}", setting.Value)));

    /// <summary>Runs an execution to completion on virtual time, so a backoff of minutes costs a test no real delay.</summary>
    /// <remarks>
    /// <para>
    /// The clock is advanced in steps rather than in one jump because each step must let the pipeline observe the
    /// previous one: an operation resumes, fails again, and schedules its next wait between two advances. The step
    /// therefore also sets how precisely a recorded delay can be measured.
    /// </para>
    /// <para>
    /// Each iteration waits for the execution or for one scheduler tick, whichever comes first. Yielding alone is not
    /// enough: an abandoned attempt resumes through a cancellation callback several thread-pool hops away, and a loop
    /// that only yields keeps requeueing itself ahead of it and advances the clock past every remaining limit. The
    /// tick is a scheduling concession, never a wait for anything a test asserts on, because every assertion is made
    /// against the virtual clock.
    /// </para>
    /// </remarks>
    internal async Task CompleteOnVirtualTimeAsync(
        Task execution,
        TimeSpan advanceStep,
        int maximumAdvances = 20000)
    {
        for (var advance = 0; advance < maximumAdvances && !execution.IsCompleted; advance++)
        {
            await Task.WhenAny(execution, Task.Delay(OneSchedulerTick));

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
