// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Application.Agent.Conversations;

/// <summary>The closed set of things a question may be asked about.</summary>
/// <remarks>
/// <para>
/// One member per way the client reaches this surface, and no member for a way it does not: the agent is opened from
/// the mail space with nothing named, from a thread, from a calendar event, and from a Discover run whose answer is
/// being carried on. Tasks and People draw no entry into the agent at all, so neither is a scope.
/// </para>
/// <para>
/// It is a closed set rather than a free string because the scope is what narrows a run's reading, and a value nothing
/// declared would be a narrowing nobody wrote a reader for. A member is added by a source change that reaches a review,
/// and the name rather than the ordinal is what the record carries.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<AgentScopeKind>))]
public enum AgentScopeKind
{
    /// <summary>Everything the person's accounts hold, which is what a question asked from the agent itself is about.</summary>
    Mailbox = 0,

    /// <summary>One conversation of mail.</summary>
    Thread = 1,

    /// <summary>One entry in the person's calendar.</summary>
    CalendarEvent = 2,

    /// <summary>One Discover run, whose answer the person is carrying on from.</summary>
    DiscoveryRun = 3,
}
