// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Agent.Conversations;

/// <summary>What became of a message a person posted into a conversation.</summary>
/// <param name="Outcome">Whether it was written, had been written already, or was refused, and why.</param>
/// <param name="Reached">The place the conversation stood at once the message was in it, and <c>0</c> where it was refused.</param>
/// <param name="Answer">The answer the message is part of: the one a question opened, or the one an instruction steers — and <see langword="null" /> where there is none.</param>
/// <remarks>
/// A repeated post reports what the first one did rather than a refusal, because the message's identifier is the
/// client's and a post retried over a dropped connection is the same post. Handing back the same answer and the same
/// place is what lets the client carry on as if the first response had arrived.
/// </remarks>
public sealed record AgentMessagePosting(AgentMessagePostingOutcome Outcome, long Reached, AgentMessageId? Answer)
{
    /// <summary>Gets whether the message stands in the conversation, whether this post or an earlier one wrote it.</summary>
    public bool Stands => this.Outcome is AgentMessagePostingOutcome.Written or AgentMessagePostingOutcome.AlreadyWritten;

    /// <summary>Reports a post that wrote nothing, and why.</summary>
    /// <param name="outcome">Why nothing was written.</param>
    /// <returns>The refusal.</returns>
    public static AgentMessagePosting Refused(AgentMessagePostingOutcome outcome) => new(outcome, Reached: 0, Answer: null);
}
