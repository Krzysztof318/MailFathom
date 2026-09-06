// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Presentation;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.Discovery;

/// <summary>What a composition reports about itself, carrying no part of the question, the answer, or the mailbox.</summary>
/// <remarks>
/// The verdict and the counts are the run's own decisions rather than mail, which is what makes them safe to record: an
/// operator reading these can tell a deployment answering from nothing from one answering from contradictory mail, and
/// still learns nothing about anybody's correspondence.
/// </remarks>
internal static partial class DiscoveryCompositionEvents
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "The composing agent at {EndpointAlias} composed {BlockCount} blocks over {SourceCount} sources, with the answer {Support}.")]
    internal static partial void LogResultComposed(
        ILogger logger,
        string endpointAlias,
        int blockCount,
        int sourceCount,
        PresentationSupport support);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "The composing agent at {EndpointAlias} answered with nothing this build could read, so the result says the sources do not answer the question.")]
    internal static partial void LogResultUnreadable(ILogger logger, string endpointAlias);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "A composing turn for {EndpointAlias} was past what this deployment sends in one request, so no call was made and the result says the sources do not answer the question.")]
    internal static partial void LogRequestPastItsBound(ILogger logger, string endpointAlias);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "The composing call to {EndpointAlias} failed, so the result says the sources do not answer the question.")]
    internal static partial void LogGenerationFailed(ILogger logger, string endpointAlias);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Warning,
        Message = "The endpoint {EndpointAlias} or its credential did not resolve, so no composing call was made and the result says the sources do not answer the question.")]
    internal static partial void LogEndpointUnresolved(ILogger logger, string endpointAlias);
}
