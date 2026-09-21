// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Application.Agent.Conversations;

/// <summary>Who wrote one message of a conversation.</summary>
/// <remarks>
/// Two members and no third. A conversation here is between one person and the deployment's own agent: nothing is
/// assigned to anybody else, no second person joins, and there is no system voice beside the agent's — what the
/// deployment has to say about a run it cut short is said as the agent, because that is whose run it was.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<AgentMessageAuthor>))]
public enum AgentMessageAuthor
{
    /// <summary>The person whose conversation it is.</summary>
    Person = 0,

    /// <summary>The deployment's agent, answering them.</summary>
    Agent = 1,
}
