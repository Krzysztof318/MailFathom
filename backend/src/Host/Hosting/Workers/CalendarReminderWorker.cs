// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Calendar;

namespace MailFathom.Host.Hosting.Workers;

/// <summary>Announces the calendar reminders that have come due, one bounded pass per interval.</summary>
/// <remarks>
/// <para>
/// Every replica runs the loop and the pass's own lease decides which of them announces anything, so the interval is
/// the deployment's rather than one per replica.
/// </para>
/// <para>
/// The interval is a minute and is not an operator's to set. A reminder is stated in whole minutes, so a shorter one
/// would ask the same question twice for the same answer and a longer one would make every reminder late by as much
/// as it was lengthened — there is no setting here, only the unit the feature is already stated in.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class CalendarReminderWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<CalendarReminderWorker> logger,
    TimeProvider timeProvider) : BackgroundService
{
    /// <summary>How often a replica asks whether anything has come due, which is the unit a reminder is stated in.</summary>
    internal static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

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

            var announced = await scope.ServiceProvider
                .GetRequiredService<CalendarReminderSweep>()
                .RunAsync(stoppingToken);

            if (announced > 0)
            {
                LogRemindersAnnounced(logger, announced.Value);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown rather than a failure: a pass cut short announced exactly what it announced, and what it did
            // not reach is still due on the next one.
        }
        catch (Exception exception)
        {
            LogPassFailed(logger, exception);
        }
    }

    /// <summary>Reports a pass in a count alone; no person, event, or title reaches a log.</summary>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Announced {AnnouncedReminderCount} calendar reminders that had come due.")]
    private static partial void LogRemindersAnnounced(ILogger logger, int announcedReminderCount);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A pass over the calendar reminders that had come due failed; the next interval will ask again.")]
    private static partial void LogPassFailed(ILogger logger, Exception exception);
}
