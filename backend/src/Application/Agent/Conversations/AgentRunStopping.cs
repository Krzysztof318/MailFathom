// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Agent.Conversations;

/// <summary>What became of asking an answer being composed to stop.</summary>
public enum AgentRunStopping
{
    /// <summary>The answer was being composed and has now ended as stopped.</summary>
    Stopped = 0,

    /// <summary>The conversation is this person's and that answer is not being composed — it ended already, or never ran here.</summary>
    NotRunning = 1,

    /// <summary>This person holds no conversation under that identifier.</summary>
    NoSuchConversation = 2,
}
