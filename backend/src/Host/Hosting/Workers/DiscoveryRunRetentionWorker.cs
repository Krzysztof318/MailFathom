// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Discovery.Streaming;

namespace MailFathom.Host.Hosting.Workers;

/// <summary>Removes the Discover runs nobody can come back for, one statement per interval.</summary>
/// <remarks>
/// <para>
/// A run's answer is composed from somebody's correspondence, so how long it is kept is a storage-limitation
/// obligation rather than housekeeping: what the run's own retention says has to happen whether or not anybody asks
/// another question. That is the whole reason this is a worker at all — opening a run sweeps as well, and a deployment
/// where the questions stopped is exactly the one where that sweep never runs again.
/// </para>
/// <para>
/// It takes no lease, unlike the sweeps that reach a bucket. What it issues is one conditional delete, so two replicas
/// running it in the same second do the same thing once between them rather than twice — and the cost of a pass that
/// found nothing is one statement. The interval is therefore each replica's rather than the deployment's, which is
/// what a deployment gains from more of them here: the window closes sooner.
/// </para>
/// <para>
/// It is registered whether or not this deployment answers questions. An instance that answers none opens no run and
/// has nothing to remove, and one that stopped being able to still has the runs it answered before.
/// </para>
/// <para>
/// The store is resolved per pass rather than taken here, because the hosted services are constructed before startup
/// composes the connection string: a constructor asking for a store over the pool would build the pool in that window
/// and end the process on the composition rather than on anything this worker does.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class DiscoveryRunRetentionWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<DiscoveryRunRetentionWorker> logger,
    TimeProvider timeProvider) : BackgroundService
{
    /// <summary>How often a pass is made.</summary>
    /// <remarks>
    /// The retention window itself, so a run is removed within twice it in the worst case and the deployment issues
    /// twelve statements an hour to hold that promise. A shorter interval would buy a tighter window at the cost of
    /// statements against a table that is usually empty; a longer one would leave mail-derived rows past the window
    /// this build states.
    /// </remarks>
    internal static readonly TimeSpan Interval = DiscoveryRunBounds.RetentionAfterLastUse;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            await Task.Delay(Interval, timeProvider, stoppingToken);
            await this.RunOnceAsync(stoppingToken);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The hosted worker isolates a failed pass so the next interval asks again.")]
    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();

            var forgotten = await scope.ServiceProvider
                .GetRequiredService<IDiscoveryRunStore>()
                .RemoveForgottenAsync(timeProvider.GetUtcNow(), stoppingToken);

            if (forgotten > 0)
            {
                LogRunsForgotten(logger, forgotten);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown rather than a failure: a pass cut short removed exactly what it removed, and what it did not
            // reach is still due on the next one.
        }
        catch (Exception exception)
        {
            LogPassFailed(logger, exception);
        }
    }

    /// <summary>Reports a pass in a count alone; no run, user, or anything a run composed reaches a log.</summary>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Forgot {ForgottenRunCount} Discover runs whose retention had ended.")]
    private static partial void LogRunsForgotten(ILogger logger, int forgottenRunCount);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A pass over the Discover runs due to be forgotten failed; the next interval will ask again.")]
    private static partial void LogPassFailed(ILogger logger, Exception exception);
}
