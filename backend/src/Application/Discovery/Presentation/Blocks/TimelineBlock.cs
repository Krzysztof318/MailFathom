// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Presentation.Citations;

namespace MailFathom.Application.Discovery.Presentation.Blocks;

/// <summary>How something changed over time, as the dated events the correspondence records.</summary>
/// <remarks>
/// The block for a question about a course of events rather than a state — "how did the price move", "when did this
/// start going wrong". The entries are presented in the order the producer gave, which is the order the answer is read
/// in; a client sorts nothing, because a producer that ordered by when something was agreed rather than by when it was
/// mentioned has said something a date column cannot.
/// </remarks>
public sealed record TimelineBlock : PresentationBlock
{
    /// <summary>The greatest number of entries one timeline may hold.</summary>
    public const int MaxEntries = 50;

    /// <summary>Initializes a course of events.</summary>
    /// <param name="evidence">What the correspondence does for the timeline as a whole.</param>
    /// <param name="entries">The entries, in the order the answer is read in.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="evidence" /> or <paramref name="entries" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when there are no entries or more than <see cref="MaxEntries" /> of them.</exception>
    public TimelineBlock(PresentationEvidence evidence, IReadOnlyList<TimelineEntry> entries)
        : base(PresentationBlockType.Timeline, evidence) =>
        this.Entries = PresentationRequirement.RequiredItems(entries, MaxEntries, nameof(entries));

    /// <summary>Gets the entries, in the order the answer is read in.</summary>
    public IReadOnlyList<TimelineEntry> Entries { get; }

    /// <inheritdoc />
    public override IEnumerable<PresentationCitationId> ReferencedCitations =>
        base.ReferencedCitations.Concat(this.Entries.SelectMany(entry => entry.Sources));
}

/// <summary>One dated event on a timeline.</summary>
/// <remarks>The subject is what the event happened to — the matter, the document, or the thread it belongs to.</remarks>
public sealed record TimelineEntry
{
    /// <summary>Initializes one dated event.</summary>
    /// <param name="occurredAt">When the event happened, as the correspondence dates it.</param>
    /// <param name="summary">What happened.</param>
    /// <param name="subject">What it happened to.</param>
    /// <param name="sources">The citations this entry rests on.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sources" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when a text is the unspecified default, or a citation is unspecified or named twice.</exception>
    public TimelineEntry(
        DateTimeOffset occurredAt,
        PresentationText summary,
        PresentationText subject,
        IReadOnlyList<PresentationCitationId> sources)
    {
        PresentationRequirement.Specified(summary, nameof(summary));
        PresentationRequirement.Specified(subject, nameof(subject));

        this.OccurredAt = occurredAt;
        this.Summary = summary;
        this.Subject = subject;
        this.Sources = PresentationRequirement.Sources(sources, nameof(sources));
    }

    /// <summary>Gets when the event happened, as the correspondence dates it.</summary>
    public DateTimeOffset OccurredAt { get; }

    /// <summary>Gets what happened.</summary>
    public PresentationText Summary { get; }

    /// <summary>Gets what it happened to.</summary>
    public PresentationText Subject { get; }

    /// <summary>Gets the citations this entry rests on, which may be none where nothing backs it.</summary>
    public IReadOnlyList<PresentationCitationId> Sources { get; }
}
