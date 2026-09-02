// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Portraits;

/// <summary>What kind of image a portrait is: one of the two this deployment stores, judged from the octets themselves.</summary>
/// <remarks>
/// <para>
/// A closed enumeration rather than a C# <see langword="enum" /> because what a member carries is the media type a
/// response is served under, and an ordinal would mean nothing to the client reading the header. The set is two
/// because two is what a portrait may be: a format nothing here publishes is refused at the boundary rather than
/// stored for a screen that could not draw it.
/// </para>
/// <para>
/// <b>The kind is read from the octets and never from what the request claimed.</b> A declared content type is a
/// string an uploader wrote, so trusting it would let anything at all be stored under an image's name and served back
/// to a browser as one. What is matched is the signature each format opens with, which is the first thing a decoder
/// reads too.
/// </para>
/// <para>
/// Being a struct, <see langword="default" /> is reachable and is not a kind. It reports itself through
/// <see cref="IsSpecified" /> and refuses to answer for a media type, so nothing undeclared reaches a response header.
/// </para>
/// </remarks>
public readonly record struct PortraitImageType
{
    private readonly string? mediaType;

    private PortraitImageType(string mediaType) => this.mediaType = mediaType;

    /// <summary>Gets the JPEG portrait kind.</summary>
    public static PortraitImageType Jpeg { get; } = new("image/jpeg");

    /// <summary>Gets the PNG portrait kind.</summary>
    public static PortraitImageType Png { get; } = new("image/png");

    /// <summary>Gets every kind this build stores.</summary>
    /// <remarks>Declared last so the members it lists are already initialized when this initializer runs.</remarks>
    public static IReadOnlyList<PortraitImageType> All { get; } = [Jpeg, Png];

    /// <summary>Gets whether this value names a published kind rather than the unusable struct default.</summary>
    public bool IsSpecified => this.mediaType is not null;

    /// <summary>Gets the media type a portrait of this kind is served under.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a kind.</exception>
    public string MediaType => this.mediaType
        ?? throw new InvalidOperationException("The value is the default of the struct and does not name a portrait kind.");

    /// <summary>Reads what kind of image the octets are, from the signature the format opens with.</summary>
    /// <param name="content">The octets as they were supplied.</param>
    /// <param name="type">The kind the octets are, or the struct default when they are neither.</param>
    /// <returns><see langword="true" /> when the octets open as a kind this build stores.</returns>
    /// <remarks>
    /// The signature is the whole of the check, deliberately: nothing here decodes an image, so a file that opens as a
    /// JPEG and is damaged after its first octets is stored and served as the person supplied it. What the check is
    /// for is that a portrait is an image at all rather than a script or an archive wearing an image's content type.
    /// </remarks>
    public static bool TryDetect(ReadOnlySpan<byte> content, out PortraitImageType type)
    {
        type = content.StartsWith(JpegSignature) ? Jpeg
            : content.StartsWith(PngSignature) ? Png
            : default;

        return type.IsSpecified;
    }

    /// <inheritdoc />
    public override string ToString() => this.mediaType ?? "(unspecified)";

    /// <summary>Gets the three octets every JPEG opens with: the start-of-image marker and the first marker after it.</summary>
    private static ReadOnlySpan<byte> JpegSignature => [0xFF, 0xD8, 0xFF];

    /// <summary>Gets the eight octets the PNG specification fixes as a file's first line.</summary>
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
}
