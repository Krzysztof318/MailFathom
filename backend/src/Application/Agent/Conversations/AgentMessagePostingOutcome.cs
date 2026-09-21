// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Agent.Conversations;

/// <summary>How posting a person's message into a conversation went.</summary>
/// <remarks>
/// Every refusal is a statement about the conversation rather than a fault, so each names what the person can do next:
/// wait for or stop the answer being composed, ask afresh rather than steer, or start another conversation.
/// </remarks>
public enum AgentMessagePostingOutcome
{
    /// <summary>The message was written by this post.</summary>
    Written = 0,

    /// <summary>A message under the same identifier was already in the conversation, so this post wrote nothing new.</summary>
    AlreadyWritten = 1,

    /// <summary>This person holds no conversation under that identifier.</summary>
    NoSuchConversation = 2,

    /// <summary>A question arrived while an answer is still being composed, which is steered or stopped rather than asked over.</summary>
    AnswerInProgress = 3,

    /// <summary>An instruction arrived for an answer that is not the one being composed, which has nothing left to steer.</summary>
    NoAnswerInProgress = 4,

    /// <summary>The conversation holds as many entries as one may.</summary>
    ConversationFull = 5,
}
