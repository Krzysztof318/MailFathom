// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Presentation.Citations;

namespace MailFathom.Application.Discovery.Presentation.Blocks;

/// <summary>The messages themselves, where the correspondence rather than a summary of it is the answer.</summary>
/// <remarks>
/// The block for "show me what they sent about it". Each entry names one source and the part of it worth reading, so a
/// person can judge the mail rather than the assistant's reading of it. Ordering is the producer's and is presented as
/// given; the relevance beside each entry is what it was ordered by.
/// </remarks>
public sealed record EvidenceListBlock : PresentationBlock
{
    /// <summary>The greatest number of entries one list may hold.</summary>
    public const int MaxEntries = 50;

    /// <summary>Initializes the messages an answer rests on.</summary>
    /// <param name="evidence">What the correspondence does for the list as a whole.</param>
    /// <param name="entries">The entries, most worth reading first.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="evidence" /> or <paramref name="entries" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when there are no entries or more than <see cref="MaxEntries" /> of them.</exception>
    public EvidenceListBlock(PresentationEvidence evidence, IReadOnlyList<EvidenceEntry> entries)
        : base(PresentationBlockType.EvidenceList, evidence) =>
        this.Entries = PresentationRequirement.RequiredItems(entries, MaxEntries, nameof(entries));

    /// <summary>Gets the entries, most worth reading first.</summary>
    public IReadOnlyList<EvidenceEntry> Entries { get; }

    /// <inheritdoc />
    public override IEnumerable<PresentationCitationId> ReferencedCitations =>
        base.ReferencedCitations.Concat(this.Entries.Select(entry => entry.Source));
}

/// <summary>One message an answer rests on, and the part of it worth reading.</summary>
/// <remarks>
/// The fragment is quoted from the message rather than written about it, which is why it is the one text in the
/// catalogue a reader can hold against the source and expect to find word for word.
/// </remarks>
public sealed record EvidenceEntry
{
    /// <summary>Initializes one message an answer rests on.</summary>
    /// <param name="source">The citation the entry presents.</param>
    /// <param name="fragment">The part of the message worth reading.</param>
    /// <param name="relevance">How well the entry answers the question, between <c>0</c> and <c>1</c> inclusive.</param>
    /// <param name="freshness">How current the local copy of this message was.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="freshness" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the source or the fragment is the unspecified default.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="relevance" /> is outside <c>0</c> to <c>1</c>.</exception>
    public EvidenceEntry(
        PresentationCitationId source,
        PresentationText fragment,
        double relevance,
        PresentationFreshness freshness)
    {
        ArgumentNullException.ThrowIfNull(freshness);
        ArgumentOutOfRangeException.ThrowIfNegative(relevance);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(relevance, 1d);
        PresentationRequirement.Specified(fragment, nameof(fragment));

        if (!source.IsSpecified)
        {
            throw new ArgumentException("An evidence entry names the citation it presents.", nameof(source));
        }

        this.Source = source;
        this.Fragment = fragment;
        this.Relevance = relevance;
        this.Freshness = freshness;
    }

    /// <summary>Gets the citation the entry presents.</summary>
    public PresentationCitationId Source { get; }

    /// <summary>Gets the part of the message worth reading, quoted from it.</summary>
    public PresentationText Fragment { get; }

    /// <summary>Gets how well the entry answers the question, between <c>0</c> and <c>1</c> inclusive.</summary>
    public double Relevance { get; }

    /// <summary>Gets how current the local copy of this message was.</summary>
    public PresentationFreshness Freshness { get; }
}
