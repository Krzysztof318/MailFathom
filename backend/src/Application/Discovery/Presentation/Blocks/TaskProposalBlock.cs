// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Discovery.Presentation.Blocks;

/// <summary>A task to be owed, shown with the day it is due so the person can correct it before they take it on.</summary>
/// <remarks>
/// <para>
/// The block for "put that on my list". It is a proposal and never an act: the plan carries what the task would say and
/// when it would be due, and nothing here says anything reached a list.
/// </para>
/// <para>
/// The due day is a day rather than an instant, because that is what a task list holds — a person owes something by
/// Thursday, not by an hour on Thursday. A proposal states nothing about what would announce it: a lead is measured
/// back from the person's own day, and the offset that day runs in is theirs to state rather than a run's to guess.
/// </para>
/// </remarks>
public sealed record TaskProposalBlock : PresentationBlock
{
    /// <summary>Initializes a task to be owed.</summary>
    /// <param name="evidence">What the correspondence does for the proposal.</param>
    /// <param name="title">The line the list would be drawn with.</param>
    /// <param name="dueOn">The day it would be due on, or <see langword="null" /> where the proposal says nothing about when.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="evidence" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="title" /> is the unspecified default.</exception>
    public TaskProposalBlock(PresentationEvidence evidence, PresentationText title, DateOnly? dueOn)
        : base(PresentationBlockType.TaskProposal, evidence)
    {
        PresentationRequirement.Specified(title, nameof(title));

        this.Title = title;
        this.DueOn = dueOn;
    }

    /// <summary>Gets the line the list would be drawn with.</summary>
    public PresentationText Title { get; }

    /// <summary>Gets the day it would be due on, or <see langword="null" /> where the proposal says nothing about when.</summary>
    public DateOnly? DueOn { get; }
}
