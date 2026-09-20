// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Domain.Tasks;

namespace MailFathom.Application.Emails.Enrichment;

/// <summary>One thing a message asked the person who received it to do, as the derivation read it.</summary>
/// <remarks>
/// <para>
/// It is what the same derivation produces beside its marks rather than a second reading of the message: one call
/// answers what a message is about and what it asks of the person, so proposing a task costs a deployment nothing
/// beyond the enrichment it already runs. That is the whole of why this travels on
/// <see cref="EmailEnrichmentDerivation" /> instead of being asked for separately.
/// </para>
/// <para>
/// It is a reading and not yet a task. What the pass writes from it is a
/// <see cref="PersonalTaskOrigin.Proposed" /> task on the list of each person assigned the mailbox, which nobody owes
/// until they accept it — so nothing here decides that the person has committed to anything, and the value carries no
/// completion, no identity, and no origin to state one with.
/// </para>
/// <para>
/// The title is derived personal data of the same standing as the message: a sentence read out of somebody's mail.
/// It is never logged, never a metric dimension, and never written into a failure message.
/// </para>
/// </remarks>
public sealed record EmailTaskProposal
{
    /// <summary>The greatest number of proposals one message produces.</summary>
    /// <remarks>
    /// A message asking for more than a few things is a message somebody reads rather than a list to be filled from,
    /// and a person offered twenty proposals from one mail has been handed work rather than saved it. The leading ones
    /// are kept, because a derivation names what the message asks most plainly first.
    /// </remarks>
    public const int MaximumPerEmail = 3;

    private EmailTaskProposal(string title, DateOnly? dueOn)
    {
        this.Title = title;
        this.DueOn = dueOn;
    }

    /// <summary>Gets the line the proposed task is offered as.</summary>
    public string Title { get; }

    /// <summary>Gets the day it falls due on, and <see langword="null" /> where the message named none.</summary>
    /// <remarks>
    /// A day rather than an instant, because that is what a task carries: a message naming an hour is naming when a
    /// meeting is, which is the calendar's reading of it rather than this one.
    /// </remarks>
    public DateOnly? DueOn { get; }

    /// <summary>Records one thing a message asked for.</summary>
    /// <param name="title">The line the proposed task is offered as.</param>
    /// <param name="dueOn">The day it falls due on, or <see langword="null" /> where the message named none.</param>
    /// <returns>The proposal, with the title shortened to what a task's title holds.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="title" /> is blank.</exception>
    /// <remarks>
    /// It shortens rather than refusing, for the reason a mark does: the value is what a producer wrote, and discarding
    /// a proposal over a long sentence would leave the person nothing where the message did ask them for something.
    /// The bound is the one a task is stored under, so what is offered is what would be kept.
    /// </remarks>
    public static EmailTaskProposal Create(string title, DateOnly? dueOn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var collapsed = string.Join(' ', title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return new EmailTaskProposal(
            MailTextBounds.TruncateAtTextElementBoundary(collapsed, PersonalTask.MaximumTitleLength),
            dueOn);
    }
}
