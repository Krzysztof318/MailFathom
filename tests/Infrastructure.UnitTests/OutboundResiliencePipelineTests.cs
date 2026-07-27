// Copyright © 2026 Krzysztof Kasprowicz

using MailKit.Net.Imap;
using MailKit.Security;
using MailMcp.Application.Resilience;
using MailMcp.Infrastructure.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Xunit;

namespace MailMcp.Infrastructure.UnitTests;

public sealed class OutboundResiliencePipelineTests
{
    private static readonly TimeSpan FineAdvanceStep = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// A budget the pumping loop cannot consume. The loop advances virtual time until the execution completes, so a
    /// test that asserts anything other than the total timeout must keep that limit beyond the loop's reach: at its
    /// coarsest step the loop can move the clock by hours, and a smaller budget would be tripped by the pumping
    /// rather than by the behavior under test.
    /// </summary>
    private const string UnreachableTotalTimeout = "1.00:00:00";

    [Fact]
    public async Task ExecuteAsync_RepeatedTransientFailure_StopsAtTheConfiguredAttemptCap()
    {
        // Arrange
        using var host = OutboundResilienceTestHost.WithConfiguredSettings(
            ("MailboxDataRetrieval:MaxAttempts", "3"),
            ("MailboxDataRetrieval:BaseDelay", "00:00:01"),
            ("MailboxDataRetrieval:MaxDelay", "00:00:04"),
            ("MailboxDataRetrieval:TotalTimeout", UnreachableTotalTimeout));
        var attempts = 0;

        // Act
        var execution = host.Executor.ExecuteAsync(
            OutboundDependency.MailboxDataRetrieval,
            _ =>
            {
                attempts++;

                throw new ImapProtocolException("The server closed the stream.");
            },
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ImapProtocolException>(
            () => host.CompleteOnVirtualTimeAsync(execution, FineAdvanceStep));

        // Assert
        Assert.Equal(3, attempts);
    }

    /// <summary>A rejected credential repeated against a mail server is what locks a mailbox account.</summary>
    [Fact]
    public async Task ExecuteAsync_TerminalFailure_IsNotRepeated()
    {
        // Arrange
        using var host = OutboundResilienceTestHost.WithConfiguredSettings(
            ("MailboxSessionEstablishment:MaxAttempts", "5"));
        var attempts = 0;

        // Act
        var execution = host.Executor.ExecuteAsync(
            OutboundDependency.MailboxSessionEstablishment,
            _ =>
            {
                attempts++;

                throw new AuthenticationException("The server rejected the credential.");
            },
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<AuthenticationException>(
            () => host.CompleteOnVirtualTimeAsync(execution, FineAdvanceStep));

        // Assert
        Assert.Equal(1, attempts);
    }

    /// <summary>Later waits must dominate earlier ones; comparing halves keeps the proof immune to individual jittered samples.</summary>
    [Fact]
    public async Task ExecuteAsync_ExponentialBackoff_WaitsLongerAsAttemptsAccumulate()
    {
        // Arrange
        using var host = OutboundResilienceTestHost.WithConfiguredSettings(
            ("MailboxDataRetrieval:MaxAttempts", "9"),
            ("MailboxDataRetrieval:BaseDelay", "00:00:01"),
            ("MailboxDataRetrieval:MaxDelay", "00:30:00"),
            ("MailboxDataRetrieval:TotalTimeout", UnreachableTotalTimeout),
            ("MailboxDataRetrieval:AttemptTimeout", "00:10:00"));

        // Act
        var waits = await this.MeasureWaitsBetweenAttemptsAsync(host, OutboundDependency.MailboxDataRetrieval, TimeSpan.FromSeconds(1));

        // Assert
        Assert.Equal(8, waits.Count);
        Assert.True(
            waits.TakeLast(4).Sum(wait => wait.TotalSeconds) > waits.Take(4).Sum(wait => wait.TotalSeconds),
            $"Waits did not grow: [{string.Join(", ", waits)}].");
    }

    [Fact]
    public async Task ExecuteAsync_JitteredBackoff_NeverWaitsLongerThanTheConfiguredCeiling()
    {
        // Arrange
        var ceiling = TimeSpan.FromSeconds(2);
        using var host = OutboundResilienceTestHost.WithConfiguredSettings(
            ("MailboxDataRetrieval:MaxAttempts", "8"),
            ("MailboxDataRetrieval:BaseDelay", "00:00:01"),
            ("MailboxDataRetrieval:MaxDelay", "00:00:02"),
            ("MailboxDataRetrieval:TotalTimeout", UnreachableTotalTimeout));

        // Act
        var waits = await this.MeasureWaitsBetweenAttemptsAsync(host, OutboundDependency.MailboxDataRetrieval, FineAdvanceStep);

        // Assert
        Assert.All(waits, wait => Assert.InRange(wait, TimeSpan.Zero, ceiling + FineAdvanceStep));
    }

    /// <summary>The total timeout is the only limit that can bound an operation whose attempts are separated by waits.</summary>
    [Fact]
    public async Task ExecuteAsync_TotalTimeoutElapsed_AbandonsTheRemainingAttempts()
    {
        // Arrange
        using var host = OutboundResilienceTestHost.WithConfiguredSettings(
            ("EmailDelivery:MaxAttempts", "10"),
            ("EmailDelivery:BaseDelay", "00:00:02"),
            ("EmailDelivery:MaxDelay", "00:00:04"),
            ("EmailDelivery:TotalTimeout", "00:00:10"),
            ("EmailDelivery:AttemptTimeout", "00:00:05"));
        var attempts = 0;

        // Act
        var execution = host.Executor.ExecuteAsync(
            OutboundDependency.EmailDelivery,
            _ =>
            {
                attempts++;

                throw new ImapProtocolException("The server closed the stream.");
            },
            TestContext.Current.CancellationToken);

        // The budget runs out while the pipeline is waiting to retry rather than inside an attempt, so the retry stops
        // and reports the failure that caused the last attempt to fail. A budget that runs out during an attempt
        // surfaces TimeoutRejectedException instead, which is what the stalled-attempt test covers.
        await Assert.ThrowsAsync<ImapProtocolException>(
            () => host.CompleteOnVirtualTimeAsync(execution, FineAdvanceStep));

        // Assert
        Assert.InRange(attempts, 1, 9);
    }

    /// <summary>A stalled attempt has to become a failure the retry above it can act on, or the operation waits for its total timeout instead.</summary>
    [Fact]
    public async Task ExecuteAsync_StalledAttempt_IsAbandonedAndRepeated()
    {
        // Arrange
        using var host = OutboundResilienceTestHost.WithConfiguredSettings(
            ("MailboxDataRetrieval:MaxAttempts", "2"),
            ("MailboxDataRetrieval:BaseDelay", "00:00:01"),
            ("MailboxDataRetrieval:MaxDelay", "00:00:02"),
            ("MailboxDataRetrieval:AttemptTimeout", "00:00:05"),
            ("MailboxDataRetrieval:TotalTimeout", UnreachableTotalTimeout));
        var attempts = 0;

        // Act
        var execution = host.Executor.ExecuteAsync(
            OutboundDependency.MailboxDataRetrieval,
            async attemptToken =>
            {
                attempts++;

                if (attempts == 1)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, attemptToken);
                }

                return attempts;
            },
            TestContext.Current.CancellationToken);

        var completedAttempt = await host.CompleteOnVirtualTimeAsync(execution, TimeSpan.FromSeconds(1));

        // Assert
        Assert.Equal(2, completedAttempt);
    }

