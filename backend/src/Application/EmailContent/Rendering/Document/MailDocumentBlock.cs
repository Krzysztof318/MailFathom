// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Application.EmailContent.Rendering.Document.Blocks;

namespace MailFathom.Application.EmailContent.Rendering.Document;

/// <summary>One typed part of a reduced mail body.</summary>
/// <remarks>
/// <para>
/// The hierarchy is closed by a private protected constructor, so the eight derived types declared beside it are the
/// whole of it: nothing outside this assembly can bring a block into being from data. What C# leaves reachable is the
/// copy constructor every non-sealed record has and which the language requires to be protected — a type derived through
/// it can only copy a block this assembly already composed, carries that block's catalogue type, and is refused by the
/// serializer as a derived type the contract never declared.
/// </para>
/// <para>
/// That closure is the isolation statement of the default rendering path. A client receives no markup and runs no
/// engine, so script cannot run because nothing that could run it is on this path, and message style cannot escape the
/// pane because no member anywhere below offsets a node, transforms it, floats it, gives it an absolute size, or orders
/// it in front of anything. A width survives only as a share of the parent it sits in, which cannot resolve to a
/// position outside that parent. Admitting a positional or absolute-dimensional property here is what would break that,
/// and this is where it would have to be argued.
/// </para>
/// <para>
/// Every block carries the version of its own type, taken from the catalogue rather than supplied by whatever composed
/// the document: a client meeting a version it does not know refuses that block and renders the rest of the message,
/// and a producer cannot claim a revision it did not write.
/// </para>
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(MailParagraphBlock), MailDocumentBlockType.ParagraphIdentity)]
[JsonDerivedType(typeof(MailHeadingBlock), MailDocumentBlockType.HeadingIdentity)]
[JsonDerivedType(typeof(MailListBlock), MailDocumentBlockType.ListIdentity)]
[JsonDerivedType(typeof(MailTableBlock), MailDocumentBlockType.TableIdentity)]
[JsonDerivedType(typeof(MailQuoteBlock), MailDocumentBlockType.QuoteIdentity)]
[JsonDerivedType(typeof(MailImageBlock), MailDocumentBlockType.ImageIdentity)]
[JsonDerivedType(typeof(MailSeparatorBlock), MailDocumentBlockType.SeparatorIdentity)]
[JsonDerivedType(typeof(MailPreformattedBlock), MailDocumentBlockType.PreformattedIdentity)]
public abstract record MailDocumentBlock
{
    private protected MailDocumentBlock(MailDocumentBlockType type) => this.Type = type;

    /// <summary>Gets which of the catalogued blocks this is.</summary>
    /// <remarks>
    /// Not written as a member of its own, because the type discriminator the polymorphic contract already writes is
    /// the same value: <see cref="MailDocumentBlockType.Identity" /> and the discriminator are one constant per member,
    /// so the two cannot disagree. This is how the value is read in code.
    /// </remarks>
    [JsonIgnore]
    public MailDocumentBlockType Type { get; }

    /// <summary>Gets the revision of this block's contract, which is the catalogue's rather than the producer's.</summary>
    /// <exception cref="ArgumentException">Thrown when a document claims a revision this build does not implement.</exception>
    /// <remarks>
    /// Refused rather than normalized, for the reason a presentation plan refuses one: silently reading a newer
    /// revision as this one would drop the members that revision added and present the block as though nothing were
    /// missing. Refusing the block costs the reader that block and nothing else, which is the choice this pane makes.
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
}
