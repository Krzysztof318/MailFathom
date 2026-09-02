// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.AiProviders;
using MailFathom.Application.Chat;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.Retrieval;

/// <summary>Records what the second retrieval pass decided, and nothing about what it was deciding over.</summary>
/// <remarks>
/// Every parameter here is a count or a classification this system produced. The query, the extracts put to the model,
/// the turn they were composed into, and the answers that came back are all somebody's mail or a judgement of it, and
/// none of them reaches these events — which is what lets them stay on in a deployment holding real mail.
/// </remarks>
internal static partial class PassageRelevanceEvents
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "The relevance filter judged {JudgedCount} of {RetrievedCount} retrieved passages and dropped {DroppedCount}.")]
    internal static partial void LogJudged(
        ILogger logger,
        int retrievedCount,
        int judgedCount,
        int droppedCount);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Debug,
        Message = "The relevance filter judged nothing and handed over all {RetrievedCount} retrieved passages, because the chat provider is {ProviderState}.")]
    internal static partial void LogProviderUnusable(
        ILogger logger,
        AiProviderHealthState providerState,
        int retrievedCount);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "The relevance filter stopped judging and kept {UnjudgedCount} passages unjudged, because the chat provider failed with {Failure}.")]
    internal static partial void LogJudgingStopped(ILogger logger, ChatGenerationFailure failure, int unjudgedCount);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "The relevance filter kept a passage whose judgement did not answer the score it asked for.")]
    internal static partial void LogJudgementMalformed(ILogger logger);
}
