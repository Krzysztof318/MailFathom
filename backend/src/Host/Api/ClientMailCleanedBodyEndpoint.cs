// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Cleaning;
using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves one message's body as the third rendering: the reduced document with the sender's wrapping dropped.</summary>
/// <remarks>
/// <para>
/// A route of its own rather than a query on the body route, because what it costs is different in kind. The body route
/// reads a local copy and is published under reading mail; this one sends an outline to a chat provider and is charged to
/// the period allowance a question is charged to, so it is published under asking instead — a reading grant must not be a
/// way to spend a deployment's provider budget. A caller holds both, the read behind this requiring the reading grant as
/// every read does.
/// </para>
/// <para>
/// <b>It answers a document and never an empty pane.</b> Where the cleaning did not happen the ordinary reduced document
/// comes back with the reason beside it, so a client draws the message it would have drawn and says what did not happen —
/// a view that silently became a different view reads as a setting that stopped working.
/// </para>
/// <para>
/// It is asked per open and nothing on either side remembers that it was asked: nothing is derived at arrival, nothing is
/// stored, and opening the message again asks again. That is the same state the reader's ask for remote pictures is, and it
/// is why the reader's answer to that travels in the query here as well — a cleaned document composed out of a different
/// read would disagree with what is on the screen about what the message asked to fetch.
/// </para>
/// <para>
/// It speaks to no mail server, so a request from a browser cannot wait on IMAP and cannot set the remote <c>\Seen</c>
/// flag. The body is read from the local copy through the same use case the body route reads it through.
/// </para>
/// </remarks>
internal static class ClientMailCleanedBodyEndpoint
{
    /// <summary>The route reporting one message's cleaned body, relative to the client prefix.</summary>
    internal const string CleanedMailBodyRoute = "/messages/{storedEmailId:guid}/body/cleaned";

    /// <summary>Maps the route into the client group, so it inherits the group's requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientMailCleanedBody(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(CleanedMailBodyRoute, ReadCleanedBodyAsync)
            .RequirePermission(MailFathomPermission.MailAsk);
    }

    /// <summary>Serves one of the acting user's messages as the cleaned rendering, or reports that there is no such message.</summary>
    /// <param name="storedEmailId">The message to draw, as a list row or a conversation published it.</param>
    /// <param name="remoteImages">Whether the reader has asked this message's remote pictures for, so the cleaned document matches the body already on their screen.</param>
    /// <param name="cleaning">Cleans the body, for a caller the read's own grant admits.</param>
    /// <param name="cancellationToken">Cancels the pass when the client disconnects.</param>
    /// <returns><c>200</c> with the rendering, <c>404</c> where this user has no such message, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.ask</c>.</returns>
    /// <remarks>
    /// A spent allowance is <c>200</c> rather than <c>429</c>, which is the difference from drafting a reply: there the
    /// person pressed a button and has nothing unless the call is made, while here the message is on the screen either way
    /// and what the answer carries is the reduced document they were already reading. Telling them the cleaning did not
    /// happen is the honest answer, and failing the read would take the message away to report a cost.
    /// </remarks>
    internal static async Task<Results<Ok<ClientMailCleanedBodyResponse>, NotFound>> ReadCleanedBodyAsync(
        [FromRoute] Guid storedEmailId,
        [FromQuery] bool? remoteImages,
        [FromServices] MailBodyCleaning cleaning,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cleaning);

        if (storedEmailId == Guid.Empty)
        {
            return TypedResults.NotFound();
        }

        var cleaned = await cleaning.CleanAsync(
            StoredEmailId.Create(storedEmailId),
            remoteImages is true,
            cancellationToken);

        return cleaned is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(ClientMailCleanedBodyResponse.For(storedEmailId, cleaned));
    }
}

/// <summary>One message's cleaned body as the client endpoint serves it.</summary>
/// <param name="StoredEmailId">The message, as the request named it.</param>
/// <param name="Cleaning">What came of the cleaning, as the outcome's own name, which a client draws where it is not <c>Cleaned</c>.</param>
/// <param name="Document">
/// The document to draw: the cleaned one where the cleaning happened, and the ordinary reduced one otherwise. It is
/// <see langword="null" /> only for a body that carried no document at all, which is what the body route says about that
/// message as well.
/// </param>
/// <remarks>
/// <para>
/// The document is the published contract rather than a projection of one — it carries its own schema version and each of
/// its blocks carries the version of its own type — so it is serialized as it stands and a client draws it with the
/// renderers it already has for the reduced view. That is the point: the third rendering is the same blocks, fewer of them.
/// </para>
/// <para>
/// The plain text is deliberately absent. A client asking for this has already read the body, so repeating the words would
/// send the message twice for a rendering that is drawn from blocks — and the fallback a reader is shown when the cleaning
/// did not happen is the reduced document in this very answer.
/// </para>
/// <para>
/// All of it is mail, so none of it reaches a log, a span attribute, or a telemetry event, here or anywhere it is carried
/// afterwards.
/// </para>
/// </remarks>
internal sealed record ClientMailCleanedBodyResponse(Guid StoredEmailId, string Cleaning, MailDocument? Document)
{
    /// <summary>Describes one cleaned body for the wire.</summary>
    /// <param name="storedEmailId">The message, as the request named it.</param>
    /// <param name="cleaned">What the pass produced.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="cleaned" /> is <see langword="null" />.</exception>
    internal static ClientMailCleanedBodyResponse For(Guid storedEmailId, CleanedMailBody cleaned)
    {
        ArgumentNullException.ThrowIfNull(cleaned);

        return new ClientMailCleanedBodyResponse(storedEmailId, cleaned.Outcome.ToString(), cleaned.Document);
    }
}
