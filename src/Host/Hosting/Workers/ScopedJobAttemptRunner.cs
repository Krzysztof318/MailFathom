// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;

namespace MailFathom.Host.Hosting.Workers;

/// <summary>Gives one job attempt a dependency-injection scope of its own, and runs it there.</summary>
/// <remarks>
/// The scope is the whole of what this contributes, and it is what makes running jobs at once safe: an executor writes
/// through the persistence session its scope holds, and a session is neither thread-safe nor shareable between attempts
/// that renew, complete, and dead-letter different rows at the same moment. One scope per attempt also means an attempt
/// releases its connection when it ends rather than when the pass around it does.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this port implementation.")]
internal sealed class ScopedJobAttemptRunner(IServiceScopeFactory scopeFactory) : IJobAttemptRunner
{
    /// <inheritdoc />
    public async Task<JobExecutionResult> RunAsync(LeasedJob job, CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        await using var scope = scopeFactory.CreateAsyncScope();

        var executor = scope.ServiceProvider.GetRequiredService<JobExecutor>();

        return await executor.ExecuteAsync(job, stoppingToken);
    }
}
