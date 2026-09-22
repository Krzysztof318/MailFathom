// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Chat;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.AgentConversations;

/// <summary>What the Agent reports about a run, carrying no part of the question, the answer, or anything it read.</summary>
/// <remarks>
/// A conversation with the Agent holds the most revealing text this deployment keeps — what somebody asked about their
/// own mail — so the endpoint that answered and how many messages the answer rests on are the whole of what is safe to
/// report.
/// </remarks>
internal static partial class AgentConversationEvents
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "The Agent at {EndpointAlias} answered, resting the answer on {CitedCount} messages.")]
    internal static partial void LogAnswered(ILogger logger, string endpointAlias, int citedCount);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "The Agent at {EndpointAlias} ended its turn with no text, so the answer ends as failed.")]
    internal static partial void LogProducedNoAnswer(ILogger logger, string endpointAlias);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Debug,
        Message = "The conversation was compacted at {EndpointAlias} into a summary of {SummaryLength} characters.")]
    internal static partial void LogCompacted(ILogger logger, string endpointAlias, int summaryLength);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "Compacting an Agent conversation failed at {EndpointAlias} ({Failure}), so the turn is composed from as much recent history as fits and nothing is recorded.")]
    internal static partial void LogCompactionFailed(ILogger logger, string endpointAlias, ChatGenerationFailure failure);
}