    [Fact]
    public async Task ExecuteAsync_FailureRatioExceeded_RejectsFurtherExecutionsUntilTheBreakElapses()
    {
        // Arrange
        using var host = BuildHostWithCircuitBreakerOnly();
        await FailUntilTheCircuitOpensAsync(host);

        // Act, Assert
        await Assert.ThrowsAsync<BrokenCircuitException>(
            () => host.Executor.ExecuteAsync(
                OutboundDependency.DatabaseCommandExecution,
                _ => Task.FromResult(1),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteAsync_TrialExecutionAfterTheBreak_ClosesTheCircuitAgain()
    {
        // Arrange
        using var host = BuildHostWithCircuitBreakerOnly();
        await FailUntilTheCircuitOpensAsync(host);

        // Act
        host.TimeProvider.Advance(TimeSpan.FromSeconds(11));
        var halfOpenResult = await host.Executor.ExecuteAsync(
            OutboundDependency.DatabaseCommandExecution,
            _ => Task.FromResult(1),
            TestContext.Current.CancellationToken);

        // Assert
        var afterRecovery = await host.Executor.ExecuteAsync(
            OutboundDependency.DatabaseCommandExecution,
            _ => Task.FromResult(2),
            TestContext.Current.CancellationToken);
        Assert.Equal(1, halfOpenResult);
        Assert.Equal(2, afterRecovery);
    }

    /// <summary>Work beyond the limit is shed rather than queued, so a slow dependency cannot accumulate in-flight operations.</summary>
    [Fact]
    public async Task ExecuteAsync_ConcurrencyLimitReached_ShedsTheAdditionalExecution()
    {
        // Arrange
        using var host = OutboundResilienceTestHost.WithConfiguredSettings(
            ("MailboxSessionEstablishment:ConcurrencyLimit", "1"));
        var occupied = new TaskCompletionSource();
        var occupyingExecution = host.Executor.ExecuteAsync(
            OutboundDependency.MailboxSessionEstablishment,
            async _ =>
            {
                await occupied.Task;

                return 1;
            },
            TestContext.Current.CancellationToken);

        // Act, Assert
        await Assert.ThrowsAsync<RateLimiterRejectedException>(
            () => host.Executor.ExecuteAsync(
                OutboundDependency.MailboxSessionEstablishment,
                _ => Task.FromResult(2),
                TestContext.Current.CancellationToken));

        occupied.SetResult();
        Assert.Equal(1, await occupyingExecution);
    }

    /// <summary>Two retry layers around one call multiply their attempt counts into a storm nobody configured.</summary>
    [Fact]
    public async Task ExecuteAsync_SameDependencyNested_FailsInsteadOfRetryingAtTwoLayers()
    {
        // Arrange
        using var host = OutboundResilienceTestHost.WithConfiguredSettings();

        // Act, Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.Executor.ExecuteAsync(
                OutboundDependency.MailboxDataRetrieval,
                outerToken => host.Executor.ExecuteAsync(
                    OutboundDependency.MailboxDataRetrieval,
                    _ => Task.FromResult(1),
                    outerToken),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteAsync_DifferentDependencyNested_IsAllowedBecauseEachCallHasOneLayer()
    {
        // Arrange
        using var host = OutboundResilienceTestHost.WithConfiguredSettings();

        // Act
        var result = await host.Executor.ExecuteAsync(
            OutboundDependency.MailboxDataRetrieval,
            outerToken => host.Executor.ExecuteAsync(
                OutboundDependency.DatabaseCommandExecution,
                _ => Task.FromResult(7),
                outerToken),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(7, result);
    }

    /// <summary>The nesting guard must not survive the operation that set it, or the second call of an ordinary loop would fail.</summary>
    [Fact]
    public async Task ExecuteAsync_SameDependencyOneAfterAnother_IsAllowed()
    {
        // Arrange
        using var host = OutboundResilienceTestHost.WithConfiguredSettings();

        // Act
        await host.Executor.ExecuteAsync(
            OutboundDependency.MailboxDataRetrieval,
            _ => Task.CompletedTask,
            TestContext.Current.CancellationToken);
        await host.Executor.ExecuteAsync(
            OutboundDependency.MailboxDataRetrieval,
            _ => Task.CompletedTask,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(host.Logs.Records, record => record.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownDependency_ResolvesNoPipelineInsteadOfRunningUnprotected()
    {
        // Arrange
        using var host = OutboundResilienceTestHost.WithConfiguredSettings();

        // Act, Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => host.Executor.ExecuteAsync(
                (OutboundDependency)99,
                _ => Task.FromResult(1),
                TestContext.Current.CancellationToken));
    }

    /// <summary>A mail server puts the rejected recipient into its error text, which must not reach a log record.</summary>
    [Fact]
    public async Task ExecuteAsync_RetriedFailure_RecordsTheFailureTypeWithoutTheServersMessage()
    {
        // Arrange
        using var host = OutboundResilienceTestHost.WithConfiguredSettings(
            ("MailboxDataRetrieval:MaxAttempts", "2"),
            ("MailboxDataRetrieval:BaseDelay", "00:00:01"),
            ("MailboxDataRetrieval:MaxDelay", "00:00:02"),
            ("MailboxDataRetrieval:TotalTimeout", UnreachableTotalTimeout));

        // Act
        var execution = host.Executor.ExecuteAsync(
            OutboundDependency.MailboxDataRetrieval,
            _ => throw new ImapProtocolException("Mailbox reader@example.com is gone."),
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ImapProtocolException>(
            () => host.CompleteOnVirtualTimeAsync(execution, FineAdvanceStep));

        // Assert
        var records = host.Logs.Records;
        Assert.Contains(
            records,
            record => record.Message.Contains(nameof(OutboundDependency.MailboxDataRetrieval), StringComparison.Ordinal)
                && record.Message.Contains(nameof(ImapProtocolException), StringComparison.Ordinal));
        Assert.DoesNotContain(records, record => record.Message.Contains("reader@example.com", StringComparison.Ordinal));
        Assert.DoesNotContain(records, record => record.Failure is not null);
    }

    [Fact]
    public void AddOutboundResiliencePipelines_ContradictoryConfiguration_FailsStartup()
    {
        // Arrange
        using var host = OutboundResilienceTestHost.WithConfiguredSettings(
            ("EmailDelivery:AttemptTimeout", "00:10:00"),
            ("EmailDelivery:TotalTimeout", "00:01:00"));

        // Act, Assert
        Assert.Throws<OptionsValidationException>(
            () => host.Services.GetRequiredService<IStartupValidator>().Validate());
    }

    [Fact]
    public void AddOutboundResiliencePipelines_AttemptCountBeyondItsRange_FailsStartup()
    {
        // Arrange
        using var host = OutboundResilienceTestHost.WithConfiguredSettings(("EmailDelivery:MaxAttempts", "99"));

        // Act, Assert
        Assert.Throws<OptionsValidationException>(
            () => host.Services.GetRequiredService<IStartupValidator>().Validate());
    }

    /// <summary>A misspelled key that bound silently would leave an operator convinced they had tuned a limit that never moved.</summary>
    [Fact]
    public void AddOutboundResiliencePipelines_MisspelledSetting_FailsStartup()
    {
        // Arrange
        using var host = OutboundResilienceTestHost.WithConfiguredSettings(("EmailDelivery:MaxAttemps", "2"));

        // Act, Assert
        Assert.Throws<InvalidOperationException>(
            () => host.Services.GetRequiredService<IStartupValidator>().Validate());
    }

    /// <summary>Strict binding inspects keys inside a section it was pointed at, so a misspelled section is invisible to it.</summary>
    [Fact]
    public void AddOutboundResiliencePipelines_SectionNamingNoDependency_FailsBeforeTheHostStarts()
    {
        // Act, Assert
        var failure = Assert.Throws<InvalidOperationException>(
            () => OutboundResilienceTestHost.WithConfiguredSettings(("EmailDelivry:MaxAttempts", "2")));
        Assert.Contains("EmailDelivry", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A timeout the strategy rejects would otherwise start the host and fail at the dependency's first use.</summary>
    [Theory]
    [InlineData("AttemptTimeout")]
    [InlineData("TotalTimeout")]
    public void AddOutboundResiliencePipelines_TimeoutBelowTheStrategyMinimum_FailsStartup(string settingName)
    {
        // Arrange
        using var host = OutboundResilienceTestHost.WithConfiguredSettings(
            ($"MailboxDataRetrieval:{settingName}", "00:00:00.001"));

        // Act, Assert
        Assert.Throws<OptionsValidationException>(
            () => host.Services.GetRequiredService<IStartupValidator>().Validate());
    }

    [Fact]
    public void AddOutboundResiliencePipelines_CircuitWindowBelowTheStrategyMinimum_FailsStartup()
    {
        // Arrange
        using var host = OutboundResilienceTestHost.WithConfiguredSettings(
            ("MailboxDataRetrieval:CircuitBreakerBreakDuration", "00:00:00.500"));

        // Act, Assert
        Assert.Throws<OptionsValidationException>(
            () => host.Services.GetRequiredService<IStartupValidator>().Validate());
    }

    /// <summary>
    /// A malformed runtime edit must neither throw on the thread that reported it nor disarm a dependency that is
    /// already serving. Listening for reloads would do both, because the options monitor materializes and validates
    /// the candidate inside the change notification.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_AfterAnInvalidReload_KeepsServingUnderTheStartupBudget()
    {
        // Arrange
        using var host = OutboundResilienceTestHost.WithConfiguredSettings(
            ("MailboxDataRetrieval:MaxAttempts", "2"),
            ("MailboxDataRetrieval:BaseDelay", "00:00:01"),
            ("MailboxDataRetrieval:MaxDelay", "00:00:02"),
            ("MailboxDataRetrieval:TotalTimeout", UnreachableTotalTimeout));
        await host.Executor.ExecuteAsync(
            OutboundDependency.MailboxDataRetrieval,
            _ => Task.CompletedTask,
            TestContext.Current.CancellationToken);

        // Act
        host.ReloadWithSetting("MailboxDataRetrieval:MaxAttempts", "99");

        // Assert
        var attempts = 0;
        var execution = host.Executor.ExecuteAsync(
            OutboundDependency.MailboxDataRetrieval,
            _ =>
            {
                attempts++;

                throw new ImapProtocolException("The server closed the stream.");
            },
            TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<ImapProtocolException>(
            () => host.CompleteOnVirtualTimeAsync(execution, FineAdvanceStep));
        Assert.Equal(2, attempts);
    }

    /// <summary>The budgets are restart-required, so even a valid edit is deliberately not adopted in place.</summary>
    [Fact]
    public async Task ExecuteAsync_AfterAValidReload_KeepsTheStartupBudgetUntilRestart()
    {
        // Arrange
        using var host = OutboundResilienceTestHost.WithConfiguredSettings(
            ("MailboxDataRetrieval:MaxAttempts", "2"),
            ("MailboxDataRetrieval:BaseDelay", "00:00:01"),
            ("MailboxDataRetrieval:MaxDelay", "00:00:02"),
            ("MailboxDataRetrieval:TotalTimeout", UnreachableTotalTimeout));
        await host.Executor.ExecuteAsync(
            OutboundDependency.MailboxDataRetrieval,
            _ => Task.CompletedTask,
            TestContext.Current.CancellationToken);

        // Act
        host.ReloadWithSetting("MailboxDataRetrieval:MaxAttempts", "4");

        // Assert
        var attempts = 0;
        var execution = host.Executor.ExecuteAsync(
            OutboundDependency.MailboxDataRetrieval,
            _ =>
            {
                attempts++;

                throw new ImapProtocolException("The server closed the stream.");
            },
            TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<ImapProtocolException>(
            () => host.CompleteOnVirtualTimeAsync(execution, FineAdvanceStep));
        Assert.Equal(2, attempts);
    }

    /// <summary>A delivery repeated as freely as a mailbox read is visible in the recipient's inbox.</summary>
    [Fact]
    public void AddOutboundResiliencePipelines_NoConfiguration_GivesEachDependencyItsOwnBudget()
    {
        // Arrange
        using var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var options = host.Services.GetRequiredService<IOptionsMonitor<OutboundDependencyResilienceOptions>>();

        // Act
        var deliveryAttempts = options.Get(nameof(OutboundDependency.EmailDelivery)).MaxAttempts;
        var databaseBaseDelay = options.Get(nameof(OutboundDependency.DatabaseCommandExecution)).BaseDelay;

        // Assert
        Assert.Equal(2, deliveryAttempts);
        Assert.Equal(TimeSpan.FromMilliseconds(200), databaseBaseDelay);
    }

    [Fact]
    public void AddOutboundResiliencePipelines_ConfiguredSetting_OverridesOnlyThatSetting()
    {
        // Arrange
        using var host = OutboundResilienceTestHost.WithConfiguredSettings(("EmailDelivery:MaxAttempts", "4"));
        var options = host.Services.GetRequiredService<IOptionsMonitor<OutboundDependencyResilienceOptions>>();

        // Act
        var delivery = options.Get(nameof(OutboundDependency.EmailDelivery));

        // Assert
        Assert.Equal(4, delivery.MaxAttempts);
        Assert.Equal(TimeSpan.FromSeconds(5), delivery.BaseDelay);
    }

    private static OutboundResilienceTestHost BuildHostWithCircuitBreakerOnly() =>
        OutboundResilienceTestHost.WithConfiguredSettings(
            ("DatabaseCommandExecution:MaxAttempts", "1"),
            ("DatabaseCommandExecution:CircuitBreakerMinimumThroughput", "2"),
            ("DatabaseCommandExecution:CircuitBreakerFailureRatio", "0.5"),
            ("DatabaseCommandExecution:CircuitBreakerSamplingDuration", "00:00:30"),
            ("DatabaseCommandExecution:CircuitBreakerBreakDuration", "00:00:10"));

    private static async Task FailUntilTheCircuitOpensAsync(OutboundResilienceTestHost host)
    {
        for (var failure = 0; failure < 2; failure++)
        {
            await Assert.ThrowsAsync<TimeoutException>(
                () => host.Executor.ExecuteAsync(
                    OutboundDependency.DatabaseCommandExecution,
                    _ => throw new TimeoutException("The command did not complete."),
                    TestContext.Current.CancellationToken));
        }
    }

    /// <summary>Runs an always-failing operation and reports the virtual time observed between consecutive attempts.</summary>
    private async Task<IReadOnlyList<TimeSpan>> MeasureWaitsBetweenAttemptsAsync(
        OutboundResilienceTestHost host,
        OutboundDependency dependency,
        TimeSpan advanceStep)
    {
        var attemptTimes = new List<DateTimeOffset>();

        var execution = host.Executor.ExecuteAsync(
            dependency,
            _ =>
            {
                attemptTimes.Add(host.TimeProvider.GetUtcNow());

                throw new ImapProtocolException("The server closed the stream.");
            },
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ImapProtocolException>(
            () => host.CompleteOnVirtualTimeAsync(execution, advanceStep));

        return [.. attemptTimes.Zip(attemptTimes.Skip(1), (earlier, later) => later - earlier)];
    }
}
