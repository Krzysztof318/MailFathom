// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using MailFathom.Application.Portraits;
using MailFathom.Domain.Access;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace MailFathom.Host.Api;

/// <summary>Serves the signed-in person the picture they are drawn by, and takes it back replaced or removed.</summary>
/// <remarks>
/// <para>
/// Three routes over one picture. It is stored on the owner axis beside the preferences document rather than inside
/// one, because a megabyte of image octets is neither configuration nor a small closed document, and reading a switch
/// should not carry a photograph.
/// </para>
/// <para>
/// <b>No route here names an owner.</b> The person is the one the credential authenticated, resolved from the request
/// exactly as the record routes resolve it, so a request reaching somebody else's portrait cannot be composed: there
/// is no argument to put another owner's identifier in and no listing to discover one from.
/// </para>
/// <para>
/// All three are <see cref="MailFathomPermission.MailRead" />, and none adds a name to the published permission set.
/// The two writes are deliberately not <see cref="MailFathomPermission.MailAccountsWrite" />, for the reason the
/// preferences write is not: that grant decides which mailboxes this deployment connects to, and what a person is
/// drawn by must not be decided by a grant over their mail configuration.
/// </para>
/// <para>
/// <b>An upload is bounded and then judged by its octets.</b> The bound is applied by the routing pipeline before a
/// handler is entered, and the kind is read from the signature the format opens with rather than from the content type
/// the request declared — a declared type is a string an uploader wrote, so trusting it would let anything at all be
/// stored under an image's name and served back to a browser as one. Both refusals name what they refused against.
/// </para>
/// <para>
/// <b>An absent portrait is answered plainly rather than refused.</b> A client draws the initials it already has from
/// the person's name, so having no picture is an ordinary state of the screen; <c>204</c> says exactly that, and keeps
/// it apart from a <c>404</c>, which on this surface says a route or a record is not there.
/// </para>
/// <para>
/// The served response carries an entity tag over the octets and asks the client to revalidate, so a screen that draws
/// the portrait a second time is answered <c>304</c> rather than the picture again. It is not cached without
/// revalidation: a portrait is personal data, and a replaced one has to reach the next screen rather than the next
/// expiry.
/// </para>
/// <para>
/// It is served with <c>X-Content-Type-Options: nosniff</c>, for the reason
/// <see cref="AttachmentContentResponse" /> sets it on a message's files: the kind was proven from a signature rather
/// than by decoding the file, so a picture whose signature is a portrait's and whose remaining octets are markup would
/// otherwise be a page a browser might render on the address the operator publishes MailFathom at.
/// </para>
/// </remarks>
internal static class ClientPortraitEndpoint
{
    /// <summary>The route the acting person's portrait is read, replaced, and removed at, relative to the client prefix.</summary>
    internal const string PortraitRoute = "/portrait";

    /// <summary>The greatest upload the replacement route reads before refusing it.</summary>
    /// <remarks>One megabyte, which is what the client's own upload screen offers and far more than a portrait drawn at any size a screen has. What it guards is that a profile field is not a way to put arbitrary octets into an operator's database.</remarks>
    internal const int MaxPortraitBytes = 1024 * 1024;

    /// <summary>Maps the portrait routes into the client group, so they inherit its requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The read is described as octets rather than left to the framework, which would record a <c>200</c> carrying
    /// JSON for a route that answers with an image. What the document names is the two kinds this deployment stores,
    /// which here is the whole set rather than a fallback, because the kind was proven before the row existed.
    /// </remarks>
    internal static void MapClientPortrait(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(PortraitRoute, ReadAsync)
            .RequirePermission(MailFathomPermission.MailRead)
            .Produces<Stream>(StatusCodes.Status200OK, PortraitImageType.Jpeg.MediaType, PortraitImageType.Png.MediaType);

        // The attribute is reached for its metadata rather than as an MVC filter, exactly as every other write on this
        // surface reaches it: it implements IRequestSizeLimitMetadata, which the routing pipeline applies to the
        // request body feature, so a body over the bound is refused before the octets are buffered.
        api.MapPost(PortraitRoute, ReplaceAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxPortraitBytes))
            .Accepts<Stream>(PortraitImageType.Jpeg.MediaType, PortraitImageType.Png.MediaType)
            .RequirePermission(MailFathomPermission.MailRead);

