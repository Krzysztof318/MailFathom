// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Agent.Conversations;

/// <summary>What a conversation may grow to, and what one read of it may return.</summary>
/// <remarks>
/// Stated once here rather than at each boundary that enforces one, so a route, the composition, and the store hold a
/// conversation to the same shape. None of these is a policy an operator sets: they bound what this deployment stores
/// and hands back, which is the same question whatever the deployment is for.
/// </remarks>
public static class AgentConversationBounds
{
    /// <summary>The longest name a conversation may be given.</summary>
    /// <remarks>A line in a history list rather than a summary of the conversation, and the agent composes it from the first question.</remarks>
    public const int MaximumTitleLength = 120;

    /// <summary>The greatest number of entries one conversation may hold.</summary>
    /// <remarks>
    /// A conversation is durable and never swept, so without a ceiling one would grow until reading it stopped being
    /// possible. Reaching it means this conversation is full rather than that anything failed: what the person does
    /// next is start another, and the reply that says so belongs to whichever surface they reached this through. The
    /// last <see cref="PlacesKeptForAnEnding" /> places are kept for an answer's ending, so an answer running when the
    /// conversation fills can still end.
    /// </remarks>
    public const int MaximumEntries = 5_000;

    /// <summary>How many of a conversation's last places only an answer's ending, and the agent's note after a stop, may take.</summary>
    /// <remarks>Two because a stop writes two entries: the ending, and the agent saying where to pick it up.</remarks>
    public const int PlacesKeptForAnEnding = 2;

    /// <summary>The greatest number of entries one read may return.</summary>
    /// <remarks>
    /// A read from a cursor returns what has been written since, which is a handful of entries during a run and the
    /// whole conversation on a first read of a long one. The bound is what keeps the second case from being unbounded;
    /// a reader that reaches it advances its cursor and reads again, which is the same call it was already making.
    /// </remarks>
    public const int MaximumEntriesPerRead = 250;

    /// <summary>The greatest number of conversations one person may hold at once.</summary>
    /// <remarks>
    /// A client names a new conversation by an identifier of its own choosing, so without a ceiling one grant could grow
    /// the table by asking under fresh identifiers indefinitely. Reaching it refuses a new conversation and nothing
    /// else: every conversation already held stays readable and answerable, and deleting one makes room for the next.
    /// </remarks>
    public const int MaximumConversations = 1_000;

    /// <summary>The greatest number of conversations one listing may return.</summary>
    public const int MaximumConversationsPerListing = 100;
}
