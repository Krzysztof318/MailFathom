// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using MailFathom.Application.Emails.Extraction.Attachments;
using MailFathom.Application.Emails.Extraction.Images;

namespace MailFathom.Infrastructure.Documents;

/// <summary>Keeps the pictures of one document that are worth a model's reading, up to the configured count.</summary>
/// <remarks>
/// <para>
/// A picture is kept only when its own header names a format the describer sends, which is read from a few octets and
/// never by decoding: a PDF image stream stored under any filter but the JPEG one is not an image file until something
/// decodes it, and nothing here does. A picture is kept once however often it recurs, and a picture too small to
/// carry a page of text is not kept at all, so a letterhead logo repeated on every page spends none of the allowance.
/// </para>
/// <para>
/// Each kept picture is a copy, because the parser's own buffers do not outlive the document it opened.
/// </para>
/// </remarks>
internal sealed class EmbeddedPictureCollector(int maxPictures)
{
    /// <summary>The shorter side, in pixels, below which a picture is taken for a logo or an icon rather than a page.</summary>
    /// <remarks>
    /// ponytail: a fixed floor. A narrow screenshot of one line of text falls under it and is not read; make it a
    /// setting if such pictures turn out to matter.
    /// </remarks>
    internal const int SmallestPictureSide = 200;

    private readonly HashSet<string> fingerprints = new(StringComparer.Ordinal);
    private readonly List<EmbeddedAttachmentPicture> pictures = [];

    /// <summary>Gets whether another picture would still be kept, so a reader can stop looking.</summary>
    public bool WantsMore => this.pictures.Count < maxPictures;

    /// <summary>Gets the pictures kept so far, in the order they were offered.</summary>
    public IReadOnlyList<EmbeddedAttachmentPicture> Pictures => this.pictures;

    /// <summary>Keeps one picture if it is readable, large enough, new, and within the allowance.</summary>
    /// <param name="pageNumber">The one-based page, slide, or sheet the picture sits on.</param>
    /// <param name="octets">The picture as the document stores it.</param>
    public void Offer(int pageNumber, ReadOnlySpan<byte> octets)
    {
        if (!this.WantsMore
            || !ImageAttachmentHeader.TryRead(octets, out var header, out _)
            || Math.Min(header.Width, header.Height) < SmallestPictureSide
            || !this.fingerprints.Add(Convert.ToHexString(SHA256.HashData(octets))))
        {
            return;
        }

        this.pictures.Add(new EmbeddedAttachmentPicture(pageNumber, header.MediaType, octets.ToArray()));
    }
}
