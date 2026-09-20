// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Enrichment;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.Enrichment;

/// <summary>What a derivation reports about itself, carrying no part of the message and no part of what was derived.</summary>
/// <remarks>
/// A mark is a sentence about somebody's mail and inherits its classification whole, and so is the line a proposed
/// task is offered as — so nothing here names either: the counts, the endpoint that produced them, and the reason a
/// derivation was withheld are the whole of what is safe to report.
/// </remarks>
internal static partial class EmailEnrichmentEvents
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "The enrichment agent at {EndpointAlias} derived {MarkCount} marks and {TaskCount} proposed tasks from {PassageCount} passages.")]
    internal static partial void LogDerived(
        ILogger logger,
        string endpointAlias,
        int markCount,
        int taskCount,
        int passageCount);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "The enrichment agent at {EndpointAlias} answered with nothing this build could read as a mark or a proposal, so the message is recorded as having nothing to say.")]
    internal static partial void LogAnswerUnreadable(ILogger logger, string endpointAlias);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Information,
        Message = "Enrichment at {EndpointAlias} was withheld because {Withholding}, so the message stays outstanding.")]
    internal static partial void LogWithheld(
        ILogger logger,
        string endpointAlias,
        EmailEnrichmentWithholding withholding);
}
