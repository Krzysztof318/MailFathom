// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Application.Agent.Conversations;

/// <summary>How one answer stopped being composed.</summary>
/// <remarks>
/// Every ending leaves what was already written exactly where it is: an answer cut short keeps its blocks and its
/// proposals, and the record says it was cut short rather than pretending it finished or removing what arrived. That is
/// the whole reason the ending is a recorded value instead of the mere absence of anything further — a conversation
/// read a week later has no other way to tell an answer that ended from one whose replica went away.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<AgentAnswerOutcome>))]
public enum AgentAnswerOutcome
{
    /// <summary>The run composed everything it had to say.</summary>
    Completed = 0,

    /// <summary>The person stopped it while it was working.</summary>
    Stopped = 1,

    /// <summary>The run could not carry on, and what it had already composed stands.</summary>
    Failed = 2,
}
