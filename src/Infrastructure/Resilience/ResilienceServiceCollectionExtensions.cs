// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Resilience;
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

namespace MailFathom.Infrastructure.Resilience;

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
    /// A budget is read once, at startup. The settings are restart-required: a reloaded candidate is not adopted, and
    /// a malformed one cannot disturb a pipeline that is already serving. Tuning a dependency therefore needs a
    /// restart, but never a rebuild.
    /// </para>
    /// <para>
    /// This registration covers non-HTTP dependencies only. <c>HttpClient</c> traffic is already wrapped once by the
    /// standard resilience handler in the host's service defaults, and adding a pipeline around an HTTP-based
    /// provider client would place two retry layers on one call.
    /// </para>
    /// <para>
    /// One builder is registered per dependency class and the registry builds a pipeline per
    /// <see cref="OutboundPipelineKey" /> from it, so a class that talks to several remote instances gets one
    /// pipeline — and therefore one circuit-breaker and one concurrency budget — per instance without any additional
    /// configuration.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddOutboundResiliencePipelines(
        this IServiceCollection services,
        IConfiguration resilienceConfiguration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(resilienceConfiguration);

        var startupBudgets = CaptureStartupBudgets(resilienceConfiguration);

        RejectSectionsNamingNoDependency(startupBudgets);

        services.AddSingleton<ITransientFailureClassifier, TransientFailureClassifier>();
        services.AddSingleton<IValidateOptions<OutboundDependencyResilienceOptions>, OutboundDependencyResilienceOptionsValidator>();
        services.AddSingleton<OutboundOperationExecutor>();

        // Only the dependency class selects a builder, so every instance of a class is built from its one
        // registration. The formatters keep the two halves of the key readable in Polly's own telemetry tags.
        services.AddResiliencePipelineRegistry<OutboundPipelineKey>(registryOptions =>
        {
            registryOptions.BuilderComparer = new OutboundPipelineBuilderComparer();
            registryOptions.BuilderNameFormatter = key => key.Dependency.ToString();
            registryOptions.InstanceNameFormatter = key => key.DependencyInstance;
        });

        foreach (var dependency in Enum.GetValues<OutboundDependency>())
        {
            var dependencyName = dependency.ToString();

            services.AddOptions<OutboundDependencyResilienceOptions>(dependencyName)
                .Configure(options => OutboundDependencyResilienceDefaults.ApplyTo(options, dependency))
                .Bind(
                    startupBudgets.GetSection(dependencyName),
                    binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddResiliencePipeline(
                new OutboundPipelineKey(dependency),
                (builder, context) => ComposePipeline(builder, context, dependency));
        }

        return services;
    }

    /// <summary>Copies the configured budgets into a standalone configuration that never reloads.</summary>
    /// <remarks>
    /// This is what makes the restart-required classification true by construction rather than by intention. Bound
    /// against the live configuration, these options would be reachable from a reload: `OptionsMonitor` drops its
    /// cache when a change token fires and rebuilds the named instance inside that notification, so one malformed
    /// edit would raise `OptionsValidationException` on the thread that reported the change — a file-watcher callback
    /// in a deployed host.
    /// <see href="../../../docs/decisions/0002-configuration-reading-mapping-and-reload-boundary.md">ADR 0002</see>
    /// rules that out, and a validated-snapshot layer that would make these settings safely reloadable does not exist
    /// for them yet. Binding a frozen copy means a reload has nothing to notify:
    /// the pipeline keeps the budget the host validated at startup, and a bad edit is inert until a restart reads it.
    /// </remarks>
    private static IConfiguration CaptureStartupBudgets(IConfiguration resilienceConfiguration) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(resilienceConfiguration.AsEnumerable(makePathsRelative: true))
            .Build();

    /// <summary>Fails when the configuration holds a section that names no dependency class.</summary>
    /// <remarks>
    /// Strict binding only inspects the keys inside a section it was pointed at, so a misspelled section name is not
    /// an unknown key to it — it is a section nobody reads. Startup would succeed on the shipped budget while the
    /// operator believed they had tuned it, which is the failure strict binding exists to prevent. The check runs at
    /// registration, before the host starts anything.
    /// </remarks>
    private static void RejectSectionsNamingNoDependency(IConfiguration resilienceConfiguration)
    {
        var dependencyNames = Enum.GetNames<OutboundDependency>();
        var unknownSections = resilienceConfiguration.GetChildren()
            .Select(section => section.Key)
            .Where(sectionName => !dependencyNames.Contains(sectionName, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        if (unknownSections.Length > 0)
        {
            throw new InvalidOperationException(
                $"Resilience configuration names no outbound dependency class in [{string.Join(", ", unknownSections)}]. "
                + $"The supported sections are [{string.Join(", ", dependencyNames)}].");
        }
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
        AddResiliencePipelineContext<OutboundPipelineKey> context,
        OutboundDependency dependency)
    {
        var dependencyName = dependency.ToString();
        // The builder runs once per requested key, so the instance the registry is creating names itself here and is
        // the only place a retry or a breaker event can learn which remote server it is reporting on.
        var dependencyInstance = context.PipelineKey.DependencyInstance;

        // EnableReloads is deliberately not called. Registering a listener on the named options makes
        // OptionsMonitor.InvokeChanged materialize the candidate — validation included — on the very thread that
        // reported the configuration change, so one malformed edit throws out of the file watcher instead of being
        // rejected. ADR 0002 forbids exactly that, and its default for a setting group without a validated-snapshot
        // layer is restart-required. These budgets take that default until they get one.
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
            builder.AddRetry(ComposeRetryStrategy(options, dependency, dependencyName, dependencyInstance, classifier, logger));
        }

        builder.AddCircuitBreaker(ComposeCircuitBreakerStrategy(options, dependency, dependencyName, dependencyInstance, classifier, logger));
        builder.AddTimeout(options.AttemptTimeout);
    }

    private static RetryStrategyOptions ComposeRetryStrategy(
        OutboundDependencyResilienceOptions options,
        OutboundDependency dependency,
        string dependencyName,
        string dependencyInstance,
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
                    dependencyInstance,
                    DescribeOperation(arguments.Context),
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
        string dependencyInstance,
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
                    dependencyInstance,
                    DescribeFailureType(arguments.Outcome.Exception),
                    arguments.BreakDuration);

                return default;
            },
            OnClosed = _ =>
            {
                OutboundResilienceEvents.LogCircuitClosed(logger, dependencyName, dependencyInstance);

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

    private static string DescribeOperation(ResilienceContext context) =>
        context.OperationKey ?? "unnamed";
}
