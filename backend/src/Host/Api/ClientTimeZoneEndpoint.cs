// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Domain.Scheduling;
using MailFathom.Host.Configuration.UserSettings.Administration;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves the signed-in person the zone their own days are read in, and takes back the one they correct it to.</summary>
/// <remarks>
/// <para>
/// The zone decides two things a client cannot settle for itself. On the deployment's side it is the anchor every
/// operation resolving a relative period states — a search sentence read into a period, an event drafted from a typed
/// description, a question asked about a stretch of mail — and it is read here rather than sent with each request, so
/// what reaches a provider's prompt is a value this deployment holds. On the client's side it is what a date is drawn
/// in, so a person whose browser reports another zone still reads their mail in their own days.
/// </para>
/// <para>
/// <b>Neither route names a user.</b> The person is the one the credential authenticated, resolved from the request
/// rather than read out of the body or the path, exactly as no record route names one.
/// </para>
/// <para>
/// Both routes are <see cref="MailFathomPermission.MailRead" />, which is the grant a signed-in person already holds.
/// The write is under the read's grant for the reason the portrait routes are: what a person's own days are read in is
/// not a decision about which mailboxes this deployment connects to and under whose credentials, so gating it on the
/// record's own write would refuse somebody whose mailboxes an administrator maintains a change about nothing but
/// themselves.
/// </para>
/// </remarks>
internal static class ClientTimeZoneEndpoint
{
    /// <summary>The route the acting person's own zone is read at and written back to, relative to the client prefix.</summary>
    internal const string TimeZoneRoute = "/time-zone";

    /// <summary>The greatest request body the write route reads before refusing it.</summary>
    /// <remarks>One identifier bounded at <see cref="ZonedInstant.MaximumZoneIdLength" /> characters, with room for the widest UTF-8 encoding of each and the JSON around them. A body past it is answered <c>413</c> before the handler is reached, as every other write on this surface is.</remarks>
    internal const int MaxWriteRequestBytes = 512;

    /// <summary>Maps the zone routes into the client group, so they inherit its requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientTimeZone(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(TimeZoneRoute, Read)
            .RequirePermission(MailFathomPermission.MailRead);

        // The attribute is reached for its metadata rather than as an MVC filter, for the reason the record routes
        // state: it implements IRequestSizeLimitMetadata, which the routing pipeline applies to the request body.
        api.MapPost(TimeZoneRoute, ChangeAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.MailRead);
    }

    /// <summary>Hands the acting person the zone their own days are read in.</summary>
    /// <param name="zones">Answers each person's zone out of the roster their records were published into.</param>
    /// <param name="authorization">Names the person the request is being served for.</param>
    /// <returns><c>200</c> with the zone identifier and whether it is still the one an unstated record falls to.</returns>
    /// <remarks>
    /// Answered from the roster rather than from a document read, because that is where every other reader of this
    /// value takes it from and a second source would be a second answer. <c>isDefault</c> is whether the record states
    /// a zone at all rather than whether it reads as the coordinated one: what it decides is whether the client may
    /// propose the zone the browser reports, and comparing identifiers would propose over a person who chose UTC.
    /// </remarks>
    internal static Ok<ClientTimeZoneResponse> Read(
        [FromServices] IMailUserTimeZones zones,
        [FromServices] AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(zones);
        ArgumentNullException.ThrowIfNull(authorization);

        var stated = zones.StatedZoneOf(authorization.RequireUser());

        return TypedResults.Ok(
            new ClientTimeZoneResponse((stated ?? MailUserTimeZone.Coordinated).Id, stated is null));
    }

    /// <summary>Records the zone the acting person states their own days are read in.</summary>
    /// <param name="records">The record administration, which judges and commits the change.</param>
    /// <param name="request">The zone they would be read in.</param>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns><c>200</c> with the zone now recorded, <c>404</c> when this deployment holds no record for the caller, or <c>400</c> when the identifier names no zone this deployment knows.</returns>
    internal static async Task<Results<Ok<ClientTimeZoneResponse>, NotFound<ProblemDetails>, ProblemHttpResult>> ChangeAsync(
        [FromServices] UserRecordAdministration records,
        [FromBody] ClientTimeZoneRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(request);

        if (!MailUserTimeZone.TryRead(request.TimeZone, out var zone))
        {
            return TypedResults.Problem(
                $"The time zone is an IANA identifier this deployment knows, such as 'Europe/Warsaw', at most {ZonedInstant.MaximumZoneIdLength} characters.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Never the default afterwards, whichever zone was named: the record now states one because this person said so.
        return await records.ChangeOwnTimeZoneAsync(zone, cancellationToken)
            ? TypedResults.Ok(new ClientTimeZoneResponse(zone.Id, IsDefault: false))
            : TypedResults.NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Detail = "This deployment holds no record for you.",
            });
    }
}

/// <summary>The zone a person states their own days are read in.</summary>
/// <param name="TimeZone">The IANA identifier, such as <c>Europe/Warsaw</c>.</param>
/// <remarks>
/// Bound strictly: a key nothing here binds fails the bind rather than being ignored, which is what stops a client
/// sending a field this surface never published and reading the unchanged answer as the change having landed.
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ClientTimeZoneRequest(string? TimeZone = null);

/// <summary>What the client endpoint reports about the zone one person's days are read in.</summary>
/// <param name="TimeZone">The IANA identifier this deployment records them under.</param>
/// <param name="IsDefault">Whether that is the zone an unstated record falls to rather than one anybody chose.</param>
internal sealed record ClientTimeZoneResponse(string TimeZone, bool IsDefault);
