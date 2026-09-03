// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;
using MailFathom.Application.Preferences;
using MailFathom.Domain.Access;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves the signed-in person what they set about their own client, and takes it back changed.</summary>
/// <remarks>
/// <para>
/// Two routes over one small closed document, held on the deployment so it follows a person between machines rather
/// than staying in the browser profile or the desktop install they last set it in. Somebody who declines telemetry on
/// their laptop has not agreed to anything on the next machine either, which is the reason this is here at all.
/// </para>
/// <para>
/// <b>Neither route names an owner.</b> The person is the one the credential authenticated, resolved from the request
/// exactly as the record routes resolve it, so a request reaching somebody else's preferences cannot be composed:
/// there is no argument to put another owner's identifier in and no listing to discover one from.
/// </para>
/// <para>
/// Both are <see cref="MailFathomPermission.MailRead" />, and neither adds a name to the published permission set. The
/// write deliberately does not take <see cref="MailFathomPermission.MailAccountsWrite" /> as the record's writes do:
/// that grant decides which mailboxes this deployment connects to, somebody whose accounts an administrator maintains
/// does not hold it, and what may be said about a person must not be decided by a grant over their mail configuration.
/// </para>
/// <para>
/// A write states the whole document, which is what makes it a closed set rather than a patch: a preference the body
/// omits is stored as its own unset answer rather than left at whatever the row held. It carries no version and is
/// last-write-wins, because the only writers are one person's own devices and a conflict screen over a checkbox would
/// be a worse answer than the second device winning.
/// </para>
/// </remarks>
internal static class ClientPreferencesEndpoint
{
    /// <summary>The route the acting person's client preferences are read at and written back to, relative to the client prefix.</summary>
    internal const string PreferencesRoute = "/preferences";

    /// <summary>The greatest request body the write route reads before refusing it.</summary>
    /// <remarks>
    /// Far above five scalars and their JSON escaping, and far below anything worth an allocation. The document is
    /// closed, so what the bound guards against is not a large record but a body that was never a preferences document
    /// at all; it is answered <c>413</c> before the handler is reached, as every other write on this surface is.
    /// </remarks>
    internal const int MaxWriteRequestBytes = 4 * 1024;

    /// <summary>Maps the preferences routes into the client group, so they inherit its requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientPreferences(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(PreferencesRoute, ReadAsync)
            .RequirePermission(MailFathomPermission.MailRead);

        // The attribute is reached for its metadata rather than as an MVC filter, for the reason the record routes
        // state: it implements IRequestSizeLimitMetadata, which the routing pipeline applies to the request body.
        api.MapPost(PreferencesRoute, SaveAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.MailRead);
    }

    /// <summary>Hands the acting person what they set about their own client.</summary>
    /// <param name="preferences">The acting person's own preferences.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the preferences, the unset answers where they have set nothing, or <c>400</c> when the stored row is not a document of preferences.</returns>
    /// <remarks>Setting nothing is answered with the defaults rather than with a refusal, because a first run draws a screen and an empty store is not an error. A person this deployment no longer holds is answered the same way, since the answer carries nothing of theirs to withhold.</remarks>
    internal static async Task<Results<Ok<ClientPreferencesResponse>, ProblemHttpResult>> ReadAsync(
        [FromServices] OwnClientPreferences preferences,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        try
        {
            return TypedResults.Ok(ClientPreferencesResponse.For(await preferences.ReadAsync(cancellationToken)));
        }
        catch (JsonException)
        {
            // The parser's own message names the token and the JSON path it stopped at, and neither is this surface's
            // to publish: the row is one only this deployment writes, so a reader learns what to do rather than where.
            return Refusal(
                "Your client preferences are not a document of preferences, so they cannot be read. Ask whoever administers this deployment to correct it.");
        }
    }

    /// <summary>Takes back what the acting person set about their own client.</summary>
    /// <param name="preferences">The acting person's own preferences.</param>
    /// <param name="request">The whole document, with an omitted preference stored as its unset answer.</param>
    /// <param name="cancellationToken">Cancels the commit.</param>
    /// <returns><c>200</c> with what is now stored, <c>404</c> when this deployment holds no record for the caller, or <c>400</c> when the body names a theme this build does not publish.</returns>
    internal static async Task<Results<Ok<ClientPreferencesResponse>, NotFound<ProblemDetails>, ProblemHttpResult>> SaveAsync(
        [FromServices] OwnClientPreferences preferences,
        [FromBody] ClientPreferencesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(request);

        if (request.Stated() is not { } stated)
        {
            return Refusal(
                $"A theme is one of {string.Join(", ", ClientThemeChoice.All.Select(choice => choice.Name))}.");
        }

        return await preferences.SaveAsync(stated, cancellationToken)
            ? TypedResults.Ok(ClientPreferencesResponse.For(stated))
            : TypedResults.NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Detail = "This deployment holds no record for you.",
            });
    }

    private static ProblemHttpResult Refusal(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status400BadRequest);
}

