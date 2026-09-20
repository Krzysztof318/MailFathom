// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Enrichment;

namespace MailFathom.Application.Calendar.Extraction;

/// <summary>Reads text into the calendar events it names: what a message said, and what a person typed.</summary>
/// <remarks>
/// <para>
/// One port and one reading for both, because they are the same operation run against two different inputs. What
/// differs is where the text came from and what the caller does with the answer, and neither of those is a reason for
/// two readings of a date — a second one would be a second set of rules about what <em>Tuesday at three</em> means.
/// </para>
/// <para>
/// <b>Neither half ever produces an event somebody holds.</b> A message's events are written as proposals for a person
/// to accept or dismiss, and a description's draft is handed back to whoever typed it and stored only if they save it.
/// There is no path from here to an asserted event.
/// </para>
/// <para>
/// An implementation is registered for every deployment, including one that reads no text at all — the answer a caller
/// needs is a reason it can act on, and a missing registration would have made every caller carry a null check and
/// decide for itself what an absence meant.
/// </para>
/// <para>
/// It never throws for a provider that failed. Every way an extraction can come to nothing is a member of
/// <see cref="CalendarEventExtractionWithholding" />, because the caller's response to each is the same shape. Caller
/// cancellation is the exception and propagates, being the caller withdrawing the work rather than anything about the
/// text.
/// </para>
/// </remarks>
public interface ICalendarEventExtractor
{
    /// <summary>Gets whether this deployment reads text into calendar events at all.</summary>
    /// <remarks>
    /// Asked so that a pass on a deployment that declined this proposes nothing without composing a turn, and so that
    /// the screen offering a description field can be told there is nothing behind it before anybody types.
    /// </remarks>
    bool IsActive { get; }

    /// <summary>Reads one message into the events it names, none of which anybody has agreed to.</summary>
    /// <param name="email">The message and the passages the reading is shown.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The events the message named, which is empty where it named none, or the reason nothing was read.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="email" /> is <see langword="null" />.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <remarks>
    /// It is shown the message the enrichment pass composed rather than one of its own, because the two readings want
    /// exactly the same thing of a message — its subject, when it arrived, and the opening of its text — and a second
    /// selection would be a second query over the same rows for the same columns.
    /// </remarks>
    Task<CalendarEventExtraction> ProposeFromEmailAsync(
        EnrichableEmail email,
        CancellationToken cancellationToken);

    /// <summary>Reads one typed sentence into the single event it describes.</summary>
    /// <param name="description">The sentence, and the instant whoever typed it is standing on.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The one event the sentence described, or none where it described nothing readable as one.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="description" /> is <see langword="null" />.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <remarks>
    /// At most one event, which is what separates this half from the other: somebody describing a meeting is
    /// describing one, and a sentence read into three would fill a calendar with two nobody meant.
    /// </remarks>
    Task<CalendarEventExtraction> DraftFromDescriptionAsync(
        CalendarEventDescription description,
        CancellationToken cancellationToken);
}
