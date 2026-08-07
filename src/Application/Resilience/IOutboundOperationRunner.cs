// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Resilience;

/// <summary>Runs an outbound operation under the resilience budget configured for its dependency class.</summary>
/// <remarks>
/// <para>
/// The port exists so an adapter outside the boundary that owns the resilience library can still be governed by it.
/// Persistence and mail adapters live beside that implementation and reach it directly; the provider adapters do not,
/// and the alternative to this interface would be either a second retry mechanism or a project reference from one
/// adapter boundary to another. Both are worse than one method.
/// </para>
/// <para>
/// It carries no strategy of its own and never will. Which limits apply, how a failure is classified, and what a
/// caller sees when a limit is reached are the implementation's, so an adapter states only which dependency class it
/// belongs to and which remote instance it is talking to.
/// </para>
/// </remarks>
public interface IOutboundOperationRunner
{
    /// <summary>Runs an operation under the pipeline of one remote instance of a dependency class.</summary>
    /// <typeparam name="TResult">The result the operation produces.</typeparam>
    /// <param name="dependency">The dependency class whose configured budget governs the operation.</param>
    /// <param name="remoteInstance">The deployment's own name for the remote instance being called, so one unhealthy endpoint does not stop the others. It must never carry personal data, because it reaches resilience telemetry.</param>
    /// <param name="operation">The operation, which must be safe to repeat.</param>
    /// <param name="cancellationToken">Cancels the operation and every remaining attempt.</param>
    /// <returns>The result of the attempt that succeeded.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="remoteInstance" /> or <paramref name="operation" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the same dependency class is already executing on this asynchronous flow, which would put one operation's retries inside another's.</exception>
    /// <remarks>Caller cancellation reaches the caller as <see cref="OperationCanceledException" />, and every limit the pipeline itself imposed as a failure the implementation documents.</remarks>
    Task<TResult> RunAsync<TResult>(
        OutboundDependency dependency,
        string remoteInstance,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken);
}
