// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.Extensions.Logging;

namespace MailFathom.AI.ReplyDrafts;

/// <summary>What a drafting reports about itself, carrying no part of the reply and no part of the correspondence.</summary>
/// <remarks>
/// A draft is a message somebody is about to send and inherits the classification of the mail it was written from, so
/// nothing here names a word of it: how many claims it made, how many of them the correspondence backs, how many people
/// it proposed, and the endpoint that wrote it are the whole of what is safe to report. No recipient, no address, no
/// conversation, and nothing derived from the person's own sent mail is named either.
/// </remarks>
internal static partial class ReplyDraftEvents
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "The reply-drafting agent at {EndpointAlias} drafted a reply from {MessageCount} messages, making {ClaimCount} claims of which {UnsupportedClaimCount} rest on no message, and proposing {RecipientCount} recipients.")]
    internal static partial void LogDrafted(
        ILogger logger,
        string endpointAlias,
        int messageCount,
        int claimCount,
        int unsupportedClaimCount,
        int recipientCount);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "The reply-drafting agent at {EndpointAlias} answered with nothing this build could read as a reply, so the composer is left as it was.")]
    internal static partial void LogAnswerUnreadable(ILogger logger, string endpointAlias);
}
