// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Chat;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.ProviderAdapters;

/// <summary>Records what a chat call decided and what it cost, and nothing about what it was carrying.</summary>
/// <remarks>
/// Every parameter here is either a name the operator chose, a classification of this system's own, or a count. No
/// prompt, no answer, no credential, and no provider response body reaches these events, which is what lets them stay
/// on in a deployment holding real mail: a prompt is somebody's question and the passages of their mail, an answer is
/// written from both, and a provider's own error text quotes the request that produced it.
/// </remarks>
internal static partial class ChatProviderEvents
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "Chat endpoint {EndpointAlias} produced no answer; the call ended with {ChatFailure}.")]
    internal static partial void LogCallFailed(
        ILogger logger,
        string endpointAlias,
        ChatGenerationFailure chatFailure);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Debug,
        Message = "Chat endpoint {EndpointAlias} answered after {InputTokens} input and {OutputTokens} output tokens, stopping with {ChatStop}.")]
    internal static partial void LogAnswered(
        ILogger logger,
        string endpointAlias,
        long inputTokens,
        long outputTokens,
        ChatGenerationStop chatStop);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Chat endpoint {EndpointAlias} stopped generating with {ChatStop} rather than completing, so the answer is not the whole of what the model was producing.")]
    internal static partial void LogAnswerCutShort(
        ILogger logger,
        string endpointAlias,
        ChatGenerationStop chatStop);
}
