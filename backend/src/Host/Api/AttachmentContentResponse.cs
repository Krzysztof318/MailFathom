// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction;
using Microsoft.Net.Http.Headers;

namespace MailFathom.Host.Api;

/// <summary>States what a response carrying one attachment's octets is, in the encoding each header defines for it.</summary>
/// <remarks>
/// <para>
/// Two routes serve the same octets to two different callers — a signed link redeemed with no credential, and a
/// signed-in reader opening a file in their own mailbox — and what has to be true of the response is the same for both.
/// It is written once here rather than twice, because every line of it is a defence and a second copy is where one of
/// them would quietly be left out.
/// </para>
/// <para>
/// Both values come from the message and are therefore attacker-controlled. The media type is parsed before it is
/// echoed, so a header value the sender wrote cannot introduce a parameter or a second header, and the file name is
/// written through the header type that applies RFC 5987 encoding rather than being concatenated into a header.
/// </para>
/// <para>
/// The disposition is always <c>attachment</c> and the sniffing opt-out is always set, because these routes serve
/// sender-controlled bytes from the deployment's own origin: rendered inline, a message carrying HTML would be a
/// scripted page on the address the operator publishes MailFathom at.
/// </para>
/// <para>
/// The length is the size the parse measured, so a reader knows what to expect and a truncated transfer is visible as
/// one rather than as a shorter file. It is the bound the response is written under as well: the octets are streamed
/// from the stored copy rather than buffered, and what the reader is told to expect is what the same parse will write.
/// </para>
/// <para>
/// <c>no-store</c> is what keeps a window meaningful. These are ordinary cacheable <c>GET</c>s whose responses are mail
/// content, and the deployments they are documented for put a reverse proxy in front of them: an intermediary applying
/// a default freshness lifetime would keep serving the file for that URL to whoever asked next, which would put the
/// octets somewhere MailFathom does not control — after a capability expired on one route, and after a session ended on
/// the other.
/// </para>
/// </remarks>
internal static class AttachmentContentResponse
{
    /// <summary>What a download declares itself to be when the message's own media type is unusable.</summary>
    /// <remarks>The sender chose the media type, so it is parsed rather than trusted; a value that is not a media type at all is served as opaque bytes instead of being repaired into something plausible.</remarks>
    internal const string FallbackMediaType = "application/octet-stream";

    /// <summary>Writes the headers that describe one attachment, before any octet of it is written.</summary>
    /// <param name="response">The response the attachment is written to.</param>
    /// <param name="description">What the parse measured about the part being served.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static void Describe(HttpResponse response, ExtractedEmailAttachment description)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(description);

        response.ContentType = MediaTypeHeaderValue.TryParse(description.MediaType, out var mediaType)
            ? mediaType.ToString()
            : FallbackMediaType;
        response.ContentLength = description.DecodedSizeOctets;

        var disposition = new ContentDispositionHeaderValue("attachment");
        if (description.FileName is { } fileName)
        {
            disposition.SetHttpFileName(fileName.Value);
        }

        response.Headers.ContentDisposition = disposition.ToString();
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers.CacheControl = "no-store";
    }
}
