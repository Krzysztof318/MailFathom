// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Domain.Emails;

/// <summary>Places one stored email in the total order a mailbox timeline is read and paged over.</summary>
/// <param name="ReceivedAt">When the last receiving hop recorded the message, or <see langword="null" /> when no header carried a usable date.</param>
/// <param name="StoredEmailId">The stable local identity that breaks ties between messages sharing a timestamp.</param>
/// <remarks>
/// <para>
/// Keyset pagination needs a total order, not merely a sort: two pages are contiguous only when every row falls on
/// exactly one side of the key the previous page ended on. A received timestamp alone is not total, because a mail
/// server can record several messages within the same instant, so the local identity is part of the position rather
/// than a decoration on it.
/// </para>
/// <para>
/// This type is the single statement of that order. The timeline indexes on <c>stored_emails</c> are configured to
/// match it column for column, so a query planned against the index returns rows in the order
/// <see cref="NewestFirst" /> describes rather than in one that merely resembles it.
/// </para>
/// </remarks>
public readonly record struct EmailTimelinePosition(DateTimeOffset? ReceivedAt, StoredEmailId StoredEmailId)
{
    /// <summary>Gets the comparer that orders positions the way a mailbox timeline is read: newest message first.</summary>
    /// <remarks>
    /// A message whose received timestamp is unknown sorts after every message that has one, because the alternative
    /// puts mail nobody can date above today's mail forever. PostgreSQL orders nulls first under <c>DESC</c>, so the
    /// timeline indexes spell out <c>NULLS LAST</c> to reproduce this decision rather than inherit the opposite one.
    /// </remarks>
    public static IComparer<EmailTimelinePosition> NewestFirst { get; } = new NewestFirstComparer();

    /// <summary>Orders two positions oldest first, which <see cref="NewestFirst" /> reverses.</summary>
    private static int CompareOldestFirst(EmailTimelinePosition left, EmailTimelinePosition right)
    {
        var byReceivedAt = CompareReceivedAt(left.ReceivedAt, right.ReceivedAt);

        return byReceivedAt != 0
            ? byReceivedAt
            : CompareInStoredOrder(left.StoredEmailId.Value, right.StoredEmailId.Value);
    }

    /// <summary>Treats an unknown received timestamp as older than every known one.</summary>
    private static int CompareReceivedAt(DateTimeOffset? left, DateTimeOffset? right) => (left, right) switch
    {
        (null, null) => 0,
        (null, _) => -1,
        (_, null) => 1,
        ({ } leftValue, { } rightValue) => leftValue.CompareTo(rightValue),
    };

    /// <summary>Compares two identifiers as the sixteen big-endian octets PostgreSQL orders a <c>uuid</c> column by.</summary>
    /// <remarks>
    /// The octet order is written out rather than delegated to <see cref="Guid.CompareTo(Guid)" />, which agrees with it
    /// on this runtime but documents only that its result is suitable for sorting — not that it is the order a
    /// <c>uuid</c> column uses. A page boundary computed here is resumed from by a query the index plans, so the
    /// tiebreaker has to be the index's order by construction: one that merely happened to agree would skip or repeat
    /// rows the day it stopped agreeing.
    /// </remarks>
    private static int CompareInStoredOrder(Guid left, Guid right)
    {
        Span<byte> leftOctets = stackalloc byte[16];
        Span<byte> rightOctets = stackalloc byte[16];

        _ = left.TryWriteBytes(leftOctets, bigEndian: true, out _);
        _ = right.TryWriteBytes(rightOctets, bigEndian: true, out _);

        return leftOctets.SequenceCompareTo(rightOctets);
    }

    private sealed class NewestFirstComparer : IComparer<EmailTimelinePosition>
    {
        public int Compare(EmailTimelinePosition x, EmailTimelinePosition y) => CompareOldestFirst(y, x);
    }
}
