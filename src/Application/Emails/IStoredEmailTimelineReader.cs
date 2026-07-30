// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Emails;

namespace MailMcp.Application.Emails;

/// <summary>Reads one bounded page of locally stored email summaries in timeline order.</summary>
/// <remarks>
/// <para>
/// The port is read-only and joins no transaction, per
/// <see href="../../../docs/decisions/0001-application-owned-repositories-for-persistence-ports.md">ADR 0001</see>: it
/// takes no persistence session, because a read has nothing to participate in.
/// </para>
/// <para>
/// It returns the projection <see cref="EmailSummary" /> describes and never an entity, a tracked graph, or raw MIME.
/// That is a privacy control as much as a performance one, so an implementation selects the columns the summary needs
/// and no others; the <c>bytea</c> columns holding stored content are unreachable through this contract by design.
/// </para>
/// </remarks>
public interface IStoredEmailTimelineReader
{
    /// <summary>Reads the emails a filter selects, beginning after a known timeline position.</summary>
    /// <param name="filter">Which emails to return and from which end of the timeline to read them.</param>
    /// <param name="continueAfter">The position the previous page ended on, or <see langword="null" /> to start at the filter's leading end.</param>
    /// <param name="limit">The greatest number of rows to return, at least one.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>
    /// At most <paramref name="limit" /> summaries, ordered by <see cref="EmailTimelinePosition" /> in the filter's
    /// direction and beginning strictly beyond <paramref name="continueAfter" />.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="filter" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="limit" /> is below one.</exception>
    /// <remarks>
    /// The order is total, which is what makes paging over it contiguous: the caller may ask for the next page at any
    /// time, and every row falls on exactly one side of <paramref name="continueAfter" /> even when several emails share
    /// a received timestamp or carry none. An implementation reproduces
    /// <see cref="EmailTimelinePosition.ComparerFor" /> rather than an order that merely resembles it.
    /// </remarks>
    Task<IReadOnlyList<EmailSummary>> ReadPageAsync(
        EmailTimelineFilter filter,
        EmailTimelinePosition? continueAfter,
        int limit,
        CancellationToken cancellationToken);
}
