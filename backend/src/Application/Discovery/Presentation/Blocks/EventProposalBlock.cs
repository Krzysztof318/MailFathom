// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Discovery.Presentation.Blocks;

/// <summary>A date to be put on the person's calendar, shown with its hour so they can correct it before they agree to it.</summary>
/// <remarks>
/// <para>
/// The block for "put that meeting in my calendar". It is a proposal and never an act: the plan carries when the event
/// would begin and end, and nothing here says anything reached a calendar.
/// </para>
/// <para>
/// It carries an instant rather than a local time because a date read out of mail is only as good as the offset it was
/// written in, and a client draws it in the reader's own. What it does not carry is who is invited: an event this
/// deployment holds is the person's own record of when they are committed, and nothing about it is sent to anybody.
/// </para>
/// </remarks>
public sealed record EventProposalBlock : PresentationBlock
{
    /// <summary>Initializes a date to be put on the calendar.</summary>
    /// <param name="evidence">What the correspondence does for the proposal.</param>
    /// <param name="title">What the event would be called.</param>
    /// <param name="start">When it would begin.</param>
    /// <param name="end">When it would end, or <see langword="null" /> where the proposal states no end.</param>
    /// <param name="isAllDay">Whether it is proposed as a day rather than as a clock time.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="evidence" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="title" /> is the unspecified default, or when an end is stated that is not after the start.</exception>
    public EventProposalBlock(
        PresentationEvidence evidence,
        PresentationText title,
        DateTimeOffset start,
        DateTimeOffset? end,
        bool isAllDay)
        : base(PresentationBlockType.EventProposal, evidence)
    {
        PresentationRequirement.Specified(title, nameof(title));

        if (end is { } closes && closes <= start)
        {
            throw new ArgumentException("A proposed event ends after it begins.", nameof(end));
        }

        this.Title = title;
        this.Start = start;
        this.End = end;
        this.IsAllDay = isAllDay;
    }

    /// <summary>Gets what the event would be called.</summary>
    public PresentationText Title { get; }

    /// <summary>Gets when it would begin.</summary>
    public DateTimeOffset Start { get; }

    /// <summary>Gets when it would end, or <see langword="null" /> where the proposal states no end.</summary>
    public DateTimeOffset? End { get; }

    /// <summary>Gets whether it is proposed as a day rather than as a clock time.</summary>
    public bool IsAllDay { get; }
}
