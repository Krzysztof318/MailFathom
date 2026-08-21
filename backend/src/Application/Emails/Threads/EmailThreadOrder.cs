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
    /// <param name="visibleEmails">The conversation's messages the caller may see, in any order.</param>
    /// <returns>The messages in the conversation's order, each carrying its position and the message it answers.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="visibleEmails" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A message whose recorded parent is not among the ones shown becomes a root of what is shown, which is both the
    /// answer for a withheld parent and the answer for an ancestor this deployment never stored. Every message given is
    /// returned exactly once, including the ones behind a reply cycle no walk could reach the end of: one member of the
    /// cycle is emitted as a root and everything else keeps the message it answers, so a conversation is never published
    /// with a message silently missing from it and never loses a reply relation the cycle did not close.
    /// </remarks>
    public static IReadOnlyList<PlacedThreadedEmail> Of(IReadOnlyList<ThreadedEmailSummary> visibleEmails)
    {
        ArgumentNullException.ThrowIfNull(visibleEmails);

        var shown = visibleEmails.ToDictionary(email => email.StoredEmailId);
        var answered = visibleEmails.ToDictionary(
            email => email.StoredEmailId,
            email => email.ParentStoredEmailId is { } parent && shown.ContainsKey(parent)
                ? parent
                : (StoredEmailId?)null);

        var answers = visibleEmails
            .Where(email => answered[email.StoredEmailId] is not null)
            .GroupBy(email => answered[email.StoredEmailId]!.Value)
            .ToDictionary(group => group.Key, group => Sorted(group));

        var placed = new List<PlacedThreadedEmail>(visibleEmails.Count);
        var emitted = new HashSet<StoredEmailId>();

        foreach (var root in Sorted(visibleEmails.Where(email => answered[email.StoredEmailId] is null)))
        {
            Emit(root, answers, answered, emitted, placed);
        }

        // Anything still unemitted sits in a reply cycle or answers a message that does, and no walk from a root reaches
        // either. Only the cycle is unorderable, so only the cycle is cut: one member of it becomes a root, losing the
        // parent it recorded along with its place, because publishing the edge that closes the loop would hand a reader
        // the cycle this order exists to never contain. Everything hanging off that cycle keeps the parent it recorded
        // and is published under it, because an edge that closes nothing is an edge the relation can still order by. One
        // cut frees a whole component, so the loop runs once per component rather than once per message. Nothing here
        // can produce a cycle: the assembler refuses to write one, so this exists for a chain that reached the database
        // by some other route.
        while (emitted.Count < visibleEmails.Count)
        {
            var stranded = Sorted(visibleEmails.Where(email => !emitted.Contains(email.StoredEmailId)));
            var cut = CutPointOf(stranded[0], shown, answered);

            answered[cut.StoredEmailId] = null;

            Emit(cut, answers, answered, emitted, placed);
        }

        return placed;
    }

    /// <summary>Writes one message and then everything answering it, depth first.</summary>
    /// <remarks>
    /// Depth first rather than by generation, because that is the order a reader follows a conversation in: an answer
    /// belongs directly under what it answers, and the branch it starts is read out before the next sibling begins.
    /// </remarks>
    private static void Emit(
        ThreadedEmailSummary email,
        IReadOnlyDictionary<StoredEmailId, IReadOnlyList<ThreadedEmailSummary>> answers,
        IReadOnlyDictionary<StoredEmailId, StoredEmailId?> answered,
        HashSet<StoredEmailId> emitted,
        List<PlacedThreadedEmail> placed)
    {
        if (!emitted.Add(email.StoredEmailId))
        {
            return;
        }

        placed.Add(new PlacedThreadedEmail(email, placed.Count, answered[email.StoredEmailId]));

        if (!answers.TryGetValue(email.StoredEmailId, out var children))
        {
            return;
        }

        foreach (var child in children)
        {
            Emit(child, answers, answered, emitted, placed);
        }
    }

    /// <summary>Finds the reply cycle keeping a message from being reached, and returns the member the cut falls on.</summary>
    /// <remarks>
    /// A message left unemitted records a parent, because one recording none is a root and was emitted above, and that
    /// parent is unemitted too, because emitting a message emits everything answering it. So walking the recorded
    /// parents from any unemitted message stays inside that set and ends where it started going round. The first message
    /// the walk meets twice is where the cycle begins, and everything walked from there on is the cycle itself; the cut
    /// falls on the earliest of those in the same order the roots were taken in, so which edge is cut does not depend on
    /// which message of the cycle's own component the walk began at.
    /// </remarks>
    private static ThreadedEmailSummary CutPointOf(
        ThreadedEmailSummary stranded,
        Dictionary<StoredEmailId, ThreadedEmailSummary> shown,
        Dictionary<StoredEmailId, StoredEmailId?> answered)
    {
        var walked = new List<ThreadedEmailSummary>();
        var passed = new Dictionary<StoredEmailId, int>();
        var current = stranded;

        while (passed.TryAdd(current.StoredEmailId, walked.Count))
        {
            walked.Add(current);

            current = shown[answered[current.StoredEmailId]!.Value];
        }

        return Sorted(walked.Skip(passed[current.StoredEmailId]))[0];
    }

    /// <summary>Orders messages the reply relation leaves side by side.</summary>
    /// <remarks>
    /// A message nobody could date sorts after every dated one rather than before them, which is the same placement the
    /// mailbox timeline gives an undated message. The identity then settles two messages a sender stamped identically,
    /// which is what makes the comparison total rather than merely usually decisive.
    /// </remarks>
    private static IReadOnlyList<ThreadedEmailSummary> Sorted(IEnumerable<ThreadedEmailSummary> emails) =>
    [
        .. emails
            .OrderBy(email => email.SentAt is null)
            .ThenBy(email => email.SentAt)
            .ThenBy(email => email.StoredEmailId.Value),
    ];
}
