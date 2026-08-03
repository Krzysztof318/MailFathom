// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>An in-memory stand-in for the stored email timeline, holding the summaries a test arranged.</summary>
/// <remarks>
/// <para>
/// The fake applies the filters and the keyset boundary itself, which is what lets a test assert what a page contains
/// rather than only which filter was forwarded. Its ordering comes from <see cref="EmailTimelinePosition" /> — production
/// code, not a second implementation of the contract — so a test that pages over this timeline is asserting against the
/// same total order PostgreSQL is configured to produce.
/// </para>
/// <para>
/// A <c>Cc</c> list is carried beside each summary because a summary does not publish one, and the recipient filter
/// matches the <c>To</c> and <c>Cc</c> addresses together. Whether the translated query reaches both array columns is a
/// question only the integration suite can answer; what this fake proves is that the use case forwards a recipient
/// filter that means both.
/// </para>
/// </remarks>
internal sealed class InMemoryStoredEmailTimeline : IStoredEmailTimelineReader
{
    private readonly List<InMemoryStoredEmail> entries = [];

    private readonly List<ReadPageCall> calls = [];

    /// <summary>Gets what each call to the port asked for, in order.</summary>
    public IReadOnlyList<ReadPageCall> Calls => this.calls;

    /// <summary>Adds one email to the timeline.</summary>
    /// <param name="summary">The summary a page would return.</param>
    /// <param name="ccAddresses">The comparison forms of the <c>Cc</c> addresses, which the summary does not publish.</param>
    /// <returns>This timeline, so arrangement reads as one statement.</returns>
    public InMemoryStoredEmailTimeline With(EmailSummary summary, params string[] ccAddresses)
    {
        this.entries.Add(new InMemoryStoredEmail(summary, ccAddresses));

        return this;
    }

    /// <summary>Adds several emails to the timeline.</summary>
    /// <param name="summaries">The summaries a page would return.</param>
    /// <returns>This timeline, so arrangement reads as one statement.</returns>
    public InMemoryStoredEmailTimeline WithAll(IEnumerable<EmailSummary> summaries)
    {
        this.entries.AddRange(summaries.Select(summary => new InMemoryStoredEmail(summary, [])));

        return this;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<EmailSummary>> ReadPageAsync(
        EmailTimelineFilter filter,
        EmailTimelinePosition? continueAfter,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        cancellationToken.ThrowIfCancellationRequested();

        this.calls.Add(new ReadPageCall(filter, continueAfter, limit));

        var order = EmailTimelinePosition.ComparerFor(filter.Direction);

        IReadOnlyList<EmailSummary> page =
        [
            .. this.entries
                .Where(entry => entry.Matches(filter.Selection))
                .Select(entry => entry.Summary)
                .Order(new PositionComparer(order))
                .Where(summary => continueAfter is not { } boundary || order.Compare(summary.Position, boundary) > 0)
                .Take(limit),
        ];

        return Task.FromResult(page);
    }

    /// <summary>What one call to the port asked for.</summary>
    /// <param name="Filter">The validated filter the use case built.</param>
    /// <param name="ContinueAfter">The keyset boundary, or <see langword="null" /> for the first page.</param>
    /// <param name="Limit">How many rows the use case asked for.</param>
    internal sealed record ReadPageCall(
        EmailTimelineFilter Filter,
        EmailTimelinePosition? ContinueAfter,
        int Limit);

    private sealed class PositionComparer(IComparer<EmailTimelinePosition> order) : IComparer<EmailSummary>
    {
        public int Compare(EmailSummary? x, EmailSummary? y) => order.Compare(x!.Position, y!.Position);
    }
}
