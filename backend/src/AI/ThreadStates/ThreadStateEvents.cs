// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.ThreadStates;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.ThreadStates;

/// <summary>What a derivation reports about itself, carrying no part of the conversation and no part of what was derived.</summary>
/// <remarks>
/// A statement is a sentence about somebody's correspondence and inherits its classification whole, so nothing here
/// names one: a count of statements, the endpoint that produced them, and the reason a derivation was withheld are the
/// whole of what is safe to report. The conversation itself is not named either — a thread identifier is a handle on
/// one person's exchange, and an operator diagnosing a provider does not need it.
/// </remarks>
internal static partial class ThreadStateEvents
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "The thread-state agent at {EndpointAlias} derived {EntryCount} statements from {MessageCount} messages.")]
    internal static partial void LogDerived(
        ILogger logger,
        string endpointAlias,
        int entryCount,
        int messageCount);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "The thread-state agent at {EndpointAlias} answered with nothing this build could read as statements, so the conversation is recorded as having nothing to say.")]
    internal static partial void LogAnswerUnreadable(ILogger logger, string endpointAlias);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Information,
        Message = "A thread state at {EndpointAlias} was withheld because {Withholding}, so the conversation stays outstanding.")]
    internal static partial void LogWithheld(
        ILogger logger,
        string endpointAlias,
        ThreadStateWithholding withholding);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Debug,
        Message = "A conversation of {MessageCount} messages is past what one derivation takes in, so it is recorded as too large rather than read in part.")]
    internal static partial void LogTooLarge(ILogger logger, int messageCount);
}
