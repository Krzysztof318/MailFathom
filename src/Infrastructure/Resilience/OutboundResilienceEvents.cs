// Copyright © 2026 Krzysztof Kasprowicz

using Microsoft.Extensions.Logging;

namespace MailMcp.Infrastructure.Resilience;

/// <summary>Records what a resilience pipeline decided, without recording what the operation was carrying.</summary>
/// <remarks>
/// Polly's own log records render the outcome exception in full, and a mail server puts the rejected recipient into
/// its error text. These events therefore replace that output: a failure appears as its type name, and the dependency
/// class, attempt number, and delay carry everything an operator needs to see a dependency degrade. Durations and
/// outcome counts remain available as Polly's metrics, which are tagged rather than formatted and carry no message.
/// </remarks>
internal static partial class OutboundResilienceEvents
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Outbound dependency {OutboundDependency} failed with {FailureType} and will be retried as attempt {NextAttemptNumber} after {RetryDelay}.")]
    internal static partial void LogRetryScheduled(
        ILogger logger,
        string outboundDependency,
        string failureType,
        int nextAttemptNumber,
        TimeSpan retryDelay);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Error,
        Message = "Outbound dependency {OutboundDependency} exceeded its failure ratio after {FailureType}; further executions are rejected for {BreakDuration}.")]
    internal static partial void LogCircuitOpened(
        ILogger logger,
        string outboundDependency,
        string failureType,
        TimeSpan breakDuration);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Information,
        Message = "Outbound dependency {OutboundDependency} recovered and is accepting executions again.")]
    internal static partial void LogCircuitClosed(ILogger logger, string outboundDependency);
}