/// <summary>What a person states about their own client.</summary>
/// <param name="TelemetryEnabled">Whether this deployment may be told what their client is doing, or nothing to say what an unset switch says.</param>
/// <param name="Theme">The name of what the client is painted in, or nothing to follow the machine.</param>
/// <param name="OpenMailInTabs">Whether opening a message opens a tab, or nothing for the unset answer.</param>
/// <param name="MarkReadOnOpen">Whether opening a message marks it read on their mail server, or nothing for the unset answer.</param>
/// <param name="ExpandWholeThread">Whether a conversation opens with every message drawn, or nothing for the unset answer.</param>
/// <remarks>
/// <para>
/// Bound strictly: a key nothing here binds fails the bind rather than being stored, which is what keeps the document
/// closed — it holds five preferences because five is what a client can state, not because a writer happened to send
/// five. Every one of them is optional, and an omitted one is committed as its unset answer rather than left at
/// whatever the row held.
/// </para>
/// <para>
/// The theme travels as its name rather than as <see cref="ClientThemeChoice" /> itself, for the reason the
/// administrative surface maps a setting's source to a string before it crosses: a closed enumeration carries its own
/// serialization and the schema generator has nothing to publish for it, so a client would be handed an empty schema
/// where the three names it may send belong. Resolving the name here is also what lets a refusal say what is on offer.
/// </para>
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ClientPreferencesRequest(
    bool? TelemetryEnabled = null,
    string? Theme = null,
    bool? OpenMailInTabs = null,
    bool? MarkReadOnOpen = null,
    bool? ExpandWholeThread = null)
{
    /// <summary>Reads the request as the whole set the write commits.</summary>
    /// <returns>The preferences, with every one the body omitted answered as unset, or <see langword="null" /> when the body names a theme this build does not publish.</returns>
    internal ClientPreferences? Stated()
    {
        var theme = ClientPreferences.Unset.Theme;

        if (this.Theme is not null && !ClientThemeChoice.TryParse(this.Theme, out theme))
        {
            return null;
        }

        return new ClientPreferences(
            this.TelemetryEnabled ?? ClientPreferences.Unset.TelemetryEnabled,
            theme,
            this.OpenMailInTabs ?? ClientPreferences.Unset.OpenMailInTabs,
            this.MarkReadOnOpen ?? ClientPreferences.Unset.MarkReadOnOpen,
            this.ExpandWholeThread ?? ClientPreferences.Unset.ExpandWholeThread);
    }
}

/// <summary>What the client endpoint reports about one person's own client preferences.</summary>
/// <param name="TelemetryEnabled">Whether this deployment may be told what their client is doing.</param>
/// <param name="Theme">What the client is painted in once a session exists.</param>
/// <param name="OpenMailInTabs">Whether opening a message opens a tab rather than replacing what is on the screen.</param>
/// <param name="MarkReadOnOpen">Whether opening a message marks it read on the owner's own mail server.</param>
/// <param name="ExpandWholeThread">Whether a conversation opens with every message drawn rather than at the one it was opened at.</param>
/// <remarks>
/// Every preference is answered, whether or not the person ever set it, so a client renders one screen rather than one
/// per combination of what happens to be stored. What it does not report is when anything was set or from where: this
/// deployment keeps no record of the machines somebody signed in from, and a response saying when a switch last moved
/// would be the beginning of one.
/// </remarks>
internal sealed record ClientPreferencesResponse(
    bool TelemetryEnabled,
    string Theme,
    bool OpenMailInTabs,
    bool MarkReadOnOpen,
    bool ExpandWholeThread)
{
    /// <summary>Describes one person's preferences on the wire.</summary>
    /// <param name="preferences">What they set.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="preferences" /> is <see langword="null" />.</exception>
    internal static ClientPreferencesResponse For(ClientPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        return new ClientPreferencesResponse(
            preferences.TelemetryEnabled,
            preferences.Theme.Name,
            preferences.OpenMailInTabs,
            preferences.MarkReadOnOpen,
            preferences.ExpandWholeThread);
    }
}
