// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.SyntheticMail.Generation;

/// <summary>One attachment a generated message carries, described rather than materialized.</summary>
/// <param name="FileName">The name the part is filed under.</param>
/// <param name="MediaType">The media type, for example <c>text</c>.</param>
/// <param name="MediaSubtype">The media subtype, for example <c>csv</c>.</param>
/// <param name="Length">How many bytes the content is, which the batch's ceiling bounds.</param>
/// <param name="ContentSeed">What the content is derived from, so the same corpus produces the same bytes.</param>
/// <remarks>
/// The bytes are a description here and are filled only while the message is being composed, immediately before it
/// goes out. A batch is generated in full before the first delivery, so holding every attachment's content would make
/// a run's peak memory the product of the count and the ceiling — a thousand messages carrying a megabyte each would
/// be a gigabyte of buffers to send a mailbox nobody is reading yet.
/// </remarks>
internal sealed record SyntheticEmailAttachment(
    string FileName,
    string MediaType,
    string MediaSubtype,
    int Length,
    int ContentSeed)
{
    /// <summary>Materializes the content the description stands for.</summary>
    /// <returns>Exactly <see cref="Length" /> bytes, the same ones on every run of the same corpus.</returns>
    /// <remarks>
    /// A text part is filled with printable ASCII so the extracted text of an attachment is something a search result
    /// can be read against; anything else is filled with arbitrary bytes, which is what an opaque attachment is.
    /// </remarks>
    [SuppressMessage(
        "Security",
        "CA5394:Do not use insecure randomness",
        Justification = "The bytes have to be the same on every run of the same corpus, which a cryptographic generator cannot do. They are the contents of an invented attachment and protect nothing.")]
    internal ReadOnlyMemory<byte> MaterializeContent()
    {
        var content = new byte[this.Length];
        var source = new Random(this.ContentSeed);

        if (string.Equals(this.MediaType, "text", StringComparison.Ordinal))
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
