// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.Emails.DownloadAttachment;
using MailFathom.Application.Emails.Extraction;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace MailFathom.Host.Api;

/// <summary>Serves one attachment to whoever presents a valid capability for it.</summary>
/// <remarks>
/// <para>
/// The one route in this process that answers without a credential, and deliberately so. A link exists to be handed to
/// whatever actually fetches files — a browser, a downloader, a client's own HTTP stack — and none of those can attach
/// the MCP endpoint's credential, so requiring one would make the capability unusable by the only callers it was minted
/// for. The signature is therefore the whole of the access control, and everything around it is sized as such: the
/// capability names one attachment of one email, it expires within minutes, its tag is verified in fixed time, and the
/// mailbox is read afresh on every redemption so a link cannot outlive the deletion of its own message.
/// </para>
/// <para>
/// It belongs to the MCP surface rather than to a surface of its own. That is what gives it that endpoint's transport,
/// its rate limiting, and its enablement without a third externally reachable listener existing: a deployment that
/// serves no MCP endpoint serves no download route either, which is the same answer an operator already expects.
/// </para>
/// <para>
/// Every refusal is one refusal. An expired capability, a forged one, one naming an email this deployment no longer
/// serves, one whose local copy is damaged, and one naming an attachment the message does not carry are all
/// <c>404</c> with the same body, because telling them apart would let whoever holds a capability learn what became of
/// mail they can no longer read. Nothing about a request or a response is logged here: the capability is an
/// unauthenticated way to obtain mail content, and the file name and octets are mail content themselves.
/// </para>
/// </remarks>
internal static class EmailAttachmentDownloadEndpoint
{
    /// <summary>The path prefix the route is served beneath, which is also what a minted link is built from.</summary>
    internal const string RoutePrefix = "/attachments";

    /// <summary>The one thing a refused request is told, whatever the reason was.</summary>
    internal const string RefusalDetail = "This attachment link is not valid.";

    /// <summary>What a download declares itself to be when the message's own media type is unusable.</summary>
    /// <remarks>The sender chose the media type, so it is parsed rather than trusted; a value that is not a media type at all is served as opaque bytes instead of being repaired into something plausible.</remarks>
    private const string FallbackMediaType = "application/octet-stream";

    /// <summary>Maps the download route.</summary>
    /// <param name="endpoints">The application's route builder.</param>
    /// <returns>The mapped route, so the caller can attach the surface's own metadata to it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoints" /> is <see langword="null" />.</exception>
    internal static RouteHandlerBuilder MapEmailAttachmentDownload(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints.MapGet($"{RoutePrefix}/{{capability}}", DownloadAsync);
    }

    /// <summary>Verifies the presented capability and streams the attachment it authorizes.</summary>
    /// <param name="capability">The capability the URL carried, which is entirely untrusted.</param>
    /// <param name="ticketReader">Verifies the capability against the deployment's key ring.</param>
    /// <param name="downloadReader">Opens the attachment the verified capability names.</param>
    /// <param name="context">The request being answered, whose response body the attachment is written to.</param>
    /// <param name="cancellationToken">Cancels the read when the reader disconnects.</param>
    /// <returns>The attachment's octets, or <c>404</c> with a body that says nothing about why.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any resolved dependency is <see langword="null" />.</exception>
    internal static async Task<Results<EmptyHttpResult, NotFound<ProblemDetails>>> DownloadAsync(
        string capability,
        IAttachmentDownloadTicketReader ticketReader,
        EmailAttachmentDownloadReader downloadReader,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticketReader);
        ArgumentNullException.ThrowIfNull(downloadReader);
        ArgumentNullException.ThrowIfNull(context);

        var ticket = await ticketReader.RedeemAsync(capability, cancellationToken);
        if (ticket is null)
        {
            return Refused();
        }

        await using var attachment = await downloadReader.OpenAsync(ticket, cancellationToken);
        if (attachment is null)
        {
            return Refused();
        }

        Describe(context.Response, attachment.Description);

        await attachment.WriteContentToAsync(context.Response.Body, cancellationToken);

        return TypedResults.Empty;
    }

    /// <summary>States what the response carries, in the encoding each header defines for it.</summary>
    /// <remarks>
    /// <para>
    /// Both values come from the message and are therefore attacker-controlled. The media type is parsed before it is
    /// echoed, so a header value the sender wrote cannot introduce a parameter or a second header, and the file name is
    /// written through the header type that applies RFC 5987 encoding rather than being concatenated into a header.
    /// </para>
    /// <para>
    /// The disposition is always <c>attachment</c> and the sniffing opt-out is always set, because this route serves
    /// sender-controlled bytes from the deployment's own origin: rendered inline, a message carrying HTML would be a
    /// scripted page on the address the operator publishes MailFathom at.
    /// </para>
    /// <para>
    /// The length is the size the same parse measured, so a reader knows what to expect and a truncated transfer is
    /// visible as one rather than as a shorter file.
    /// </para>
    /// <para>
    /// <c>no-store</c> is what keeps the window meaningful. This is an ordinary cacheable <c>GET</c> whose response is
    /// mail content, and the deployments this route is documented for put a reverse proxy in front of it: an
    /// intermediary applying a default freshness lifetime would keep serving the file for that URL after the capability
    /// expired, which would put the octets somewhere MailFathom does not control and take the expiry out of the
    /// revocation model it is the whole of.
    /// </para>
    /// </remarks>
    private static void Describe(HttpResponse response, ExtractedEmailAttachment description)
    {
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

    private static NotFound<ProblemDetails> Refused() =>
        TypedResults.NotFound(new ProblemDetails
        {
            Title = "Not found",
            Detail = RefusalDetail,
            Status = StatusCodes.Status404NotFound,
        });
}
