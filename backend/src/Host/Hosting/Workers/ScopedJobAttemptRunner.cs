// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;
using MailFathom.Infrastructure.Observability;

namespace MailFathom.Host.Hosting.Workers;

/// <summary>Gives one job attempt a dependency-injection scope of its own and a span of its own, and runs it there.</summary>
/// <remarks>
/// <para>
/// The scope is what makes running jobs at once safe: an executor writes through the persistence session its scope
/// holds, and a session is neither thread-safe nor shareable between attempts that renew, complete, and dead-letter
/// different rows at the same moment. One scope per attempt also means an attempt releases its connection when it ends
/// rather than when the pass around it does.
/// </para>
/// <para>
/// The span is opened around the same boundary and for the same reason the scope is drawn there. An attempt is the unit
/// of work a trace can attribute anything to, so everything the executor and the handler beneath it issue — the
/// database commands above all — becomes that span's children instead of parentless work beside whatever request
/// happened to be running. A pass dispatching several jobs at once therefore produces one span each, since each attempt
/// opens its own on the task that runs it.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this port implementation.")]
internal sealed class ScopedJobAttemptRunner(IServiceScopeFactory scopeFactory, JobQueueTelemetry telemetry)
    : IJobAttemptRunner
{
    /// <inheritdoc />
    public async Task<JobExecutionResult> RunAsync(LeasedJob job, CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        using var attempt = telemetry.BeginAttempt(job.JobType, job.EnqueuedTrace);

        await using var scope = scopeFactory.CreateAsyncScope();

        var executor = scope.ServiceProvider.GetRequiredService<JobExecutor>();
        var result = await executor.ExecuteAsync(job, stoppingToken);

        attempt.Ended(result);

        return result;
    }
}
