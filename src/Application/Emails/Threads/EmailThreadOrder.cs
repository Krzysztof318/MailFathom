// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Threads;

/// <summary>Produces the one order a conversation has, from the messages a caller is allowed to see.</summary>
/// <remarks>
/// <para>
/// A set of messages is not a thread. The order is half of what a thread is, and it is what every reader of one asks for
/// first: which message opened the conversation, which one answers which, and what the last word was.
/// </para>
/// <para>
/// Three things decide it, in this order and for these reasons. The reply relation decides wherever it is known, because
/// it is the only statement about sequence that a sender did not make about themselves. The sent timestamp settles what
/// the relation leaves open — messages answering the same parent — and nothing more, because a <c>Date</c> header is
/// written by a clock this deployment does not control and a misconfigured machine sets it years out. The local identity
/// settles the rest, which is what makes the order total, so two reads of one unchanged conversation never disagree.
/// </para>
/// <para>
/// It is produced on every read rather than stored. Storing a position would mean rewriting every message of a
/// conversation each time one arrived, and the answer is derivable from three values the rows already carry.
/// </para>
/// </remarks>
public static class EmailThreadOrder
{
    /// <summary>Orders the messages a caller is shown, and places each one against the message it answers.</summary>
    /// <param name="visibleMessages">The conversation's messages the caller may see, in any order.</param>
    /// <returns>The messages in the conversation's order, each carrying its position and the message it answers.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="visibleMessages" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A message whose recorded parent is not among the ones shown becomes a root of what is shown, which is both the
    /// answer for a withheld parent and the answer for an ancestor this deployment never stored. Every message given is
    /// returned exactly once, including one caught in a reply cycle no walk could reach the end of: such a message is
    /// emitted as a root, so a conversation is never published with a message silently missing from it.
    /// </remarks>
    public static IReadOnlyList<PlacedEmailThreadMessage> Of(IReadOnlyList<EmailThreadMessage> visibleMessages)
    {
        ArgumentNullException.ThrowIfNull(visibleMessages);

        var shown = visibleMessages.ToDictionary(message => message.StoredEmailId);
        var answered = visibleMessages.ToDictionary(
            message => message.StoredEmailId,
            message => message.ParentStoredEmailId is { } parent && shown.ContainsKey(parent)
                ? parent
                : (StoredEmailId?)null);

        var answers = visibleMessages
            .Where(message => answered[message.StoredEmailId] is not null)
            .GroupBy(message => answered[message.StoredEmailId]!.Value)
            .ToDictionary(group => group.Key, group => Sorted(group));

        var placed = new List<PlacedEmailThreadMessage>(visibleMessages.Count);
        var emitted = new HashSet<StoredEmailId>();

        foreach (var root in Sorted(visibleMessages.Where(message => answered[message.StoredEmailId] is null)))
        {
            Emit(root, answers, answered, emitted, placed);
        }

        // Anything still unemitted sits in a reply cycle, which no walk from a root reaches. The relation cannot order
        // it, so the fallback is the one the relation leaves: each such message becomes a root in the same total order
        // the roots above were taken in. Nothing here can produce a cycle — the assembler refuses to write one — so this
        // exists for a chain that reached the database by some other route.
        foreach (var stranded in Sorted(visibleMessages.Where(message => !emitted.Contains(message.StoredEmailId))))
        {
            Emit(stranded, answers, answered, emitted, placed);
        }

        return placed;
    }

    /// <summary>Writes one message and then everything answering it, depth first.</summary>
    /// <remarks>
    /// Depth first rather than by generation, because that is the order a reader follows a conversation in: an answer
    /// belongs directly under what it answers, and the branch it starts is read out before the next sibling begins.
    /// </remarks>
    private static void Emit(
        EmailThreadMessage message,
        IReadOnlyDictionary<StoredEmailId, IReadOnlyList<EmailThreadMessage>> answers,
        IReadOnlyDictionary<StoredEmailId, StoredEmailId?> answered,
        HashSet<StoredEmailId> emitted,
        List<PlacedEmailThreadMessage> placed)
    {
        if (!emitted.Add(message.StoredEmailId))
        {
            return;
        }

        placed.Add(new PlacedEmailThreadMessage(message, placed.Count, answered[message.StoredEmailId]));

        if (!answers.TryGetValue(message.StoredEmailId, out var children))
        {
            return;
        }

        foreach (var child in children)
        {
            Emit(child, answers, answered, emitted, placed);
        }
    }

    /// <summary>Orders messages the reply relation leaves side by side.</summary>
    /// <remarks>
    /// A message nobody could date sorts after every dated one rather than before them, which is the same placement the
    /// mailbox timeline gives an undated message. The identity then settles two messages a sender stamped identically,
    /// which is what makes the comparison total rather than merely usually decisive.
    /// </remarks>
    private static IReadOnlyList<EmailThreadMessage> Sorted(IEnumerable<EmailThreadMessage> messages) =>
    [
        .. messages
            .OrderBy(message => message.SentAt is null)
            .ThenBy(message => message.SentAt)
            .ThenBy(message => message.StoredEmailId.Value),
    ];
}
