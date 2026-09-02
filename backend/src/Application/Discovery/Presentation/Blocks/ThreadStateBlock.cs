// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Presentation.Citations;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Discovery.Presentation.Blocks;

/// <summary>Where a conversation stands: what was agreed, what is still open, and who owes what.</summary>
/// <remarks>
/// <para>
/// The block for "where did we leave this". Three lists rather than one, because the three read differently and a
/// reader scanning for what they still owe should not have to find it among what was settled.
/// </para>
/// <para>
/// Any of the three may legitimately be empty — a thread that agreed nothing yet is an ordinary thread — so the block
/// requires participants and nothing else. What it refuses is all three being empty at once, which is a block that
/// presents a heading over nothing.
/// </para>
/// </remarks>
public sealed record ThreadStateBlock : PresentationBlock
{
    /// <summary>The greatest number of statements one of the three lists may hold.</summary>
    public const int MaxStatements = 25;

    /// <summary>The greatest number of participants one block may name.</summary>
    public const int MaxParticipants = 30;

    /// <summary>Initializes where a conversation stands.</summary>
    /// <param name="evidence">What the correspondence does for the block as a whole.</param>
    /// <param name="participants">Who is taking part.</param>
    /// <param name="agreements">What the conversation settled.</param>
    /// <param name="openQuestions">What it has not settled.</param>
    /// <param name="commitments">What somebody undertook to do.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="evidence" /> or any of the four lists is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when no participant is named, a list is oversized, or all three statement lists are empty.</exception>
    public ThreadStateBlock(
        PresentationEvidence evidence,
        IReadOnlyList<ThreadParticipant> participants,
        IReadOnlyList<ThreadStatement> agreements,
        IReadOnlyList<ThreadStatement> openQuestions,
        IReadOnlyList<ThreadCommitment> commitments)
        : base(PresentationBlockType.ThreadState, evidence)
    {
        this.Participants = PresentationRequirement.RequiredItems(participants, MaxParticipants, nameof(participants));
        this.Agreements = PresentationRequirement.OptionalItems(agreements, MaxStatements, nameof(agreements));
        this.OpenQuestions = PresentationRequirement.OptionalItems(openQuestions, MaxStatements, nameof(openQuestions));
        this.Commitments = PresentationRequirement.OptionalItems(commitments, MaxStatements, nameof(commitments));

        if (this.Agreements.Count + this.OpenQuestions.Count + this.Commitments.Count == 0)
        {
            throw new ArgumentException(
                "A thread state says what was agreed, what is open, or what was undertaken; a block saying none of the three presents nothing.",
                nameof(agreements));
        }
    }

    /// <summary>Gets who is taking part.</summary>
    public IReadOnlyList<ThreadParticipant> Participants { get; }

    /// <summary>Gets what the conversation settled.</summary>
    public IReadOnlyList<ThreadStatement> Agreements { get; }

    /// <summary>Gets what it has not settled.</summary>
    public IReadOnlyList<ThreadStatement> OpenQuestions { get; }

    /// <summary>Gets what somebody undertook to do.</summary>
    public IReadOnlyList<ThreadCommitment> Commitments { get; }

    /// <inheritdoc />
    public override IEnumerable<PresentationCitationId> ReferencedCitations => base.ReferencedCitations
        .Concat(this.Agreements.SelectMany(statement => statement.Sources))
        .Concat(this.OpenQuestions.SelectMany(statement => statement.Sources))
        .Concat(this.Commitments.SelectMany(commitment => commitment.Sources));
}

/// <summary>Somebody taking part in a conversation.</summary>
public sealed record ThreadParticipant
{
    /// <summary>Initializes one participant.</summary>
    /// <param name="displayName">The name a reader recognizes them by.</param>
    /// <param name="address">Their address, or <see langword="null" /> where the conversation names them without one.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="displayName" /> is the unspecified default.</exception>
    public ThreadParticipant(PresentationText displayName, EmailAddress? address)
    {
        PresentationRequirement.Specified(displayName, nameof(displayName));

        this.DisplayName = displayName;
        this.Address = address;
    }

    /// <summary>Gets the name a reader recognizes them by.</summary>
    public PresentationText DisplayName { get; }

    /// <summary>Gets their address, or <see langword="null" /> where the conversation names them without one.</summary>
    public EmailAddress? Address { get; }
}

/// <summary>One thing a conversation settled or left open.</summary>
public sealed record ThreadStatement
{
    /// <summary>Initializes one statement.</summary>
    /// <param name="text">What was settled or left open.</param>
    /// <param name="sources">The citations this statement rests on.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sources" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the text is the unspecified default, or a citation is unspecified or named twice.</exception>
    public ThreadStatement(PresentationText text, IReadOnlyList<PresentationCitationId> sources)
    {
        PresentationRequirement.Specified(text, nameof(text));

        this.Text = text;
        this.Sources = PresentationRequirement.Sources(sources, nameof(sources));
    }

    /// <summary>Gets what was settled or left open.</summary>
    public PresentationText Text { get; }

    /// <summary>Gets the citations this statement rests on.</summary>
    public IReadOnlyList<PresentationCitationId> Sources { get; }
}

/// <summary>Something somebody undertook to do, and when they said they would.</summary>
/// <remarks>
/// The owner is optional because a commitment is often made without naming who keeps it — "we will send the revised
/// figures" — and inventing a name for it would be the assistant asserting something the mail did not.
/// </remarks>
public sealed record ThreadCommitment
{
    /// <summary>Initializes one commitment.</summary>
    /// <param name="text">What was undertaken.</param>
    /// <param name="owedBy">Who undertook it, or <see langword="null" /> where the correspondence does not say.</param>
    /// <param name="dueAt">When it was said to be due, or <see langword="null" /> where nothing says.</param>
    /// <param name="sources">The citations this commitment rests on.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sources" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the text is the unspecified default, or a citation is unspecified or named twice.</exception>
    public ThreadCommitment(
        PresentationText text,
        ThreadParticipant? owedBy,
        DateTimeOffset? dueAt,
        IReadOnlyList<PresentationCitationId> sources)
    {
        PresentationRequirement.Specified(text, nameof(text));

        this.Text = text;
        this.OwedBy = owedBy;
        this.DueAt = dueAt;
        this.Sources = PresentationRequirement.Sources(sources, nameof(sources));
    }

    /// <summary>Gets what was undertaken.</summary>
    public PresentationText Text { get; }

    /// <summary>Gets who undertook it, or <see langword="null" /> where the correspondence does not say.</summary>
    public ThreadParticipant? OwedBy { get; }

    /// <summary>Gets when it was said to be due, or <see langword="null" /> where nothing says.</summary>
    public DateTimeOffset? DueAt { get; }

    /// <summary>Gets the citations this commitment rests on.</summary>
    public IReadOnlyList<PresentationCitationId> Sources { get; }
}
