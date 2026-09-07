// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Chunking;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Enrichment;

/// <summary>One reading of a message, with the reason behind it, what it rests on, and what produced it.</summary>
/// <remarks>
/// <para>
/// A mark is never the text alone. The product requires a mark on a list to expand into the evidence and the rule that
/// produced it, so a value without <see cref="Reason" />, <see cref="Evidence" />, and <see cref="Provenance" /> is not
/// something this system stores — which is why they are constructor arguments rather than properties a caller may leave
/// unset.
/// </para>
/// <para>
/// The evidence names passages of the message the mark is about, in the same terms the run surface's citations name
/// them, so the same reader resolves both: a passage identifier and the message it hangs on are what a fragment
/// citation carries, and following one is reading what chunking already derived rather than fetching mail again. A
/// passage the store no longer holds resolves to nothing, which is a mark whose evidence has been re-derived rather
/// than a mark that never had any.
/// </para>
/// <para>
/// The text and the reason are derived from mail and are personal data of the same standing as the message: never
/// logged, never attached to a span, and scanned by whatever egress guard is switched on before either leaves this
/// deployment.
/// </para>
/// </remarks>
public sealed record EmailEnrichmentMark
{
    /// <summary>The greatest length the text of a mark or its reason carries before it is shortened to it.</summary>
    /// <remarks>
    /// About two lines of a list row, which is what a mark exists to be. It shortens rather than refusing, because the
    /// value is what a producer wrote and discarding a whole derivation over a long sentence would leave the row with
    /// nothing; the bound is applied at a text-element boundary so a shortened value never ends inside a surrogate pair.
    /// </remarks>
    public const int MaximumTextLength = 240;

    /// <summary>The greatest number of passages one mark may cite.</summary>
    /// <remarks>
    /// A mark is one sentence about a message, so evidence past a handful of passages is a producer citing the message
    /// rather than the place its claim came from. The leading citations are kept, because a producer names what it
    /// relied on most first.
    /// </remarks>
    public const int MaximumEvidenceCount = 4;

    private EmailEnrichmentMark(
        EmailEnrichmentAspect aspect,
        string text,
        string reason,
        IReadOnlyList<EmailChunkId> evidence,
        EmailEnrichmentProvenance provenance,
        DateTimeOffset? dueAt)
    {
        this.Aspect = aspect;
        this.Text = text;
        this.Reason = reason;
        this.Evidence = evidence;
        this.Provenance = provenance;
        this.DueAt = dueAt;
    }

    /// <summary>Gets which of the three readings this mark carries.</summary>
    public EmailEnrichmentAspect Aspect { get; }

    /// <summary>Gets what the mark says, which is the sentence a row draws.</summary>
    public string Text { get; }

    /// <summary>Gets why the producer says it, which is what a reader checks before believing the mark.</summary>
    public string Reason { get; }

    /// <summary>Gets the passages of the message the mark rests on, in the order the producer named them.</summary>
    public IReadOnlyList<EmailChunkId> Evidence { get; }

    /// <summary>Gets what produced the mark, and what within that source it was.</summary>
    public EmailEnrichmentProvenance Provenance { get; }

    /// <summary>Gets when the commitment falls due, or <see langword="null" /> where the mark names no date.</summary>
    /// <remarks>
    /// Only a <see cref="EmailEnrichmentAspect.Commitment" /> may carry one. A date on either of the other two aspects
    /// would be a value nothing reads and a reader would have to decide the meaning of, so it is refused rather than
    /// ignored.
    /// </remarks>
    public DateTimeOffset? DueAt { get; }

    /// <summary>Records one reading of a message.</summary>
    /// <param name="aspect">Which of the three readings the mark carries.</param>
    /// <param name="text">What the mark says.</param>
    /// <param name="reason">Why the producer says it.</param>
    /// <param name="evidence">The passages the mark rests on, which is never empty.</param>
    /// <param name="provenance">What produced the mark.</param>
    /// <param name="dueAt">When the commitment falls due, for a commitment that names a date.</param>
    /// <returns>The mark, with the text and the reason shortened to <see cref="MaximumTextLength" /> and the evidence to its own bound.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="evidence" /> or <paramref name="provenance" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the text or the reason is blank, when the evidence is empty, or when an aspect other than a commitment carries a date.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="aspect" /> is not a defined member.</exception>
    /// <remarks>
    /// Empty evidence is refused rather than stored, because a mark nothing backs is the thing this record exists to
    /// rule out: it would read on a screen exactly like one a passage supports, and no reader could tell them apart.
    /// </remarks>
    public static EmailEnrichmentMark Create(
        EmailEnrichmentAspect aspect,
        string text,
        string reason,
        IReadOnlyList<EmailChunkId> evidence,
        EmailEnrichmentProvenance provenance,
        DateTimeOffset? dueAt = null)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(provenance);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (!Enum.IsDefined(aspect))
        {
            throw new ArgumentOutOfRangeException(
                nameof(aspect),
                aspect,
                "A mark carries one of the readings this system derives.");
        }

        if (evidence.Count == 0)
        {
            throw new ArgumentException(
                "A mark cites at least one passage of the message it is about.",
                nameof(evidence));
        }

        if (dueAt is not null && aspect is not EmailEnrichmentAspect.Commitment)
        {
            throw new ArgumentException(
                "Only a commitment carries a date.",
                nameof(dueAt));
        }

        return new EmailEnrichmentMark(
            aspect,
            Shortened(text),
            Shortened(reason),
            [.. evidence.Take(MaximumEvidenceCount)],
            provenance,
            dueAt);
    }

    /// <summary>Puts a producer's sentence into the one form a record keeps it in.</summary>
    /// <remarks>
    /// Whitespace collapses first, so a value a producer wrapped across lines is bounded by what it says rather than by
    /// how it was laid out, and the cut then falls on a text-element boundary rather than on a UTF-16 index.
    /// </remarks>
    private static string Shortened(string value)
    {
        var collapsed = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return MailTextBounds.TruncateAtTextElementBoundary(collapsed, MaximumTextLength);
    }
}
