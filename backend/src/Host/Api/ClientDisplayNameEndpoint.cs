// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.OwnerSettings.Administration;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves the signed-in person the name this deployment records them under, and takes back the one they correct it to.</summary>
/// <remarks>
/// <para>
/// A client draws a person, and until these routes existed it held a finished credential and nothing the person is
/// called. The name is not in the record <see cref="ClientOwnerRecordEndpoint" /> serves: it is the envelope beside it,
/// the column an operator tells one owner from another by, so a client reading the record would still have nothing to
/// draw a menu with.
/// </para>
/// <para>
/// <b>Neither route names an owner</b>, exactly as no record route does. The person is the one the credential
/// authenticated, resolved from the request rather than read out of the body or the path.
/// </para>
/// <para>
/// The read is <see cref="MailFathomPermission.MailRead" /> and the write is
/// <see cref="MailFathomPermission.MailAccountsWrite" />, which is the record's own write and is granted separately.
/// Neither adds a name to the published permission set. The read reports whether a write would be accepted — the grant
/// and the source this deployment reads the person's mail accounts from, both asked without attempting one — so
/// somebody an administrator maintains the mailboxes of sees their name as text rather than meeting an unexplained
/// refusal on a field the client should never have offered them.
/// </para>
/// </remarks>
internal static class ClientDisplayNameEndpoint
{
    /// <summary>The route the acting person's own name is read at and written back to, relative to the client prefix.</summary>
    internal const string DisplayNameRoute = "/display-name";

    /// <summary>The greatest request body the write route reads before refusing it.</summary>
    /// <remarks>
    /// One string bounded at <see cref="MailOwnerRecord.MaximumDisplayNameLength" /> characters, with room for the
    /// widest UTF-8 encoding of each and the JSON escaping around them. Far below the record's bound, because a body
    /// sized for a page of mail-account declarations would be a bound nobody decided on; a body past it is answered
    /// <c>413</c> before the handler is reached, as every other write on this surface is.
    /// </remarks>
    internal const int MaxWriteRequestBytes = 1024;

    /// <summary>Maps the name routes into the client group, so they inherit its requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientDisplayName(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(DisplayNameRoute, ReadAsync)
            .RequirePermission(MailFathomPermission.MailRead);

        // The attribute is reached for its metadata rather than as an MVC filter, for the reason the record routes
        // state: it implements IRequestSizeLimitMetadata, which the routing pipeline applies to the request body.
        api.MapPost(DisplayNameRoute, ChangeAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.MailAccountsWrite);
    }

    /// <summary>Hands the acting person the name this deployment records them under.</summary>
    /// <param name="names">The acting person's own name.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the name and whether it may be changed, or <c>404</c> when this deployment holds no record for the caller.</returns>
    internal static async Task<Results<Ok<ClientDisplayNameResponse>, NotFound<ProblemDetails>>> ReadAsync(
        [FromServices] OwnDisplayName names,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(names);

        return await names.ReadAsync(cancellationToken) is { } held
            ? TypedResults.Ok(new ClientDisplayNameResponse(held.DisplayName, held.Changeable))
            : NoRecord();
    }

    /// <summary>Records the acting person under the name they corrected theirs to.</summary>
    /// <param name="names">The acting person's own name.</param>
    /// <param name="request">The name they would be recorded under.</param>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns><c>200</c> with the name now recorded, <c>404</c> when this deployment holds no record for the caller, or <c>400</c> naming what to correct.</returns>
    /// <remarks>The answer carries the name as it was stored rather than as it was sent, because a name is trimmed on its way in and a client that redrew what it typed would show something the deployment does not hold.</remarks>
    internal static async Task<Results<Ok<ClientDisplayNameResponse>, NotFound<ProblemDetails>, ProblemHttpResult>> ChangeAsync(
        [FromServices] OwnDisplayName names,
        [FromBody] ClientDisplayNameRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(request);

        var outcome = await names.ChangeAsync(request.DisplayName, cancellationToken);

        if (!outcome.OwnerHeld)
        {
            return NoRecord();
        }

        // A caller that just wrote the name holds what it takes to write it, so the answer says so rather than asking
        // the deployment again for a fact this request has already proven.
        return outcome.Recorded is { } recorded
            ? TypedResults.Ok(new ClientDisplayNameResponse(recorded, Changeable: true))
            : TypedResults.Problem(outcome.RefusalMessage, statusCode: StatusCodes.Status400BadRequest);
    }

    /// <summary>Answers that this deployment holds no record for the caller.</summary>
    /// <remarks>Reached where the row behind an authenticated caller has gone, which is an owner erased under a credential that has not yet been withdrawn.</remarks>
    private static NotFound<ProblemDetails> NoRecord() => TypedResults.NotFound(new ProblemDetails
    {
        Status = StatusCodes.Status404NotFound,
        Detail = "This deployment holds no record for you.",
    });
}

/// <summary>The name a person states they should be recorded under.</summary>
/// <param name="DisplayName">The name, which is bound exactly as the envelope binds it.</param>
/// <remarks>
/// Bound strictly: a key nothing here binds fails the bind rather than being ignored, which is what stops a client
/// sending a field this surface never published and reading the unchanged answer as the change having landed.
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ClientDisplayNameRequest(string? DisplayName = null);

/// <summary>What the client endpoint reports about the name one person is recorded under.</summary>
/// <param name="DisplayName">The name this deployment records them under.</param>
/// <param name="Changeable">Whether a write of it from this caller would be accepted.</param>
/// <remarks>
/// The second is answered rather than left for the client to discover, because both things that would refuse a write —
/// a grant the credential does not carry, and mail accounts a configuration source still declares — are facts about the
/// deployment that a client cannot see from here. What it does not report is which of the two: a client draws the same
/// screen either way, and naming the grant a credential lacks would say more about the deployment's own entries than a
/// page holding a token has any business reading back.
/// </remarks>
internal sealed record ClientDisplayNameResponse(string DisplayName, bool Changeable);