        api.MapDelete(PortraitRoute, RemoveAsync)
            .RequirePermission(MailFathomPermission.MailRead);
    }

    /// <summary>Hands the acting person the picture they are drawn by.</summary>
    /// <param name="portraits">The acting person's own portrait.</param>
    /// <param name="context">The request being answered, whose response carries the caching the client revalidates against.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the octets under the kind they are, or <c>204</c> where this deployment holds no portrait for the caller.</returns>
    /// <remarks>Octets the store holds that are no kind this build publishes are answered as an absent portrait, because a row nothing here could have written is a row this surface has nothing to say about.</remarks>
    internal static async Task<Results<FileContentHttpResult, NoContent>> ReadAsync(
        [FromServices] OwnPortrait portraits,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(portraits);
        ArgumentNullException.ThrowIfNull(context);

        if (await portraits.ReadAsync(cancellationToken) is not { } portrait)
        {
            return TypedResults.NoContent();
        }

        // Private and revalidated rather than freely cacheable: a portrait is personal data, and the entity tag is what
        // turns the second screen drawing it into a 304 instead of the picture again.
        context.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue { Private = true, NoCache = true };

        // The kind was proven from a signature rather than by decoding the file, so what follows those few octets is
        // whatever the person uploaded: the browser is told to serve it as the type stated and never to sniff its way
        // to another one.
        context.Response.Headers.XContentTypeOptions = "nosniff";

        return TypedResults.File(
            portrait.Content.ToArray(),
            portrait.Type.MediaType,
            entityTag: EntityTagOf(portrait));
    }

    /// <summary>Replaces the picture the acting person is drawn by with the octets the request carries.</summary>
    /// <param name="portraits">The acting person's own portrait.</param>
    /// <param name="context">The request being answered, whose body carries the picture.</param>
    /// <param name="cancellationToken">Cancels the read and the commit.</param>
    /// <returns><c>204</c> when it was stored, <c>404</c> when this deployment holds no record for the caller, <c>413</c> for an upload over the bound, <c>415</c> for octets that are neither kind, or <c>400</c> for a request carrying no picture at all.</returns>
    internal static async Task<Results<NoContent, NotFound<ProblemDetails>, ProblemHttpResult>> ReplaceAsync(
        [FromServices] OwnPortrait portraits,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(portraits);
        ArgumentNullException.ThrowIfNull(context);

        using var buffer = new MemoryStream();

        try
        {
            await context.Request.Body.CopyToAsync(buffer, cancellationToken);
        }
        catch (BadHttpRequestException refusal)
            when (refusal.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            // The pipeline's own refusal carries no body, and the one thing a person needs from it is the bound they
            // went over, so it is restated here as a problem rather than left as a bare status.
            return Refusal(
                $"A portrait is at most {MaxPortraitBytes / 1024 / 1024} MB.",
                StatusCodes.Status413PayloadTooLarge);
        }

        if (buffer.Length == 0)
        {
            return Refusal(
                "An upload carries the picture. A request with no body replaces nothing.",
                StatusCodes.Status400BadRequest);
        }

        if (OwnerPortrait.Of(buffer.ToArray()) is not { } portrait)
        {
            return Refusal(
                $"A portrait is {string.Join(" or ", PortraitImageType.All.Select(kind => kind.MediaType))}, judged by what the file is rather than by what the request declared it to be.",
                StatusCodes.Status415UnsupportedMediaType);
        }

        return await portraits.ReplaceAsync(portrait, cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Detail = "This deployment holds no record for you.",
            });
    }

    /// <summary>Removes the picture the acting person is drawn by.</summary>
    /// <param name="portraits">The acting person's own portrait.</param>
    /// <param name="cancellationToken">Cancels the commit.</param>
    /// <returns><c>204</c>, whether or not there was a portrait to remove.</returns>
    /// <remarks>Everything else about the person is left as it was, and the answer does not separate having removed one from having had none: both leave the caller with no portrait, which is the whole of what they asked for.</remarks>
    internal static async Task<NoContent> RemoveAsync(
        [FromServices] OwnPortrait portraits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(portraits);

        await portraits.RemoveAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    /// <summary>Names the served octets, so a client that already holds them is answered that they have not changed.</summary>
    /// <remarks>A digest of the picture rather than the instant it was written: it is stable across a restore, and two writes of the same picture leave a client's copy valid.</remarks>
    private static EntityTagHeaderValue EntityTagOf(OwnerPortrait portrait) =>
        new($"\"{Convert.ToHexString(SHA256.HashData(portrait.Content.Span))}\"");

    private static ProblemHttpResult Refusal(string detail, int statusCode) =>
        TypedResults.Problem(detail, statusCode: statusCode);
}
