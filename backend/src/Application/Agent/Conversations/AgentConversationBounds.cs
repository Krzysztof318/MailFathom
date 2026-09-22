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

    /// <summary>The greatest number of entries of the visible history one conversation may hold.</summary>
    /// <remarks>
    /// <para>
    /// A conversation is durable and never swept, so without a ceiling one would grow until reading it stopped being
    /// possible. Reaching it means this conversation is full rather than that anything failed: what the person does
    /// next is start another, and the reply that says so belongs to whichever surface they reached this through. The
    /// last <see cref="PlacesKeptForAnEnding" /> places are kept for an answer's ending, so an answer running when the
    /// conversation fills can still end.
    /// </para>
    /// <para>
    /// <strong>It counts what a person can read back and nothing else.</strong> The record also holds the technical
    /// history — every tool call and tool result a run made, what a call was charged, and every summary a compaction
    /// produced — and those arrive several times faster than anything a person sees. Counted under one number with the
    /// visible entries, they would make a conversation full while compaction, the mechanism built to keep a long one
    /// going, was working perfectly. So this is one of two ceilings rather than one ceiling raised: it bounds what a
    /// person can keep working in, which is the same length it always was, and <see cref="MaximumRecordedEntries" />
    /// bounds what the record holds underneath it. Sweeping the technical entries behind the newest summary instead was
    /// refused, being exactly the deletion an append-only record forbids and taking with it what a turn's model input
    /// is rebuilt from.
    /// </para>
    /// <para>
    /// Both are enforced in the statement that writes an entry, each against a counter that statement advances, so no
    /// caller can write past either by forgetting to check.
    /// </para>
    /// </remarks>
    public const int MaximumEntries = 5_000;

    /// <summary>The greatest number of entries one conversation's record may hold, both readings together.</summary>
    /// <remarks>
    /// <para>
    /// The second ceiling beside <see cref="MaximumEntries" />, and the one the technical history is held to. Ten entries
    /// in the record for every one a person can read is the room it states: a run writes a call and a result for every
    /// tool it uses and a charge for its first call, which is a handful of technical entries for the status line, the
    /// sources, the blocks, and the ending the same run writes visibly, and a compaction adds one more now and then.
    /// A conversation whose tool traffic outruns that ratio fills on this ceiling instead, which the person meets as a
    /// conversation that is full, in exactly the terms the other ceiling uses.
    /// </para>
    /// <para>
    /// The same last <see cref="PlacesKeptForAnEnding" /> places are kept here as on the visible ceiling, so an answer
    /// running when either fills can still end.
    /// </para>
    /// </remarks>
    public const int MaximumRecordedEntries = MaximumEntries * 10;

    /// <summary>How many of a conversation's last places only an answer's ending, and the agent's note after a stop, may take.</summary>
    /// <remarks>Two because a stop writes two entries: the ending, and the agent saying where to pick it up. Kept on both ceilings.</remarks>
    public const int PlacesKeptForAnEnding = 2;

    /// <summary>The greatest number of entries one read may return.</summary>
    /// <remarks>
    /// <para>
    /// A read from a cursor returns what has been written since, which is a handful of entries during a run and the
    /// whole conversation on a first read of a long one. The bound is what keeps the second case from being unbounded;
    /// a reader that reaches it advances its cursor and reads again, which is the same call it was already making.
    /// </para>
    /// <para>
    /// It counts the entries of the reading asked for. A read of the visible history passes over the technical entries
    /// in the database rather than after it, so a page holds this many visible entries wherever the history still has
    /// them, never comes back empty while more is written, and ends on a visible entry whose place is the cursor the
    /// reader holds next — so a conversation whose order is mostly tool traffic reads exactly as one that holds none.
    /// </para>
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
