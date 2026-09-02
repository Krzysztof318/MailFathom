// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Mail.Mime.Rendering;

/// <summary>Judges a <c>data:</c> URI a message wrote against what a pane may be handed.</summary>
/// <remarks>
/// <para>
/// A <c>data:</c> URI is the one scheme the document carries besides a link's own, and it is admitted for a picture and
/// for nothing else. That narrowness is the whole of its safety: <c>data:text/html</c> is a document, and a pane handed
/// one would be handed a document a stranger wrote by a path that was supposed to carry a picture.
/// </para>
/// <para>
/// The media type is checked against the same list a resolved message part is checked against, so a picture is drawn on
/// the same terms whether the message carried it as a part or wrote it into the body. Anything else — a media type off
/// the list, a URI longer than the bound, or a form this does not read — is not drawn.
/// </para>
/// </remarks>
internal static class MailDataUri
{
    private static readonly string[] DrawablePrefixes =
    [
        "data:image/png;base64,",
        "data:image/jpeg;base64,",
        "data:image/jpg;base64,",
        "data:image/gif;base64,",
        "data:image/webp;base64,",
        "data:image/bmp;base64,",
    ];

    /// <summary>Judges one URI.</summary>
    /// <param name="reference">The URI as the message wrote it.</param>
    /// <param name="maximumOctets">How many octets the picture behind it may hold.</param>
    /// <returns>The URI where it is a picture this pane draws; otherwise <see langword="null" />.</returns>
    internal static string? Drawable(string reference, int maximumOctets)
    {
        var prefix = DrawablePrefixes.FirstOrDefault(candidate =>
            reference.StartsWith(candidate, StringComparison.OrdinalIgnoreCase));

        if (prefix is null)
        {
            return null;
        }

        // Base64 carries three octets in four characters, so the bound on the message's picture is applied to the
        // encoding it arrived in rather than to a decode nobody needs to run.
        var encoded = reference.Length - prefix.Length;

        return encoded > 0 && (long)encoded * 3 / 4 <= maximumOctets ? reference : null;
    }
}
