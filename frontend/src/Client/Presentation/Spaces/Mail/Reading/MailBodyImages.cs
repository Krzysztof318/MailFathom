// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.UI.Xaml.Media.Imaging;

namespace MailFathom.Client.Presentation.Spaces.Mail.Reading;

/// <summary>Turns the address a picture arrived under into something a pane can draw.</summary>
/// <remarks>
/// <para>
/// Two addresses reach here and no others. A <c>data:</c> URI is a part of the message itself, already bounded by the
/// deployment, and is decoded in this process rather than fetched. An absolute <c>http</c> or <c>https</c> address is
/// there only because the reader asked for this message's remote content, and it is the one case anything is fetched
/// from somebody else's server at all.
/// </para>
/// <para>
/// Anything else is refused rather than handed to the platform. A picture is drawn from an address a stranger wrote, so
/// what the pane must never do is let a scheme decide what happens — and a refusal costs the reader that picture rather
/// than the message.
/// </para>
/// <para>
/// Nothing here discards its synchronization context: every continuation ends at a <see cref="BitmapImage" />, which is
/// a visual-tree object and may only be touched on the thread that draws.
/// </para>
/// </remarks>
internal static class MailBodyImages
{
    /// <summary>The largest inline picture this pane decodes, matching what the deployment already bounds it to.</summary>
    /// <remarks>
    /// Stated again on this side rather than trusted from the answer. The bound protects the head that is drawing, and
    /// a head reading a deployment it does not control is exactly where a second statement of it is worth the line.
    /// </remarks>
    internal const int MaximumInlineOctets = 2 * 1024 * 1024;

    private const string DataUriPrefix = "data:";
    private const string Base64Marker = ";base64,";

    /// <summary>Resolves what a picture is drawn from, or nothing where it may not be drawn.</summary>
    /// <param name="source">The address the document carried.</param>
    /// <returns>The source to draw, or <see langword="null" /> where the pane draws the picture's description instead.</returns>
    internal static async Task<ImageSource?> ResolveAsync(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return null;
        }

        if (source.StartsWith(DataUriPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return await DecodeAsync(source);
        }

        return Uri.TryCreate(source, UriKind.Absolute, out var address)
            && (address.Scheme == Uri.UriSchemeHttp || address.Scheme == Uri.UriSchemeHttps)
                ? new BitmapImage(address)
                : null;
    }

    /// <summary>Decodes a picture the message carried, without anything leaving this process.</summary>
    private static async Task<ImageSource?> DecodeAsync(string source)
    {
        if (Decode(source) is not { } octets)
        {
            return null;
        }

        using var octetStream = new MemoryStream(octets, writable: false);
        using var stream = octetStream.AsRandomAccessStream();

        var picture = new BitmapImage();
        await picture.SetSourceAsync(stream);

        return picture;
    }

    /// <summary>Reads the octets out of a <c>data:</c> URI, or nothing where it is not one this pane draws.</summary>
    private static byte[]? Decode(string source)
    {
        var marker = source.IndexOf(Base64Marker, StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return null;
        }

        var encoded = source.AsSpan(marker + Base64Marker.Length);
        if (encoded.Length / 4 * 3 > MaximumInlineOctets)
        {
            return null;
        }

        var buffer = new byte[(encoded.Length / 4 * 3) + 3];

        return Convert.TryFromBase64Chars(encoded, buffer, out var written) && written > 0
            ? buffer[..written]
            : null;
    }
}
