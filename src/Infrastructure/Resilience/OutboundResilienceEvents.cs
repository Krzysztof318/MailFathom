// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.Extensions.Logging;

namespace MailFathom.Infrastructure.Resilience;

/// <summary>Records what a resilience pipeline decided, without recording what the operation was carrying.</summary>
/// <remarks>
/// Polly's own log records render the outcome exception in full, and a mail server puts the rejected recipient into
/// its error text. These events therefore replace that output: a failure appears as its type name, and the dependency
/// class, the remote instance, the operation, the attempt number, and the delay carry everything an operator needs to
/// see a dependency degrade. The instance is a configured account identifier and the operation a folder name — both
/// deployment vocabulary rather than mailbox contents. Durations and outcome counts remain available as Polly's
/// metrics, which are tagged rather than formatted and carry no message.
/// </remarks>
internal static partial class OutboundResilienceEvents
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Outbound dependency {OutboundDependency} instance {DependencyInstance} failed operation {OutboundOperation} with {FailureType} and will retry as attempt {NextAttemptNumber} after {RetryDelay}.")]
    internal static partial void LogRetryScheduled(
        ILogger logger,
        string outboundDependency,
        string dependencyInstance,
        string outboundOperation,
        string failureType,
        int nextAttemptNumber,
        TimeSpan retryDelay);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Error,
        Message = "Outbound dependency {OutboundDependency} instance {DependencyInstance} exceeded its failure ratio after {FailureType}; further executions are rejected for {BreakDuration}.")]
    internal static partial void LogCircuitOpened(
        ILogger logger,
        string outboundDependency,
        string dependencyInstance,
        string failureType,
        TimeSpan breakDuration);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Information,
        Message = "Outbound dependency {OutboundDependency} instance {DependencyInstance} recovered and is accepting executions again.")]
    internal static partial void LogCircuitClosed(
        ILogger logger,
        string outboundDependency,
        string dependencyInstance);
}
