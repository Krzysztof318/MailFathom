// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Rendering;

/// <summary>What one rendering may produce, and how much of it a reader may be handed.</summary>
/// <param name="IncludeSanitizedHtml">Whether to also produce the sanitized HTML representation.</param>
/// <param name="MaxCharactersPerRepresentation">The bound every representation is subject to, whatever else the call asked for.</param>
/// <param name="RemainingCharactersForRead">What the whole read's character budget still allows when this email is reached.</param>
/// <remarks>
/// <para>
/// The bounds are passed in rather than read by the adapter, because they are the use case's privacy control: one of
/// them is spent across the emails a single call names, so only the code sequencing that call knows what is left. An
/// adapter holding its own copy could not be told that an earlier email had already drawn on it.
/// </para>
/// <para>
/// The two are not interchangeable. The per-representation bound is about one message and applies to the plain text and
/// the markup separately, so a long body cannot decide what the other representation returns. The remaining budget is
/// about the call and is consumed by everything it returns, so it applies across both.
/// </para>
/// <para>
/// Nothing here bounds attachments, because a rendering returns none of their octets. What a message carries is
/// described from the same walk that produces the body, and the file itself is fetched through a link rather than
/// returned, so the only bound it is subject to is the size limit its raw MIME was stored under.
/// </para>
/// </remarks>
public sealed record EmailContentRenderingBounds(
    bool IncludeSanitizedHtml,
    int MaxCharactersPerRepresentation,
    int RemainingCharactersForRead)
{
    /// <summary>Gets whether to also reduce the body to the document tree a reading pane draws.</summary>
    /// <remarks>
    /// Opt-in like the markup, and for a sharper reason: the reduction is what a person reading a message needs and it
    /// is not what a model reading one needs, so a tool call pays for neither the walk nor the inlined pictures it
    /// resolves. It is an init property rather than a constructor parameter so the callers that ask for neither say
    /// nothing about it.
    /// </remarks>
    public bool IncludeMailDocument { get; init; }

    /// <summary>Gets whether to also produce the message's own markup with everything that runs or reports removed.</summary>
    /// <remarks>
    /// Opt-in like the other two, and the most expensive of the three: it carries the message's markup and inlines the
    /// message's own pictures beside it, so a caller that never draws a layout pays for neither.
    /// </remarks>
    public bool IncludeSelfContainedHtml { get; init; }

    /// <summary>Gets whether the reduced document may carry the message's remote picture references.</summary>
    /// <remarks>
    /// <para>
    /// False by every default, which is what defeats a tracking pixel by construction rather than by a renderer
    /// honouring a setting: the addresses are dropped while the document is built, so there is nothing for a rendering
    /// defect to fetch. True only where the reader asked for this message's remote content, having been told what that
    /// reveals to whoever wrote it.
    /// </para>
    /// <para>
    /// In the reduced document it widens exactly one thing — <c>http</c> and <c>https</c> on a picture's source, and
    /// nowhere else. A link's target is unaffected because it was never fetched, and no other reference exists in the
    /// tree to widen. The self-contained markup has more that a renderer resolves unasked — a stylesheet, a background,
    /// a web font, a candidate source — so there it widens every one of them, which is the same consent applied to
    /// everything that would have acted on it. It restores no executable construct in either.
    /// </para>
    /// </remarks>
    public bool RetainRemoteImageReferences { get; init; }

    /// <summary>Gets how many octets of its own pictures the whole read may still inline when this email is reached.</summary>
    /// <remarks>
    /// <para>
    /// The octet counterpart of <see cref="RemainingCharactersForRead" />, and it exists for the same reason: a read
    /// may name several emails, each of which would otherwise inline a whole document's worth of pictures. The words
    /// are bounded across the call and the pictures were not, so the response size was the sender's decision rather
    /// than the deployment's.
    /// </para>
    /// <para>
    /// It narrows what one document may carry rather than replacing it — the per-document bound still applies, so the
    /// first email of a call is answered exactly as a call naming it alone would be. <see cref="int.MaxValue" /> is the
    /// default because it means the caller stated nothing, which leaves the document's own bound the only one.
    /// </para>
    /// </remarks>
    public int RemainingInlineImageOctetsForRead { get; init; } = int.MaxValue;
}
