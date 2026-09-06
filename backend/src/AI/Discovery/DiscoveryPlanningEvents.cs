// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.Extensions.Logging;

namespace MailFathom.AI.Discovery;

/// <summary>What a planning derivation reports about itself, carrying no part of the question and no part of the mailbox.</summary>
internal static partial class DiscoveryPlanningEvents
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "The planning agent at {EndpointAlias} read a question as {Intent} with {LookupCount} lookups, enough at {SufficientPassages} passages.")]
    internal static partial void LogPlanDerived(
        ILogger logger,
        string endpointAlias,
        string intent,
        int lookupCount,
        int sufficientPassages);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "The planning agent at {EndpointAlias} answered with nothing this build could read as a plan, so the question's own words are being searched for instead.")]
    internal static partial void LogPlanUnreadable(ILogger logger, string endpointAlias);
}
