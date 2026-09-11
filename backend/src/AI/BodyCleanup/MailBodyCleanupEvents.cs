// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Cleaning;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.BodyCleanup;

/// <summary>What a cleaning reports about itself, carrying no part of the message and no part of what was dropped.</summary>
/// <remarks>
/// A block's opening is a line somebody wrote to somebody, and which blocks were dropped is a description of one person's
/// mail, so nothing here names either: a count of blocks, the endpoint that answered, and the reason a cleaning was
/// withheld are the whole of what is safe to report. The message is not named either — a stored identifier is a handle on
/// one person's mail, and an operator diagnosing a provider does not need it.
/// </remarks>
internal static partial class MailBodyCleanupEvents
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "The body-cleanup agent at {EndpointAlias} proposed {SegmentCount} ranges over {BlockCount} blocks.")]
    internal static partial void LogProposed(
        ILogger logger,
        string endpointAlias,
        int segmentCount,
        int blockCount);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "The body-cleanup agent at {EndpointAlias} answered with nothing this build could read as a partition of {BlockCount} block numbers, so the reader is shown the uncleaned message.")]
    internal static partial void LogAnswerUnreadable(ILogger logger, string endpointAlias, int blockCount);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Information,
        Message = "A body cleaning at {EndpointAlias} was withheld because {Withholding}, so the reader is shown the uncleaned message.")]
    internal static partial void LogWithheld(
        ILogger logger,
        string endpointAlias,
        MailBodyCleaningWithholding withholding);
}
