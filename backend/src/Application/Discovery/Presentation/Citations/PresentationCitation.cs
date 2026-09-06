// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Discovery.Presentation.Citations;

/// <summary>One source a plan declares, under the name its blocks refer to it by.</summary>
/// <remarks>
/// <para>
/// A citation is declared once in the plan and referred to by <see cref="Id" /> wherever it is used, so a reader can see
/// that two facts rest on the same message and a client can draw one source list for a run.
/// </para>
/// <para>
/// The label is what a client prints where the source is named — a subject, a sender, a file name — and it exists so a
/// citation reads as something before it is followed. It is descriptive text and never the identity: two citations may
/// carry the same label and still resolve to different places.
/// </para>
/// <para>
/// Two of its members are about the source rather than about the reference to it, which is why they sit here and not on
/// the blocks that cite it. <see cref="Medium" /> says whether somebody wrote the source or this deployment described a
/// picture of it, which is the ranking
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0030-describing-an-image-attachment-in-words-and-ranking-a-depicted-match-below-a-written-one.md">ADR 0030</see>
/// fixes. <see cref="Unreadable" /> says that the source exists and yielded nothing, and why — a state a plan declares
/// so that an encrypted contract nobody could open is never presented as a mailbox that held nothing on the subject.
/// </para>
/// </remarks>
public sealed record PresentationCitation
{
    /// <summary>Initializes one source a plan declares.</summary>
    /// <param name="id">The name blocks refer to this citation by.</param>
    /// <param name="target">What the citation resolves to.</param>
    /// <param name="label">What a client prints where the source is named.</param>
    /// <param name="medium">Whether somebody wrote the source or it is a description of a picture.</param>
    /// <param name="unreadable">Why the source could not be read, or <see langword="null" /> where it was read.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="target" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="label" /> is the unspecified default.</exception>
    /// <remarks>
    /// The identifier is checked by <see cref="PresentationPlan" /> rather than here, because what makes a name usable
    /// is that the plan declares it once and no block names anything else — a claim about the set rather than about
    /// one citation, and one this constructor could not make. The same holds for what an unreadable source may back:
    /// a block resting on one would be a fact drawn from something nobody read, and refusing that is a claim about the
    /// plan.
    /// </remarks>
    public PresentationCitation(
        PresentationCitationId id,
        PresentationCitationTarget target,
        PresentationText label,
        PresentationSourceMedium medium,
        UnreadableSourceReason? unreadable = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        PresentationRequirement.Specified(label, nameof(label));

        this.Id = id;
        this.Target = target;
        this.Label = label;
        this.Medium = medium;
        this.Unreadable = unreadable;
    }

    /// <summary>Gets the name blocks refer to this citation by.</summary>
    public PresentationCitationId Id { get; }

    /// <summary>Gets what the citation resolves to.</summary>
    public PresentationCitationTarget Target { get; }

    /// <summary>Gets what a client prints where the source is named.</summary>
    public PresentationText Label { get; }

    /// <summary>Gets whether somebody wrote the source or it is this deployment's description of a picture.</summary>
    public PresentationSourceMedium Medium { get; }

    /// <summary>Gets why the source could not be read, or <see langword="null" /> where it was read.</summary>
    public UnreadableSourceReason? Unreadable { get; }
}
