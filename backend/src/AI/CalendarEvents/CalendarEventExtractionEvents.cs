// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Calendar.Extraction;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.CalendarEvents;

/// <summary>What an extraction reports about itself, carrying no part of the text and no part of what was read.</summary>
/// <remarks>
/// A title says who somebody is meeting and the times say when they are not somewhere else, so nothing here names
/// either: a count of events, the endpoint that produced them, and the reason an extraction was withheld are the whole
/// of what is safe to report.
/// </remarks>
internal static partial class CalendarEventExtractionEvents
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "The calendar extraction agent at {EndpointAlias} read {EventCount} events out of a message.")]
    internal static partial void LogProposedFromMail(ILogger logger, string endpointAlias, int eventCount);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Debug,
        Message = "The calendar extraction agent at {EndpointAlias} read {EventCount} events out of a typed description.")]
    internal static partial void LogDraftedFromDescription(ILogger logger, string endpointAlias, int eventCount);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Information,
        Message = "The calendar extraction at {EndpointAlias} was withheld because {Withholding}, so the text stays unread.")]
    internal static partial void LogWithheld(
        ILogger logger,
        string endpointAlias,
        CalendarEventExtractionWithholding withholding);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Debug,
        Message = "A message carrying no arrival instant was left unread, because nothing in it could be resolved to a day.")]
    internal static partial void LogMessageNotAnchored(ILogger logger);
}
