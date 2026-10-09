// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;
using MailFathom.Host.Configuration.Mail;
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
/// <para>
/// A job enqueued for a mail account is run against that account's settings and those of the other accounts its users
/// hold, read when the scope is created and before the executor is: every handler is composed with the scope, and what
/// they are composed from reads the account's settings. An account no longer served leaves the scope holding none, so
/// the handler meets the account as removed, exactly as it would have once its record went.
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

        var preparationFailure = await PrepareAsync(scope.ServiceProvider, job, stoppingToken);
        var executor = scope.ServiceProvider.GetRequiredService<JobExecutor>();
        var result = preparationFailure is null
            ? await executor.ExecuteAsync(job, stoppingToken)
            : await executor.RecordFailedPreparationAsync(job, preparationFailure, stoppingToken);

        attempt.Ended(result);

        return result;
    }

    /// <summary>Prepares the account's settings before anything in the scope is composed, and answers what that raised.</summary>
    /// <remarks>
    /// Nothing is read for a host already stopping, so the executor releases the job rather than this reading failing
    /// it. A failure is answered rather than raised, because it belongs to this job alone and the executor records it
    /// against the job; a cancellation by the stopping host is answered as nothing for the same reason, since the
    /// executor sees the host stopping and releases the job.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Whatever preparing one job raised is recorded against that job, so the jobs beside it in the batch still run.")]
    private static async Task<Exception?> PrepareAsync(
        IServiceProvider services,
        LeasedJob job,
        CancellationToken stoppingToken)
    {
        if (job.AccountId is not { } account || stoppingToken.IsCancellationRequested)
        {
            return null;
        }

        try
        {
            await services
                .GetRequiredService<ScopedMailSynchronizationSettings>()
                .UseAccountSettingsAsync(account, stoppingToken);

            return null;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception failure)
        {
            return failure;
        }
    }
}
