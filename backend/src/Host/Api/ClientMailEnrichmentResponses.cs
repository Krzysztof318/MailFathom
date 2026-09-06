// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Enrichment;

namespace MailFathom.Host.Api;

/// <summary>What a derivation concluded about one message, as a list row draws it.</summary>
/// <param name="DerivedAt">When the derivation ran.</param>
/// <param name="Marks">What it concluded, which is empty where it found nothing to say.</param>
/// <remarks>
/// The whole object is absent for a message no derivation has reached, so a client tells *nothing has been derived yet*
/// from *a derivation had nothing to say* without a third field saying which — the first is a missing object and the
/// second an empty array.
/// </remarks>
internal sealed record ClientMailEnrichmentResponse(DateTimeOffset DerivedAt, IReadOnlyList<ClientMailMarkResponse> Marks)
{
    /// <summary>Describes one message's derivation for the wire.</summary>
    /// <param name="enrichment">What was derived, or <see langword="null" /> where nothing has been.</param>
    /// <returns>The response body, or <see langword="null" />.</returns>
    internal static ClientMailEnrichmentResponse? For(EmailEnrichment? enrichment) =>
        enrichment is null
            ? null
            : new ClientMailEnrichmentResponse(
                enrichment.DerivedAt,
                [.. enrichment.Marks.Select(ClientMailMarkResponse.For)]);
}

/// <summary>One reading of a message, with what backs it and what produced it.</summary>
/// <param name="Aspect">Which of the three readings this is.</param>
/// <param name="Text">The reading itself, which is the sentence the row draws.</param>
/// <param name="Reason">Why the producer says it, which is what somebody checks the reading against.</param>
/// <param name="DueAt">When the commitment falls due, or <see langword="null" /> on any other aspect and on a commitment that named no date.</param>
/// <param name="Source">Whether a deterministic rule or a model produced the reading.</param>
/// <param name="Origin">What within that source produced it: a rule identity, or the name the agent was composed under.</param>
/// <param name="Evidence">The passages the reading rests on, in the order the producer named them.</param>
/// <remarks>
/// <para>
/// The evidence is published as passage identifiers rather than as text, because following one is a request of its own:
/// the citation route resolves exactly these identifiers against exactly this message, and a row that carried the
/// passages themselves would be a list page publishing a body it had no reason to.
/// </para>
/// <para>
/// The source and the origin are published beside the reading rather than folded into it, because the product requires
/// somebody to see what a model did and what a deterministic rule did. A client that drew the two alike would be
/// showing a verdict without its standing.
/// </para>
/// </remarks>
internal sealed record ClientMailMarkResponse(
    EmailEnrichmentAspect Aspect,
    string Text,
    string Reason,
    DateTimeOffset? DueAt,
    EmailEnrichmentSource Source,
    string Origin,
    IReadOnlyList<Guid> Evidence)
{
    /// <summary>Describes one reading for the wire.</summary>
    /// <param name="mark">The mark the read returned.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="mark" /> is <see langword="null" />.</exception>
    internal static ClientMailMarkResponse For(EmailEnrichmentMark mark)
    {
        ArgumentNullException.ThrowIfNull(mark);

        return new ClientMailMarkResponse(
            mark.Aspect,
            mark.Text,
            mark.Reason,
            mark.DueAt,
            mark.Provenance.Source,
            mark.Provenance.Origin,
            [.. mark.Evidence.Select(static passage => passage.Value)]);
    }
}
