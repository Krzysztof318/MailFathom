// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Mail.Export;

namespace MailFathom.Host.Hosting.Workers;

/// <summary>Deletes export archives whose retention period has run out, one bounded pass per interval.</summary>
/// <remarks>
/// Every replica runs the loop and the sweep's own lease decides which of them deletes anything, so the interval is the
/// deployment's rather than one per replica. It is registered whichever backend the deployment stores content in: a
/// deployment that cannot produce an archive has none to expire, and a deployment that stopped being able to still has
/// the ones it produced before.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class MailboxExportExpiryWorker(
    IServiceScopeFactory scopeFactory,
    TimeSpan interval,
    ILogger<MailboxExportExpiryWorker> logger,
    TimeProvider timeProvider) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            await Task.Delay(interval, timeProvider, stoppingToken);
            await this.RunOnceAsync(stoppingToken);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The hosted worker isolates a failed pass so the next interval asks again.")]
    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();

            var expired = await scope.ServiceProvider
                .GetRequiredService<MailboxExportExpirySweep>()
                .RunAsync(stoppingToken);

            if (expired > 0)
            {
                LogArchivesExpired(logger, expired.Value);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown rather than a failure: a pass cut short has deleted exactly what it deleted, and what it did not
            // reach is still due on the next pass.
        }
        catch (Exception exception)
        {
            LogPassFailed(logger, exception);
        }
    }

    /// <summary>Reports a pass in a count alone; no account, export, or key reaches a log.</summary>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Deleted {ExpiredArchiveCount} mailbox export archives whose retention period had ended.")]
    private static partial void LogArchivesExpired(ILogger logger, int expiredArchiveCount);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A pass over the mailbox export archives due for expiry failed; the next interval will ask again.")]
    private static partial void LogPassFailed(ILogger logger, Exception exception);
}
