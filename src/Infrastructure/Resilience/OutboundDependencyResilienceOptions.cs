// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Resilience;

namespace MailFathom.Infrastructure.Resilience;

/// <summary>Configures the resilience pipeline of one <see cref="OutboundDependency" />.</summary>
/// <remarks>
/// <para>
/// One instance is bound per dependency class as named options, where the name is the enumeration member. Retry
/// bounds and backoff are therefore an operator setting rather than a code constant, and a flaky dependency can be
/// tuned without a rebuild.
/// </para>
/// <para>
/// Every limit is bounded on both sides. An unbounded attempt count or an absent total timeout would turn a
/// struggling mail server into an outage of unlimited duration, so <see cref="OutboundDependencyResilienceOptionsValidator" />
/// rejects such a configuration at startup rather than at the first failure.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class OutboundDependencyResilienceOptions
{
    /// <summary>Gets or sets the maximum number of attempts for one logical operation, including the first attempt.</summary>
    /// <remarks>A value of <c>1</c> disables retry entirely and leaves the timeout, circuit-breaker, and concurrency strategies in place.</remarks>
    [Range(1, 10)]
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Gets or sets the delay before the first retry, from which the exponential backoff grows.</summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Gets or sets the ceiling an exponentially growing retry delay is capped at.</summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets the time one attempt may take before it is abandoned and counted as a transient failure.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets the time the whole operation may take, including every retry and every backoff delay.</summary>
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Gets or sets the proportion of failed executions within one sampling window that opens the circuit.</summary>
    [Range(0.01, 1.0)]
    public double CircuitBreakerFailureRatio { get; set; } = 0.5;

    /// <summary>Gets or sets the minimum number of executions a sampling window must observe before the failure ratio is considered.</summary>
    /// <remarks>Below this count the ratio is ignored, so a single early failure cannot open the circuit for the whole deployment.</remarks>
    [Range(2, 1000)]
    public int CircuitBreakerMinimumThroughput { get; set; } = 10;

    /// <summary>Gets or sets the window over which the failure ratio is measured.</summary>
    public TimeSpan CircuitBreakerSamplingDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets how long an open circuit rejects executions before it admits one trial execution.</summary>
    public TimeSpan CircuitBreakerBreakDuration { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Gets or sets the number of executions of this dependency class allowed to run at the same time.</summary>
    /// <remarks>The limiter is the backpressure boundary: work beyond the limit is rejected rather than queued, so a slow dependency cannot accumulate unbounded in-flight operations.</remarks>
    [Range(1, 1000)]
    public int ConcurrencyLimit { get; set; } = 8;
}
