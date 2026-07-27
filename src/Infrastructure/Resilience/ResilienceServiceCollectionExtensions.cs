// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Resilience;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.DependencyInjection;
using Polly.Retry;
using Polly.Telemetry;
using Polly.Timeout;

namespace MailMcp.Infrastructure.Resilience;

/// <summary>Registers one named resilience pipeline per outbound dependency class.</summary>
public static class ResilienceServiceCollectionExtensions
{
    /// <summary>Registers the failure classifier, the pipeline of every dependency class, and the executor adapters run them through.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="resilienceConfiguration">The <c>Resilience</c> configuration section, whose children are named after <see cref="OutboundDependency" /> members.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> or <paramref name="resilienceConfiguration" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// Each class is bound as named options, so a deployment names only the limits it disagrees with and the rest
    /// come from the class defaults. Binding is strict: a misspelled key would otherwise be ignored and leave the
    /// operator convinced they had tuned a limit that never moved.
    /// </para>
    /// <para>
    /// A pipeline rebuilds itself when its options reload, which makes retry bounds and backoff adjustable without a
    /// restart. An operation already running keeps the pipeline it started under, because a budget swapped mid-flight
    /// would apply half of one configuration and half of another.
    /// </para>
    /// <para>
    /// This registration covers non-HTTP dependencies only. <c>HttpClient</c> traffic is already wrapped once by the
    /// standard resilience handler in the host's service defaults, and adding a pipeline around an HTTP-based
    /// provider client would place two retry layers on one call.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddOutboundResiliencePipelines(
        this IServiceCollection services,
        IConfiguration resilienceConfiguration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(resilienceConfiguration);

        services.AddSingleton<ITransientFailureClassifier, TransientFailureClassifier>();
        services.AddSingleton<IValidateOptions<OutboundDependencyResilienceOptions>, OutboundDependencyResilienceOptionsValidator>();
        services.AddSingleton<OutboundOperationExecutor>();

        foreach (var dependency in Enum.GetValues<OutboundDependency>())
        {
            var dependencyName = dependency.ToString();

            services.AddOptions<OutboundDependencyResilienceOptions>(dependencyName)
                .Configure(options => OutboundDependencyResilienceDefaults.ApplyTo(options, dependency))
                .Bind(
                    resilienceConfiguration.GetSection(dependencyName),
                    binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddResiliencePipeline(
                dependency,
                (builder, context) => ComposePipeline(builder, context, dependency));
        }

        return services;
    }

    /// <summary>Composes the strategies of one dependency class from outermost to innermost.</summary>
    /// <remarks>
    /// The order matches the shape the standard HTTP pipeline established, and each position is a consequence of the
    /// one before it. Load shedding comes first so work beyond the concurrency limit is rejected before it consumes a
    /// timeout. The total timeout then bounds the whole operation including its backoff waits, which is the only
    /// limit that can bound a retrying operation at all. Retry sits inside it, the circuit breaker inside retry so it
    /// observes every attempt rather than every operation, and the per-attempt timeout innermost so a stalled attempt
    /// becomes a transient failure the retry above it can act on.
    /// </remarks>
    private static void ComposePipeline(
        ResiliencePipelineBuilder builder,
        AddResiliencePipelineContext<OutboundDependency> context,
        OutboundDependency dependency)
    {
        var dependencyName = dependency.ToString();

        context.EnableReloads<OutboundDependencyResilienceOptions>(dependencyName);

        var options = context.GetOptions<OutboundDependencyResilienceOptions>(dependencyName);
        var classifier = context.ServiceProvider.GetRequiredService<ITransientFailureClassifier>();
        var logger = context.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(OutboundResilienceEvents));

        builder.TimeProvider = context.ServiceProvider.GetRequiredService<TimeProvider>();
        // Polly's own log records render the outcome exception in full, which for a mail server means the rejected
        // recipient reaches the log. Its metering stays on; the events below replace its logging.
        builder.ConfigureTelemetry(new TelemetryOptions { LoggerFactory = NullLoggerFactory.Instance });

        builder.AddConcurrencyLimiter(options.ConcurrencyLimit);
        builder.AddTimeout(options.TotalTimeout);

        if (options.MaxAttempts > 1)
        {
            builder.AddRetry(ComposeRetryStrategy(options, dependency, dependencyName, classifier, logger));
        }

        builder.AddCircuitBreaker(ComposeCircuitBreakerStrategy(options, dependency, dependencyName, classifier, logger));
        builder.AddTimeout(options.AttemptTimeout);
    }

    private static RetryStrategyOptions ComposeRetryStrategy(
        OutboundDependencyResilienceOptions options,
        OutboundDependency dependency,
        string dependencyName,
        ITransientFailureClassifier classifier,
        ILogger logger) =>
        new()
        {
            // The configured count includes the first call, which the strategy counts separately from its retries.
            MaxRetryAttempts = options.MaxAttempts - 1,
            Delay = options.BaseDelay,
            MaxDelay = options.MaxDelay,
            BackoffType = DelayBackoffType.Exponential,
            // Jitter spreads the retries of concurrent operations that failed together, so a recovering server is not
            // hit by every one of them at the same instant.
            UseJitter = true,
            ShouldHandle = arguments => ValueTask.FromResult(
                IsWorthAnotherAttempt(dependency, arguments.Outcome.Exception, classifier)),
            OnRetry = arguments =>
            {
                OutboundResilienceEvents.LogRetryScheduled(
                    logger,
                    dependencyName,
                    DescribeFailureType(arguments.Outcome.Exception),
                    arguments.AttemptNumber + 2,
                    arguments.RetryDelay);

                return default;
            },
        };

    private static CircuitBreakerStrategyOptions ComposeCircuitBreakerStrategy(
        OutboundDependencyResilienceOptions options,
        OutboundDependency dependency,
        string dependencyName,
        ITransientFailureClassifier classifier,
        ILogger logger) =>
        new()
        {
            FailureRatio = options.CircuitBreakerFailureRatio,
            MinimumThroughput = options.CircuitBreakerMinimumThroughput,
            SamplingDuration = options.CircuitBreakerSamplingDuration,
            BreakDuration = options.CircuitBreakerBreakDuration,
            ShouldHandle = arguments => ValueTask.FromResult(
                IsWorthAnotherAttempt(dependency, arguments.Outcome.Exception, classifier)),
            OnOpened = arguments =>
            {
                OutboundResilienceEvents.LogCircuitOpened(
                    logger,
                    dependencyName,
                    DescribeFailureType(arguments.Outcome.Exception),
                    arguments.BreakDuration);

                return default;
            },
            OnClosed = _ =>
            {
                OutboundResilienceEvents.LogCircuitClosed(logger, dependencyName);

                return default;
            },
        };

    /// <summary>Decides whether one failed attempt should be repeated and whether it counts against the dependency's health.</summary>
    /// <remarks>
    /// A per-attempt timeout is the pipeline's own verdict that the dependency stopped responding, so it is repeated
    /// like any other transient failure. An open circuit or a shed execution is not the dependency failing at all: it
    /// is this process declining to call it, and repeating that decision would only spend the operation's budget
    /// waiting for a limit that is already deliberate.
    /// </remarks>
    private static bool IsWorthAnotherAttempt(
        OutboundDependency dependency,
        Exception? failure,
        ITransientFailureClassifier classifier) => failure switch
        {
            null => false,
            TimeoutRejectedException => true,
            ExecutionRejectedException => false,
            _ => classifier.IsTransientFailure(dependency, failure),
        };

    private static string DescribeFailureType(Exception? failure) =>
        failure?.GetType().Name ?? "no exception";
}
