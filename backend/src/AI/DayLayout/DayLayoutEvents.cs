// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Tasks;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.DayLayout;

/// <summary>What laying out a day reports about itself, carrying no part of the day and no part of the arrangement.</summary>
/// <remarks>
/// A task's line and an appointment's title are personal data of the same standing as the mail they may have been read
/// out of, and when somebody's day is busy is no less so — so nothing here names any of them: the counts, the endpoint
/// that answered, and the reason an arrangement was withheld are the whole of what is safe to report.
/// </remarks>
internal static partial class DayLayoutEvents
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "The day-layout agent at {EndpointAlias} placed {PlacedCount} of {TaskCount} tasks and left {DeferredCount} for another day.")]
    internal static partial void LogSuggested(
        ILogger logger,
        string endpointAlias,
        int placedCount,
        int taskCount,
        int deferredCount);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "The day-layout agent at {EndpointAlias} answered with nothing this build could read as an arrangement, so the day is answered with none.")]
    internal static partial void LogAnswerUnreadable(ILogger logger, string endpointAlias);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Information,
        Message = "Laying out a day at {EndpointAlias} was withheld because {Withholding}, so the person is offered no arrangement this time.")]
    internal static partial void LogWithheld(
        ILogger logger,
        string endpointAlias,
        DayLayoutWithholding withholding);
}
