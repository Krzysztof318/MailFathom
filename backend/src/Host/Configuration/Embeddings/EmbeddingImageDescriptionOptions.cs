// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;

namespace MailFathom.Host.Configuration.Embeddings;

/// <summary>Declares whether this deployment sends image attachments to a chat provider to be described, and how large one may be.</summary>
/// <remarks>
/// <para>
/// Off, and off is what an operator who has not read this gets. Describing an image sends the attachment's octets to
/// the declared chat endpoint, which is a disclosure of mail content as real as sending message text for embedding and
/// frequently a larger one — a photograph of a document discloses the document — and it is the one egress in this
/// system that no content scan covers, because the sensitive-content guard detects regions in a string and there is no
/// such operation for a picture. So the operator's own decision is the whole of the control, and nothing here starts
/// because a release shipped or a chat endpoint was declared for something else.
/// </para>
/// <para>
/// Separate from whether the deployment embeds at all, and separate again from the chat endpoint's own declaration,
/// because the three are different costs: embedding is priced per character over text already stored, describing is a
/// chat call per picture, and neither implies the other. It sits under this section rather than beside the chat
/// endpoint because what it produces is a passage — a description is text derived from an attachment, and what happens
/// to attachment-derived text is decided here.
/// </para>
/// <para>
/// How large a picture may be *in octets* is not declared here. That ceiling is the chat endpoint's
/// <c>MaxRequestImageOctets</c>, because it bounds what one request carries rather than what this feature will look
/// at, and two numbers for one limit would be a pair an operator could set into disagreement.
/// </para>
/// </remarks>
internal sealed class EmbeddingImageDescriptionOptions
{
    /// <summary>The largest pixel grid this deployment sends where none is declared.</summary>
    /// <remarks>
    /// Forty megapixels is well past any camera a person attaches a photograph from and well short of what a
    /// decompression bomb declares, which is the gap the number exists to sit in. An image declaring more is refused on
    /// its header rather than decoded.
    /// </remarks>
    public const long DefaultMaxPixels = 40_000_000;

    /// <summary>Gets or sets whether an image attachment is sent to the chat provider to be described.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the largest pixel grid an image may declare and still be described.</summary>
    /// <remarks>
    /// The decompression-bomb bound, and the reason it is stated in pixels rather than in octets: a compressed image of
    /// a few kilobytes may declare a grid of billions, and what a decoder then allocates follows the grid rather than
    /// the file. Nothing in this deployment decodes an image, so what this protects is the provider that does — and a
    /// grid this deployment would not have decoded is not one to make somebody else decode either.
    /// </remarks>
    [Range(1, 1_000_000_000)]
    public long MaxPixels { get; set; } = DefaultMaxPixels;
}
