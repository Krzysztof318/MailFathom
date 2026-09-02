// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Application.Discovery.Presentation.Blocks;
using MailFathom.Application.Discovery.Presentation.Citations;

namespace MailFathom.Application.Discovery.Presentation;

/// <summary>One typed part of a presentation plan, carrying its own data and its own sources.</summary>
/// <remarks>
/// <para>
/// The hierarchy is closed by a private protected constructor, so the nine derived types declared beside it are the
/// whole of it: nothing outside this assembly can bring a block into being from data. What C# leaves reachable is the
/// copy constructor every non-sealed record has and which the language requires to be protected — a type derived
/// through it can only copy a block this assembly already composed, carries that block's catalogue type, and is refused
/// by the serializer as a derived type the contract never declared. That, and not a review, is what keeps a plan to
/// presentations the client has renderers for.
/// </para>
/// <para>
/// Every block carries the evidence behind it, so checking a fact means reading one block's sources rather than the
/// whole answer's. And every block carries the version of its own type, taken from the catalogue rather than supplied
/// by whatever composed the plan: a client meeting a version it does not know refuses that block and keeps the rest of
/// the run, and a producer cannot claim a revision it did not write.
/// </para>
/// <para>
/// Nothing here is markup, a template, an expression, or a reference to code. Text is <see cref="PresentationText" />,
/// which refuses to be markup; every other member is a number, a timestamp, an identity, or a value from a closed set.
/// A client draws these with ordinary typed UI and evaluates none of it.
/// </para>
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(AnswerBlock), PresentationBlockType.AnswerIdentity)]
[JsonDerivedType(typeof(EvidenceListBlock), PresentationBlockType.EvidenceListIdentity)]
[JsonDerivedType(typeof(TimelineBlock), PresentationBlockType.TimelineIdentity)]
[JsonDerivedType(typeof(FactTableBlock), PresentationBlockType.FactTableIdentity)]
[JsonDerivedType(typeof(PeopleBlock), PresentationBlockType.PeopleIdentity)]
[JsonDerivedType(typeof(ThreadStateBlock), PresentationBlockType.ThreadStateIdentity)]
[JsonDerivedType(typeof(AttachmentGalleryBlock), PresentationBlockType.AttachmentGalleryIdentity)]
[JsonDerivedType(typeof(DraftBlock), PresentationBlockType.DraftIdentity)]
[JsonDerivedType(typeof(SuggestedActionBlock), PresentationBlockType.SuggestedActionIdentity)]
public abstract record PresentationBlock
{
    private protected PresentationBlock(PresentationBlockType type, PresentationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        this.Type = type;
        this.Evidence = evidence;
    }

    /// <summary>Gets which of the catalogued types this block is.</summary>
    /// <remarks>
    /// Not written as a member of its own, because the type discriminator the polymorphic contract already writes is the
    /// same value: <see cref="PresentationBlockType.Identity" /> and the discriminator are one constant per member, so
    /// the two cannot disagree. Declared here rather than overridden per block, because a property named <c>type</c> on
    /// a derived type is the discriminator's own name and the serializer refuses the collision. This is how the value is
    /// read in code. It is never the unspecified default, because the hierarchy admits only the nine types declared
    /// beside it and each of them names its own catalogue member.
    /// </remarks>
    [JsonIgnore]
    public PresentationBlockType Type { get; }

    /// <summary>Gets the revision of this block type's contract, which is the catalogue's rather than the producer's.</summary>
    /// <remarks>
    /// Written from the catalogue, so nothing can stamp a revision it did not write, and checked on the way back in: a
    /// plan whose block claims a revision this build does not implement is refused rather than read as though it were
    /// this one. Silently normalizing it would be the worst of the three answers — the members that revision added
    /// would be dropped, and the block would present as though nothing were missing. Degrading such a block instead of
    /// refusing the plan is a reader's decision, and a reader that has to make it reads the number out of the JSON
    /// rather than binding to this build's catalogue.
    /// </remarks>
    public int Version
    {
        get => this.Type.Version;

        init
        {
            if (value != this.Type.Version)
            {
                throw new ArgumentException(
                    $"This build implements revision {this.Type.Version} of the '{this.Type.Identity}' block and cannot read revision {value}.",
                    nameof(value));
            }
        }
    }

    /// <summary>Gets what the correspondence does for what this block states.</summary>
    public PresentationEvidence Evidence { get; }

    /// <summary>Gets every citation this block names, including the ones its own entries name.</summary>
    /// <remarks>
    /// How the plan checks that every reference resolves to a citation it declares. A block whose entries cite
    /// individually overrides this to include them; a block that cites only as a whole does not, which is why the base
    /// answers rather than leaving nine implementations to remember the same thing. The value is read in code and is
    /// not part of the wire contract — the citations are already written where they are used.
    /// </remarks>
    [JsonIgnore]
    public virtual IEnumerable<PresentationCitationId> ReferencedCitations => this.Evidence.Citations;
}
