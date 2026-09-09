// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.Extensions.Logging;

namespace MailFathom.AI.Search;

/// <summary>What a phrase reading reports about itself, carrying no part of the sentence and no part of the mailbox.</summary>
/// <remarks>
/// Counts and the endpoint's own alias, and nothing else. What somebody is looking for in their own mailbox is the most
/// revealing value this surface carries, so neither the sentence, nor a criterion read out of it, nor the part left
/// over reaches a log line here or anywhere else.
/// </remarks>
internal static partial class MailSearchPhraseEvents
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "The phrase-reading agent at {EndpointAlias} read a sentence into {FilterCount} filters and {CriterionCount} criteria.")]
    internal static partial void LogPhraseRead(
        ILogger logger,
        string endpointAlias,
        int filterCount,
        int criterionCount);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "The phrase-reading agent at {EndpointAlias} answered with nothing this build could read as an interpretation, so the typed words are being searched for instead.")]
    internal static partial void LogPhraseUnreadable(ILogger logger, string endpointAlias);
}
