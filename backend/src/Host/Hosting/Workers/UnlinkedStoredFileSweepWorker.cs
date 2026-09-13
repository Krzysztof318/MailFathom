// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.StoredFiles;

namespace MailFathom.Host.Hosting.Workers;

/// <summary>Runs the sweep of stored files no record links to, one bounded run per interval.</summary>
/// <remarks>
/// Every replica runs the loop and the sweep's own lease decides which of them removes anything, so the interval is a
/// deployment's rather than one per replica only in the sense that a run refused the lease does nothing. Registered on
/// every deployment, because a file left unlinked is left whichever backend holds its octets.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class UnlinkedStoredFileSweepWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<UnlinkedStoredFileSweepWorker> logger,
    TimeProvider timeProvider) : BackgroundService
{
    /// <summary>How long the worker waits before each run.</summary>
    /// <remarks>ponytail: a constant rather than a setting; an unlinked file is a failed upload, so make it configurable only once a deployment produces enough of them for an hour to matter.</remarks>
    internal static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            await Task.Delay(Interval, timeProvider, stoppingToken);
            await this.RunOnceAsync(stoppingToken);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The hosted worker isolates a failed run so the next interval asks again.")]
    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();

            var removed = await scope.ServiceProvider
                .GetRequiredService<UnlinkedStoredFileSweep>()
                .RunAsync(stoppingToken);

            if (removed > 0)
            {
                LogFilesRemoved(logger, removed.Value);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown rather than a failure: a run cut short has removed exactly what it removed.
        }
        catch (Exception exception)
        {
            LogRunFailed(logger, exception);
        }
    }

    /// <summary>Reports a run in a count alone; no identifier of a file or of its owner reaches a log.</summary>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Removed {RemovedFileCount} stored files no user record links to.")]
    private static partial void LogFilesRemoved(ILogger logger, int removedFileCount);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A sweep of stored files no user record links to failed; the next interval will ask again.")]
    private static partial void LogRunFailed(ILogger logger, Exception exception);
}
