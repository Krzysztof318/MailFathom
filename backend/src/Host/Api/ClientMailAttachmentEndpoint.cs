// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.DownloadAttachment;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Streams one file a message carries to the signed-in reader who asked for it.</summary>
/// <remarks>
/// <para>
/// A file is fetched rather than carried. The message route describes every attachment — what it is called, what it
/// declares itself to be, and how large it is — so that opening a message costs the same whether the sender attached a
/// note or a video, and this is the route the reader follows once they decide one is worth having.
/// </para>
/// <para>
/// It is the client surface's own route rather than the signed link the tool surface mints, and the difference is who
/// is being served. A link exists to be handed to something that holds no credential, which is why it is a bearer
/// capability that expires within minutes; a reader here has already authenticated and holds the mailbox read grant, so
/// the credential they already presented is the access control and no second one is minted. That also keeps the client
/// working on a deployment serving no MCP endpoint, which serves no link route either.
/// </para>
/// <para>
/// The octets are streamed from the local copy rather than buffered: the response states the size the parse measured
/// before the first octet is written, and the same parse then writes exactly that many. Nothing about the request or
/// the response reaches a log — a file name and its octets are mail content.
/// </para>
/// <para>
/// It speaks to no mail server, so a download cannot fetch a message and cannot set the remote <c>\Seen</c> flag.
/// </para>
/// </remarks>
internal static class ClientMailAttachmentEndpoint
{
    /// <summary>The route serving one message's attachment, relative to the client prefix.</summary>
    internal const string MailAttachmentRoute = "/messages/{storedEmailId:guid}/attachments/{position:int}";

    /// <summary>Maps the route into the client group, so it inherits the group's requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The response is described as octets rather than left to the framework, which would record a <c>200</c> with no
    /// content at all for a handler that writes to the response body itself — a document telling a client this route
    /// answers with nothing. What the document names is the general binary type rather than any one message's, because
    /// the media type a response actually carries is whatever the sender wrote and no document can enumerate that.
    /// </remarks>
    internal static void MapClientMailAttachment(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(MailAttachmentRoute, DownloadAsync)
            .RequirePermission(MailFathomPermission.MailRead)
            .Produces<Stream>(StatusCodes.Status200OK, AttachmentContentResponse.FallbackMediaType);
    }

    /// <summary>Streams one attachment of one of the acting owner's messages, or reports that there is no such file.</summary>
    /// <param name="storedEmailId">The message the file belongs to, as a read of that message published it.</param>
    /// <param name="position">The file's place in the order that read listed the message's attachments in.</param>
    /// <param name="attachments">Opens the attachment, for a caller the read's own grant admits.</param>
    /// <param name="context">The request being answered, whose response body the file is written to.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns>The file's octets, <c>404</c> where this owner has no such file, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.read</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any resolved dependency is <see langword="null" />.</exception>
    /// <remarks>
    /// Every refusal is one refusal. A message this owner does not hold, one no deployment ever held, a local copy that
    /// is damaged or missing, and a position the message carries no part at all answer identically, because telling them
    /// apart would let a caller learn what became of mail they cannot read by asking about it.
    /// </remarks>
    internal static async Task<Results<EmptyHttpResult, NotFound>> DownloadAsync(
        [FromRoute] Guid storedEmailId,
        [FromRoute] int position,
        [FromServices] EmailAttachmentDownloadReader attachments,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attachments);
        ArgumentNullException.ThrowIfNull(context);

        if (storedEmailId == Guid.Empty || position < 0)
        {
            return TypedResults.NotFound();
        }

        await using var attachment = await attachments.OpenForReaderAsync(
            StoredEmailId.Create(storedEmailId),
            position,
            cancellationToken);

        if (attachment is null)
        {
            return TypedResults.NotFound();
        }

        AttachmentContentResponse.Describe(context.Response, attachment.Description);

        await attachment.WriteContentToAsync(context.Response.Body, cancellationToken);

        return TypedResults.Empty;
    }
}
