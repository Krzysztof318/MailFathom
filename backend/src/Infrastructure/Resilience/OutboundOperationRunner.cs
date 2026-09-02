// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Resilience;

namespace MailFathom.Infrastructure.Resilience;

/// <summary>Publishes <see cref="OutboundOperationExecutor" /> to adapters outside this boundary.</summary>
/// <remarks>
/// It adds nothing and is meant to add nothing. The executor stays internal because the pipeline key, the registry,
/// and the resilience library it composes are this namespace's business, and this type is the one member of its
/// surface an adapter elsewhere needs — so the single-layer rule, the failure classification, and the
/// <see cref="OutboundDependencyUnavailableException" /> translation all still happen in exactly one place.
/// </remarks>
internal sealed class OutboundOperationRunner : IOutboundOperationRunner
{
    private readonly OutboundOperationExecutor operationExecutor;

    /// <summary>Initializes a runner over the executor that owns the pipelines.</summary>
    /// <param name="operationExecutor">Applies the configured budget of a dependency class.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="operationExecutor" /> is <see langword="null" />.</exception>
    public OutboundOperationRunner(OutboundOperationExecutor operationExecutor)
    {
        ArgumentNullException.ThrowIfNull(operationExecutor);

        this.operationExecutor = operationExecutor;
    }

    /// <inheritdoc />
    /// <exception cref="OutboundDependencyUnavailableException">Thrown when a configured limit stopped the operation.</exception>
    public Task<TResult> RunAsync<TResult>(
        OutboundDependency dependency,
        string remoteInstance,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteInstance);

        return this.operationExecutor.ExecuteAsync(
            new OutboundPipelineKey(dependency, remoteInstance),
            operationKey: null,
            operation,
            cancellationToken);
    }
}
