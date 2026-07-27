// Copyright © 2026 Krzysztof Kasprowicz

using Microsoft.Extensions.Options;

namespace MailMcp.Infrastructure.Resilience;

/// <summary>Rejects a resilience budget whose limits contradict each other or leave an operation unbounded.</summary>
/// <remarks>
/// Data annotations bound each setting on its own; the rules here bound the combinations. A backoff ceiling larger
/// than the total timeout, or an attempt allowed to run longer than the operation it belongs to, describes a limit
/// that can never be reached — the pipeline would silently behave as if the smaller bound were the only one. Because
/// the validation runs on start, an operator learns that at deployment rather than during the first outage.
/// </remarks>
internal sealed class OutboundDependencyResilienceOptionsValidator : IValidateOptions<OutboundDependencyResilienceOptions>
{
    private static readonly TimeSpan LongestPermittedDuration = TimeSpan.FromDays(1);

    /// <summary>Validates the combination of limits configured for one dependency class.</summary>
    /// <param name="name">The named options instance, which is the dependency class name.</param>
    /// <param name="options">The bound options.</param>
    /// <returns>The aggregated result naming every contradiction found.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options" /> is <see langword="null" />.</exception>
    public ValidateOptionsResult Validate(string? name, OutboundDependencyResilienceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var contradictions = DescribeContradictions(name ?? string.Empty, options).ToArray();

        return contradictions.Length == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(contradictions);
    }

    private static IEnumerable<string> DescribeContradictions(string dependencyName, OutboundDependencyResilienceOptions options)
    {
        if (options.BaseDelay <= TimeSpan.Zero || options.BaseDelay > LongestPermittedDuration)
        {
            yield return Describe(dependencyName, $"{nameof(options.BaseDelay)} must be greater than zero and at most {LongestPermittedDuration}.");
        }

        if (options.MaxDelay < options.BaseDelay)
        {
            yield return Describe(dependencyName, $"{nameof(options.MaxDelay)} must not be shorter than {nameof(options.BaseDelay)}.");
        }

        if (options.AttemptTimeout <= TimeSpan.Zero || options.AttemptTimeout > LongestPermittedDuration)
        {
            yield return Describe(dependencyName, $"{nameof(options.AttemptTimeout)} must be greater than zero and at most {LongestPermittedDuration}.");
        }

        if (options.TotalTimeout <= TimeSpan.Zero || options.TotalTimeout > LongestPermittedDuration)
        {
            yield return Describe(dependencyName, $"{nameof(options.TotalTimeout)} must be greater than zero and at most {LongestPermittedDuration}.");
        }

        if (options.AttemptTimeout > options.TotalTimeout)
        {
            yield return Describe(dependencyName, $"{nameof(options.AttemptTimeout)} must not exceed {nameof(options.TotalTimeout)}, because an attempt that outlives its operation can never complete.");
        }

        if (options.MaxAttempts > 1 && options.MaxDelay > options.TotalTimeout)
        {
            yield return Describe(dependencyName, $"{nameof(options.MaxDelay)} must not exceed {nameof(options.TotalTimeout)}, because waiting that long would consume the whole budget before the retry runs.");
        }

        if (options.CircuitBreakerSamplingDuration < TimeSpan.FromSeconds(0.5) || options.CircuitBreakerSamplingDuration > LongestPermittedDuration)
        {
            yield return Describe(dependencyName, $"{nameof(options.CircuitBreakerSamplingDuration)} must be at least half a second and at most {LongestPermittedDuration}.");
        }

        if (options.CircuitBreakerBreakDuration < TimeSpan.FromSeconds(0.5) || options.CircuitBreakerBreakDuration > LongestPermittedDuration)
        {
            yield return Describe(dependencyName, $"{nameof(options.CircuitBreakerBreakDuration)} must be at least half a second and at most {LongestPermittedDuration}.");
        }
    }

    private static string Describe(string dependencyName, string contradiction) =>
        $"Resilience:{dependencyName} — {contradiction}";
}
