// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.Enrichment;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Calendar;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Calendar.Extraction;

/// <summary>Turns the dates a message named into proposals on the calendars of the people that mailbox serves.</summary>
/// <remarks>
/// <para>
/// The half of the extraction that knows about calendars, kept apart from the pass that runs it so that the pass gains
/// one collaborator rather than four and so that nothing in the enrichment boundary has to know how an event is
/// composed. Reading and writing are separate members for one reason: the read is a provider call and the write joins
/// the caller's transaction, and folding them together would either hold a transaction open across a network call or
/// commit a proposal apart from the record that says the message has been read.
/// </para>
/// <para>
/// A mailbox two people are assigned proposes to both, through the same relation every other per-person fan-out reads.
/// One reading is paid for and each person then decides for themselves, which is the only answer that works in both
/// directions: writing to one of them would pick a person arbitrarily, and reading twice would charge the deployment
/// twice for one message.
/// </para>
/// <para>
/// Nothing here writes an asserted event. Every row it stages is <see cref="CalendarEventOrigin.Proposed" /> and cites
/// the message it came from, so what a person meets is an offer their own act turns into a calendar entry.
/// </para>
/// </remarks>
public sealed class MailCalendarProposals
{
    private readonly ICalendarEventExtractor extractor;
    private readonly ICalendarEventStore events;
    private readonly IMailAccountAssignments assignments;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the proposals over the reading that finds them and the calendars they reach.</summary>
    /// <param name="extractor">Reads one message into the events it names.</param>
    /// <param name="events">Holds the calendars the proposals are written into.</param>
    /// <param name="assignments">Answers which people the mailbox serves.</param>
    /// <param name="timeProvider">Reads when a proposal was written down.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailCalendarProposals(
        ICalendarEventExtractor extractor,
        ICalendarEventStore events,
        IMailAccountAssignments assignments,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.extractor = extractor;
        this.events = events;
        this.assignments = assignments;
        this.timeProvider = timeProvider;
    }

    /// <summary>Gets whether this deployment proposes events from mail at all.</summary>
    public bool IsActive => this.extractor.IsActive;

    /// <summary>Reads one message into the events it names, without writing any of them.</summary>
    /// <param name="email">The message the reading is shown.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The events the message named, or the reason nothing was read.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="email" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Outside any transaction, because it is a provider call: a read held inside one would hold a database connection
    /// for as long as an endpoint takes to answer.
    /// </remarks>
    public Task<CalendarEventExtraction> ReadAsync(EnrichableEmail email, CancellationToken cancellationToken) =>
        this.extractor.ProposeFromEmailAsync(email, cancellationToken);

    /// <summary>Stages what a reading found onto the calendar of each person the mailbox serves.</summary>
    /// <param name="session">The session the writes join.</param>
    /// <param name="account">The mailbox the message arrived in, whose assigned people are proposed to.</param>
    /// <param name="sourceMessage">The message the events were read out of, which every proposal cites.</param>
    /// <param name="proposals">What the reading found, and empty where it found nothing.</param>
    /// <param name="cancellationToken">Cancels the staging.</param>
    /// <returns>How many rows were staged, across every calendar reached.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is <see langword="null" />.</exception>
    /// <remarks>
    /// Staged rather than committed, so that the proposals and the record saying this message has been read become
    /// durable together: a commit that carried one without the other would either propose from a message a later run
    /// reads again or lose the proposals of a message nothing will offer twice.
    /// </remarks>
    public async Task<int> StageAsync(
        IPersistenceSession session,
        MailAccountId account,
        StoredEmailId sourceMessage,
        IReadOnlyList<ExtractedCalendarEvent> proposals,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(proposals);

        if (proposals.Count is 0)
        {
            return 0;
        }

        var recordedAt = this.timeProvider.GetUtcNow();
        var rows = this.assignments.UsersAssignedTo(account)
            .SelectMany(owner => proposals.Select(proposal => (Owner: owner, Proposal: proposal)))
            .ToArray();

        foreach (var (owner, proposal) in rows)
        {
            await this.events.AddAsync(
                session,
                owner,
                Compose(proposal, sourceMessage, recordedAt),
                cancellationToken);
        }

        return rows.Length;
    }

    /// <summary>Composes one proposal as the event it will be held as.</summary>
    /// <remarks>
    /// The identity is minted from the instant the proposal was written, which is what every other record here is
    /// identified by, and it carries no imported identifier because nothing imports a proposal. Both times are the
    /// same instant on a row nobody has amended yet.
    /// <para>
    /// A proposal announces nothing and states a clock time. A reading of mail is not the place either is decided:
    /// what a person wants to be told about is theirs to set once they have agreed to the date, so the reminders are
    /// empty until they open the event and the extraction is never read as a statement about a whole day.
    /// </para>
    /// </remarks>
    private static CalendarEvent Compose(
        ExtractedCalendarEvent proposal,
        StoredEmailId sourceMessage,
        DateTimeOffset recordedAt) =>
        CalendarEvent.Create(
            CalendarEventId.Create(Guid.CreateVersion7(recordedAt)),
            proposal.Title,
            proposal.Start,
            proposal.End,
            isAllDay: false,
            reminders: [],
            CalendarEventOrigin.Proposed,
            sourceMessage,
            importedUid: null,
            recordedAt,
            recordedAt);
}
