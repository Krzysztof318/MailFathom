// Copyright © 2026 Krzysztof Kasprowicz

using System.Collections.Immutable;
using MailMcp.Application.Resilience;
using Polly.Registry;

namespace MailMcp.Infrastructure.Resilience;

/// <summary>Runs an outbound operation under the resilience pipeline configured for its dependency class.</summary>
/// <remarks>
/// <para>
/// Adapters go through this type instead of resolving a pipeline themselves, which keeps the resilience library
/// inside this namespace and makes the dependency class an explicit argument at every call site.
/// </para>
/// <para>
/// It is also where the single-layer rule is enforced. Re-entering the same dependency class means an inner retry is
/// running inside an outer one, and the attempt counts of the two layers multiply: three attempts wrapped by three
/// attempts is nine calls into a server that is already struggling. That nesting fails immediately rather than
/// producing a retry storm nobody configured.
/// </para>
/// </remarks>
internal sealed class OutboundOperationExecutor
{
    private static readonly AsyncLocal<ImmutableHashSet<OutboundDependency>?> DependenciesInFlight = new();

    private readonly ResiliencePipelineProvider<OutboundDependency> pipelineProvider;

    /// <summary>Initializes an executor over the registered pipelines.</summary>
    /// <param name="pipelineProvider">Resolves the pipeline registered for a dependency class.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="pipelineProvider" /> is <see langword="null" />.</exception>
    public OutboundOperationExecutor(ResiliencePipelineProvider<OutboundDependency> pipelineProvider)
    {
        ArgumentNullException.ThrowIfNull(pipelineProvider);

        this.pipelineProvider = pipelineProvider;
    }

    /// <summary>Runs an operation that produces a result under its dependency class pipeline.</summary>
    /// <typeparam name="TResult">The result the operation produces.</typeparam>
    /// <param name="dependency">The dependency class whose budget governs the operation.</param>
    /// <param name="operation">The operation, which must be safe to repeat.</param>
    /// <param name="cancellationToken">Cancels the operation and every remaining attempt.</param>
    /// <returns>The result of the attempt that succeeded.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation" /> is <see langword="null" />.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when no pipeline is registered for <paramref name="dependency" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the same dependency class is already executing on this asynchronous flow.</exception>
    /// <remarks>
    /// Caller cancellation, an abandoned attempt, an open circuit, and a shed execution reach the caller as distinct
    /// exception types. The total timeout is the one limit that does not always name itself: expiring inside an
    /// attempt surfaces <see cref="Polly.Timeout.TimeoutRejectedException" />, while expiring between attempts stops
    /// the retry and surfaces the failure that ended the last one.
    /// </remarks>
    public async Task<TResult> ExecuteAsync<TResult>(
        OutboundDependency dependency,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var pipeline = this.pipelineProvider.GetPipeline(dependency);
        var enclosingDependencies = DependenciesInFlight.Value ?? [];

        if (enclosingDependencies.Contains(dependency))
        {
            throw new InvalidOperationException(
                $"A resilience pipeline for {dependency} is already running on this execution flow. "
                + "One logical operation is retried at exactly one layer.");
        }

        DependenciesInFlight.Value = enclosingDependencies.Add(dependency);

        try
        {
            return await pipeline.ExecuteAsync(
                static async (attempt, attemptToken) => await attempt(attemptToken),
                operation,
                cancellationToken);
        }
        finally
        {
            DependenciesInFlight.Value = enclosingDependencies;
        }
    }

    /// <summary>Runs an operation that produces no result under its dependency class pipeline.</summary>
    /// <param name="dependency">The dependency class whose budget governs the operation.</param>
    /// <param name="operation">The operation, which must be safe to repeat.</param>
    /// <param name="cancellationToken">Cancels the operation and every remaining attempt.</param>
    /// <returns>A task that completes when an attempt has succeeded.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation" /> is <see langword="null" />.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when no pipeline is registered for <paramref name="dependency" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the same dependency class is already executing on this asynchronous flow.</exception>
    public async Task ExecuteAsync(
        OutboundDependency dependency,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await this.ExecuteAsync(
            dependency,
            async attemptToken =>
            {
                await operation(attemptToken);

                return true;
            },
            cancellationToken);
    }
}
