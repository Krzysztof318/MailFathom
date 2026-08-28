// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Application.Emails.GetEmailContent;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves one message's body as the two renderings a reading pane draws it from.</summary>
/// <remarks>
/// <para>
/// The route serves a body and nothing else. A conversation is a route of its own and the header block, the sender
/// verdict, and the attachment list belong to the screen that composes them, so what is here is what a renderer needs:
/// the message reduced to a closed document tree, and the plain text that is a first-class rendering in its own right
/// rather than a fallback that looks broken.
/// </para>
/// <para>
/// The document carries no reference anything resolves unasked. A tracking pixel is defeated because its address is
/// removed while the tree is built rather than because a renderer honours a setting, and what a reader is told instead
/// is how many references were removed. Asking for them is a second request, per message, and nothing on either side
/// remembers that it was made: the query is the whole of the state, so opening the message again asks again.
/// </para>
/// <para>
/// It speaks to no mail server, so a request from a browser cannot wait on IMAP and cannot set the remote <c>\Seen</c>
/// flag. The body is read from the local copy exactly as the tool surface reads it, through the same use case and under
/// the same grant, so nothing about this transport widens what a caller may see.
/// </para>
/// </remarks>
internal static class ClientMailBodyEndpoint
{
    /// <summary>The route reporting one message's body, relative to the client prefix.</summary>
    internal const string MailBodyRoute = "/messages/{storedEmailId:guid}/body";

    /// <summary>Maps the route into the client group, so it inherits the group's requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientMailBody(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(MailBodyRoute, ReadBodyAsync)
            .RequirePermission(MailFathomPermission.MailRead);
    }

    /// <summary>Serves one of the acting owner's messages as a body, or reports that there is no such message.</summary>
    /// <param name="storedEmailId">The message to read, as a list row or a conversation published it.</param>
    /// <param name="remoteImages">Whether the reader asked for this message's remote pictures, having been told what that reveals.</param>
    /// <param name="content">Reads the message from the local copy, for a caller the read's own grant admits.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the body, <c>404</c> where this owner has no such message, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.read</c>.</returns>
    /// <remarks>
    /// A message this owner does not hold and one no deployment ever held answer identically, so nothing here tells a
    /// caller that somebody else's mail exists. A local copy that is damaged or missing answers the same way as well,
    /// having recorded the repair request the use case records for it: the reader is told there is nothing to draw
    /// rather than told which of this deployment's own defects they met.
    /// </remarks>
    internal static async Task<Results<Ok<ClientMailBodyResponse>, NotFound>> ReadBodyAsync(
        [FromRoute] Guid storedEmailId,
        [FromQuery] bool? remoteImages,
        [FromServices] EmailContentReader content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (storedEmailId == Guid.Empty)
        {
            return TypedResults.NotFound();
        }

        var request = RequestFor(storedEmailId, remoteImages);

        var result = await content.ReadContentAsync(request, cancellationToken);

        return result.Emails[0].Content is { } message
            ? TypedResults.Ok(ClientMailBodyResponse.For(message, request.RetainRemoteImageReferences))
            : TypedResults.NotFound();
    }

    /// <summary>Composes the read this route makes, which is the whole of what the query decides.</summary>
    /// <param name="storedEmailId">The message to read.</param>
    /// <param name="remoteImages">The query as it arrived, absent where the reader said nothing.</param>
    /// <returns>The request the use case is asked with.</returns>
    /// <remarks>
    /// Named rather than inlined because what it decides is a privacy boundary: the pane never asks for markup, it
    /// always asks for the tree, and an absent query means the same as a refusal rather than something to interpret.
    /// A seam here is what lets that be asserted without standing up the read behind it.
    /// </remarks>
    internal static GetEmailContentRequest RequestFor(Guid storedEmailId, bool? remoteImages) =>
        GetEmailContentRequest.Create([StoredEmailId.Create(storedEmailId)]) with
        {
            IncludeMailDocument = true,
            RetainRemoteImageReferences = remoteImages is true,
        };
}

/// <summary>One message's body as the client endpoint serves it.</summary>
/// <param name="StoredEmailId">The message, as the request named it.</param>
/// <param name="Availability">Whether the body could be read at all, or why it could not, as the state's own name.</param>
/// <param name="PlainText">The message as words, which is a rendering in its own right and what a refused document is read as.</param>
/// <param name="Document">The message reduced to the document tree the pane draws, which is present whenever the body could be read.</param>
/// <param name="RemoteImagesRequested">Whether this read was the one the reader asked remote pictures for.</param>
/// <remarks>
/// <para>
/// Both renderings travel together because a pane needs both: the document says whether it was refused and the plain
/// text is what it falls back to, and a client that had to ask twice would draw an empty pane in between.
/// </para>
/// <para>
/// The document is the published contract rather than a projection of one — it carries its own schema version and each
/// of its blocks carries the version of its own type — so it is serialized as it stands and a client keys its renderers
/// by the identities it publishes.
/// </para>
/// <para>
/// All of it is mail, so none of it reaches a log, a span attribute, or a telemetry event, here or anywhere it is
/// carried afterwards.
/// </para>
/// </remarks>
internal sealed record ClientMailBodyResponse(
    Guid StoredEmailId,
    string Availability,
    ClientMailBodyTextResponse PlainText,
    MailDocument? Document,
    bool RemoteImagesRequested)
{
    /// <summary>Describes one message's body for the wire.</summary>
    /// <param name="message">The message the use case read.</param>
    /// <param name="remoteImagesRequested">Whether this read was the one the reader asked remote pictures for.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="message" /> is <see langword="null" />.</exception>
    internal static ClientMailBodyResponse For(ReadEmailContent message, bool remoteImagesRequested)
    {
        ArgumentNullException.ThrowIfNull(message);

        return new ClientMailBodyResponse(
            message.StoredEmailId.Value,
            message.Body.Availability.ToString(),
            ClientMailBodyTextResponse.For(message.Body.PlainText),
            message.Body.Document,
            remoteImagesRequested);
    }
}

/// <summary>One textual rendering of a body, and what was left out of it.</summary>
/// <param name="Text">The text as it is returned, already bounded.</param>
/// <param name="OriginalCharacterCount">How many characters the message held before any bound was applied.</param>
/// <param name="Truncation">Which bound removed something, as the bound's own name, or that none did.</param>
internal sealed record ClientMailBodyTextResponse(string Text, int OriginalCharacterCount, string Truncation)
{
    /// <summary>Describes one representation for the wire.</summary>
    /// <param name="representation">The representation the use case produced.</param>
    /// <returns>The response body.</returns>
    internal static ClientMailBodyTextResponse For(EmailBodyRepresentation representation) => new(
        representation.Text,
        representation.OriginalCharacterCount,
        representation.Truncation.ToString());
}
