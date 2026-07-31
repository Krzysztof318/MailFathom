// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Collections.Immutable;
using MailFathom.Application.Resilience;
using Polly;
using Polly.Registry;

namespace MailFathom.Infrastructure.Resilience;

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

    private readonly ResiliencePipelineProvider<OutboundPipelineKey> pipelineProvider;

    /// <summary>Initializes an executor over the registered pipelines.</summary>
    /// <param name="pipelineProvider">Resolves, and creates on first use, the pipeline registered for a key.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="pipelineProvider" /> is <see langword="null" />.</exception>
    public OutboundOperationExecutor(ResiliencePipelineProvider<OutboundPipelineKey> pipelineProvider)
    {
        ArgumentNullException.ThrowIfNull(pipelineProvider);

        this.pipelineProvider = pipelineProvider;
    }

    /// <summary>Runs an operation under the process-wide pipeline of a dependency class that talks to one remote instance.</summary>
    /// <typeparam name="TResult">The result the operation produces.</typeparam>
    /// <param name="dependency">The dependency class whose budget governs the operation.</param>
    /// <param name="operation">The operation, which must be safe to repeat.</param>
    /// <param name="cancellationToken">Cancels the operation and every remaining attempt.</param>
    /// <returns>The result of the attempt that succeeded.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation" /> is <see langword="null" />.</exception>
    /// <exception cref="OutboundDependencyUnavailableException">Thrown when a configured limit stopped the operation.</exception>
    public Task<TResult> ExecuteAsync<TResult>(
        OutboundDependency dependency,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken) =>
        this.ExecuteAsync(
            new OutboundPipelineKey(dependency),
            operationKey: null,
            operation,
            cancellationToken);

    /// <summary>Runs an operation that produces no result under the process-wide pipeline of its dependency class.</summary>
    /// <param name="dependency">The dependency class whose budget governs the operation.</param>
    /// <param name="operation">The operation, which must be safe to repeat.</param>
    /// <param name="cancellationToken">Cancels the operation and every remaining attempt.</param>
    /// <returns>A task that completes when an attempt has succeeded.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation" /> is <see langword="null" />.</exception>
    /// <exception cref="OutboundDependencyUnavailableException">Thrown when a configured limit stopped the operation.</exception>
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

    /// <summary>Runs an operation under the pipeline of one remote instance of a dependency class.</summary>
    /// <typeparam name="TResult">The result the operation produces.</typeparam>
    /// <param name="pipelineKey">The dependency class and the remote instance whose pipeline state governs the operation.</param>
    /// <param name="operationKey">Names the logical operation in resilience telemetry, or <see langword="null" /> when it has no useful name. It must never carry personal data.</param>
    /// <param name="operation">The operation, which must be safe to repeat.</param>
    /// <param name="cancellationToken">Cancels the operation and every remaining attempt.</param>
    /// <returns>The result of the attempt that succeeded.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation" /> is <see langword="null" />.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when no pipeline is registered for the key's dependency class.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the same dependency class is already executing on this asynchronous flow.</exception>
    /// <exception cref="OutboundDependencyUnavailableException">Thrown when a configured limit stopped the operation.</exception>
    /// <remarks>
    /// Caller cancellation reaches the caller as <see cref="OperationCanceledException" />, and every limit the
    /// pipeline itself imposed as <see cref="OutboundDependencyUnavailableException" />. The total timeout is the one
    /// limit that does not always announce itself that way: expiring inside an attempt is a rejection, while expiring
    /// between attempts stops the retry and surfaces the failure that ended the last attempt.
    /// </remarks>
    public async Task<TResult> ExecuteAsync<TResult>(
        OutboundPipelineKey pipelineKey,
        string? operationKey,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var pipeline = this.pipelineProvider.GetPipeline(pipelineKey);
        var enclosingDependencies = DependenciesInFlight.Value ?? [];

        if (enclosingDependencies.Contains(pipelineKey.Dependency))
        {
            throw new InvalidOperationException(
                $"A resilience pipeline for {pipelineKey.Dependency} is already running on this execution flow. "
                + "One logical operation is retried at exactly one layer.");
        }

        DependenciesInFlight.Value = enclosingDependencies.Add(pipelineKey.Dependency);

        // The context carries the operation name into the retry and circuit-breaker events, which have no other way to
        // say which of an instance's operations was refused.
        var context = ResilienceContextPool.Shared.Get(operationKey, cancellationToken);

        try
        {
            return await pipeline.ExecuteAsync(
                static async (attemptContext, attempt) => await attempt(attemptContext.CancellationToken),
                context,
                operation);
        }
        catch (ExecutionRejectedException rejection)
        {
            throw new OutboundDependencyUnavailableException(pipelineKey.Dependency, rejection);
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
            DependenciesInFlight.Value = enclosingDependencies;
        }
    }
}
