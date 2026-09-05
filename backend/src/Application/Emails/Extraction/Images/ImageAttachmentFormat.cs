// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Extraction.Images;

/// <summary>Names an image format this deployment is willing to send to a provider for description.</summary>
/// <remarks>
/// <para>
/// The whole allow-list, and deliberately a short one. Every entry is a raster format whose header states its own pixel
/// grid in a fixed place, which is what lets a decompression bomb be refused from a few bytes rather than discovered by
/// allocating for it. A format that carries markup, script, or a reference to something fetched elsewhere is not on the
/// list and is not made to be — <c>image/svg+xml</c> is the one such format common enough in mail to be named as
/// excluded rather than left to fall through as unrecognized.
/// </para>
/// <para>
/// Membership is decided from the octets rather than from the part's declared media type, because the media type is
/// written by the sender and the octets are what a provider decodes.
/// </para>
/// </remarks>
public enum ImageAttachmentFormat
{
    /// <summary>Portable Network Graphics, whose <c>IHDR</c> chunk states the grid in the first twenty-four octets.</summary>
    Png = 0,

    /// <summary>JPEG, whose grid is stated by whichever start-of-frame marker the segment chain reaches first.</summary>
    Jpeg = 1,

    /// <summary>WebP, in any of its three chunk layouts: lossy, lossless, and extended.</summary>
    Webp = 2,

    /// <summary>GIF, whose logical screen descriptor states the grid every frame is composed onto.</summary>
    /// <remarks>An animated GIF is sent as it arrived and a vision model reads its first frame, which is the frame the description is of.</remarks>
    Gif = 3,
}
