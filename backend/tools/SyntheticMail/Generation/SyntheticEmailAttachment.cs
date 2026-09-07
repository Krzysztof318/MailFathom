// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace MailFathom.SyntheticMail.Generation;

/// <summary>One attachment a generated message carries, described rather than materialized.</summary>
/// <param name="FileName">The name the part is filed under.</param>
/// <param name="MediaType">The media type, for example <c>text</c>.</param>
/// <param name="MediaSubtype">The media subtype, for example <c>csv</c>.</param>
/// <param name="Length">How many bytes the content is, which the batch's ceiling bounds.</param>
/// <param name="ContentSeed">What the content is derived from, so the same corpus produces the same bytes.</param>
/// <param name="Text">The file's contents, when they were written rather than drawn, and <see langword="null" /> when the bytes come from the seed.</param>
/// <remarks>
/// The bytes are a description here and are filled only while the message is being composed, immediately before it
/// goes out. A batch is generated in full before the first delivery, so holding every attachment's content would make
/// a run's peak memory the product of the count and the ceiling — a thousand messages carrying a megabyte each would
/// be a gigabyte of buffers to send a mailbox nobody is reading yet. A written file is the exception and is held,
/// because what a model answered cannot be derived from anything.
/// </remarks>
internal sealed record SyntheticEmailAttachment(
    string FileName,
    string MediaType,
    string MediaSubtype,
    int Length,
    int ContentSeed,
    string? Text = null)
{
    /// <summary>Whether the part is text, which is what decides both how it is filled and whether it can be written.</summary>
    internal bool IsText => string.Equals(this.MediaType, "text", StringComparison.Ordinal);

    /// <summary>Describes the same attachment carrying the contents somebody wrote for it.</summary>
    /// <param name="text">The file's contents.</param>
    /// <returns>The description, with its length now the length of those contents.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The length moves with the text rather than staying the size the seed drew, because the drawn size is what the
    /// writer was asked for and what it wrote is what the message carries — and a listing reporting one while the part
    /// holds the other is a corpus nobody can compare.
    /// </remarks>
    internal SyntheticEmailAttachment Carrying(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return this with { Text = text, Length = Encoding.UTF8.GetByteCount(text) };
    }

    /// <summary>Materializes the content the description stands for.</summary>
    /// <returns>Exactly <see cref="Length" /> bytes, the same ones on every run of the same corpus.</returns>
    /// <remarks>
    /// A written file is encoded as UTF-8, which is the charset the composed part declares. A drawn text part is
    /// filled with printable ASCII so the extracted text of an attachment is something a search result can be read
    /// against; anything else is filled with arbitrary bytes, which is what an opaque attachment is.
    /// </remarks>
    [SuppressMessage(
        "Security",
        "CA5394:Do not use insecure randomness",
        Justification = "The bytes have to be the same on every run of the same corpus, which a cryptographic generator cannot do. They are the contents of an invented attachment and protect nothing.")]
    internal ReadOnlyMemory<byte> MaterializeContent()
    {
        if (this.Text is { } written)
        {
            return Encoding.UTF8.GetBytes(written);
        }

        var content = new byte[this.Length];
        var source = new Random(this.ContentSeed);

        if (this.IsText)
        {
            for (var index = 0; index < content.Length; index++)
            {
                // A newline every so often, so the part reads as lines rather than as one enormous one.
                content[index] = index % 64 == 63 ? (byte)'\n' : (byte)source.Next('a', 'z' + 1);
            }

            return content;
        }

        source.NextBytes(content);

        return content;
    }
}
