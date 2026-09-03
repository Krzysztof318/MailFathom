// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Portraits;

/// <summary>The picture one person is drawn by, as the octets they supplied and the kind those octets are.</summary>
/// <remarks>
/// <para>
/// It is composed through <see cref="Of" /> and by nothing else, because what makes octets a portrait is that they
/// open as an image this deployment stores — the one invariant here, and the one a constructor taking both halves
/// would let a caller state rather than prove. The kind is therefore never carried beside the octets: it is read from
/// them, so a stored portrait and its media type cannot disagree.
/// </para>
/// <para>
/// Nothing here resizes, crops, re-encodes, or strips metadata from what was supplied. A portrait is served back as
/// the person uploaded it, which is what makes the bound and the kind check the whole of what stands between an
/// upload and the database.
/// </para>
/// </remarks>
public sealed class OwnerPortrait
{
    private OwnerPortrait(PortraitImageType type, ReadOnlyMemory<byte> content)
    {
        this.Type = type;
        this.Content = content;
    }

    /// <summary>Gets what kind of image the portrait is, which is the media type it is served under.</summary>
    public PortraitImageType Type { get; }

    /// <summary>Gets the octets the person supplied, unchanged.</summary>
    public ReadOnlyMemory<byte> Content { get; }

    /// <summary>Reads octets as a portrait, where they are one.</summary>
    /// <param name="content">The octets as they were supplied.</param>
    /// <returns>The portrait, or <see langword="null" /> where the octets are not an image kind this build stores.</returns>
    public static OwnerPortrait? Of(ReadOnlyMemory<byte> content) =>
        PortraitImageType.TryDetect(content.Span, out var type) ? new OwnerPortrait(type, content) : null;
}
