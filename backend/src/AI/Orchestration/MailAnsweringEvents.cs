// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.Extensions.Logging;

namespace MailFathom.AI.Orchestration;

/// <summary>Records what one answering run did, and nothing about what it was carrying.</summary>
/// <remarks>
/// Every parameter here is either a name the operator chose or a count. The question, the answer, the queries the model
/// wrote, and the passages the run retrieved are all somebody's mail or somebody's words about it, and none of them
/// reaches these events — which is what lets them stay on in a deployment holding real mail.
/// </remarks>
internal static partial class MailAnsweringEvents
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "Chat endpoint {EndpointAlias} answered a question from {PassageCount} passages of {EmailCount} emails.")]
    internal static partial void LogAnswered(
        ILogger logger,
        string endpointAlias,
        int passageCount,
        int emailCount);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Error,
        Message = "Chat endpoint {EndpointAlias} ended an answering run without producing any text, having retrieved {PassageCount} passages.")]
    internal static partial void LogRunProducedNoAnswer(
        ILogger logger,
        string endpointAlias,
        int passageCount);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Information,
        Message = "Chat endpoint {EndpointAlias} answered from {PassageCount} passages after the run reached this deployment's ceiling on retrieved mail; the answer states that the mailbox was not read in full.")]
    internal static partial void LogRetrievalCeilingReached(
        ILogger logger,
        string endpointAlias,
        int passageCount);
}
