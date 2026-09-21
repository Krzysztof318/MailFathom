// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Application.Access;
using MailFathom.Application.Calendar.Extraction;
using MailFathom.Domain.Access;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Reads a sentence somebody typed into the one event it describes, without storing any of it.</summary>
/// <remarks>
/// <para>
/// Two routes on one address, because a caller has two different questions and only one of them costs anything. The
/// read says whether this deployment reads a description at all, so a form need not offer the field before anybody
/// types into it — a field promising to take a description over a deployment that reads none would fail a person at
/// the one moment they trusted it. The write reads one sentence and costs a provider call.
/// </para>
/// <para>
/// <strong>Nothing here writes to a calendar.</strong> What comes back is a reading a caller fills its own fields
/// from, to be saved through the ordinary create route or abandoned. A route that saved as well would put an event
/// somebody never read onto their calendar, which is the one thing every half of this feature is arranged to
/// prevent.
/// </para>
/// <para>
/// The sentence travels in a body rather than in a query string, which is the whole reason this is a <c>POST</c> for
/// an operation that changes nothing. What somebody is arranging and with whom is as revealing as the mail that
/// arranged it, and a query string is the part of a request that reaches an access log by default, on this deployment
/// and on every proxy in front of it.
/// </para>
/// <para>
/// It is published under the grant that governs asking a question rather than the one that governs reading mail,
/// because that is what it does: a sentence leaves this deployment for a chat provider and is charged to the same
/// allowance a question is. A caller holding only the reading grant is refused here and composes the event itself,
/// which is what a deployment with no provider leaves every caller doing.
/// </para>
/// </remarks>
internal static class ClientCalendarEventDraftEndpoint
{
    /// <summary>The route a description is read at, relative to the client prefix.</summary>
    internal const string CalendarEventDraftRoute = "/calendar/drafts";

    /// <summary>The greatest size a description may have on the wire.</summary>
    /// <remarks>Generous against the bound the text itself carries and against the envelope around it, and small enough that a body is refused before it is read rather than after.</remarks>
    internal const int MaxDescriptionRequestBytes = 4 * 1024;

    /// <summary>Maps the routes into the client group, so they inherit the group's requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientCalendarEventDrafts(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(CalendarEventDraftRoute, ReadsDescriptions)
            .RequirePermission(MailFathomPermission.MailAsk);

        // The MVC attribute is what a minimal API states this with, whatever its namespace suggests: it implements
        // IRequestSizeLimitMetadata, which the routing pipeline applies to the request body feature, so a body over
        // the bound is refused before anything reads it.
        api.MapPost(CalendarEventDraftRoute, DraftEventAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxDescriptionRequestBytes))
            .RequirePermission(MailFathomPermission.MailAsk);
    }

    /// <summary>Says whether this deployment turns a description into an event.</summary>
    /// <param name="extractor">Reads text into events, in whichever state this deployment left it.</param>
    /// <returns><c>200</c> saying whether a description is read, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.ask</c>.</returns>
    /// <remarks>
    /// A capability rather than a probe: it reads a registration and calls nothing, so a screen asking it once costs no
    /// provider call. The two reasons a deployment does not read a description — no endpoint, and an operator who
    /// turned it off — are one answer here on purpose, because what a client does about either is identical and naming
    /// which would publish a deployment's configuration to every signed-in browser.
    /// </remarks>
    internal static Ok<ClientCalendarEventDraftingResponse> ReadsDescriptions(
        [FromServices] ICalendarEventExtractor extractor)
    {
        ArgumentNullException.ThrowIfNull(extractor);

        return TypedResults.Ok(new ClientCalendarEventDraftingResponse(extractor.IsActive));
    }

    /// <summary>Reads one description, or reports what was wrong with the request.</summary>
    /// <param name="request">The sentence to read.</param>
    /// <param name="extractor">Reads the sentence, in whichever state this deployment left it.</param>
    /// <param name="userClock">Reads the instant whoever typed it is standing on, which every relative day and hour is resolved against.</param>
    /// <param name="cancellationToken">Cancels the reading when the client disconnects.</param>
    /// <returns><c>200</c> with the draft or with the statement that none was read, <c>400</c> naming what was wrong with the request, <c>429</c> where the deployment has spent what it allows a provider, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.ask</c>.</returns>
    /// <remarks>
    /// <para>
    /// A deployment that reads no description answers <c>200</c> saying so rather than <c>404</c>, because a person
    /// composing the event themselves has made no mistake and there is nothing for the caller to repair. The same
    /// answer is what a provider that failed produces, and deliberately so: what follows either is a form with
    /// nothing filled in.
    /// </para>
    /// <para>
    /// A spent allowance is the one failure that travels, because answering <c>200</c> there would leave a person
    /// typing sentences into a field that had quietly stopped costing the operator anything and stopped working.
    /// </para>
    /// </remarks>
    internal static async Task<Results<Ok<ClientCalendarEventDraftResponse>, ProblemHttpResult>> DraftEventAsync(
        [FromBody] ClientCalendarEventDraftRequest? request,
        [FromServices] ICalendarEventExtractor extractor,
        [FromServices] MailUserClock userClock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(userClock);

        if (request is null)
        {
            return Refuse("The request carries no description to read.");
        }

        if (!CalendarEventDescription.TryCreate(request.Description, userClock.Now(), out var description))
        {
            return Refuse(
                $"A description is non-blank and at most {CalendarEventDescription.MaximumTextLength} characters.");
        }

        var extraction = await extractor.DraftFromDescriptionAsync(description, cancellationToken);

        return extraction.Withheld switch
        {
            CalendarEventExtractionWithholding.AllowanceExhausted => TypedResults.Problem(
                "This deployment has spent what it allows a chat provider for the current period, so the description was not read.",
                statusCode: StatusCodes.Status429TooManyRequests),
            _ => TypedResults.Ok(ClientCalendarEventDraftResponse.For(extraction)),
        };
    }

    /// <summary>States what a caller has to change, without echoing what they sent.</summary>
    /// <remarks>Without echoing it because a description of what somebody is arranging is the most revealing value this route carries, and a problem detail is the one part of a response that reaches a log by default.</remarks>
    private static ProblemHttpResult Refuse(string stated) =>
        TypedResults.Problem(stated, statusCode: StatusCodes.Status400BadRequest);

}

/// <summary>What this deployment does with a description, which is what says whether a caller may offer the field.</summary>
/// <param name="ReadsDescriptions">Whether a typed description is read into an event draft.</param>
/// <remarks>
/// One field and no reason beside it. A deployment that declared no chat endpoint and one whose operator turned this
/// off are the same answer to a caller — there is no description to send — and naming which would publish a
/// deployment's configuration to every signed-in browser to answer a question nobody asked.
/// </remarks>
internal sealed record ClientCalendarEventDraftingResponse(bool ReadsDescriptions);

/// <summary>One description to read.</summary>
/// <param name="Description">What was typed, which the deployment bounds as a sentence rather than as a body of text.</param>
/// <remarks>
/// The instant <em>tomorrow at nine</em> is resolved against is not here, deliberately. It used to arrive as a request
/// field, which made an unverified client value part of what a provider's prompt is composed from; the deployment now
/// reads it from its own clock and the zone that person's record carries, which still means nine where they are
/// without taking a caller's word for where that is.
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ClientCalendarEventDraftRequest(string? Description);

/// <summary>What one description was read as: the event it describes, or the statement that none was read.</summary>
/// <param name="Drafted">Whether a draft was read at all, which is <see langword="false" /> where this deployment reads no description, the provider could not be reached, or the sentence described no occasion.</param>
/// <param name="Title">What to call the event, or <see langword="null" /> where nothing was drafted.</param>
/// <param name="Start">When it begins, written with the offset it was read in, or <see langword="null" /> where nothing was drafted.</param>
/// <param name="End">When it ends, or <see langword="null" /> where the sentence stated no length or nothing was drafted.</param>
/// <remarks>
/// <para>
/// <c>drafted</c> being <see langword="false" /> is not a failure a client reports: there is simply nothing to fill
/// a form with. It is what a deployment with no provider answers, and what this one answers while its provider is
/// unreachable or the sentence named no day.
/// </para>
/// <para>
/// Nothing here is stored. The reading lives only in the answer, and the event that reaches a calendar is the one
/// whoever typed the sentence submitted rather than the one a model wrote.
/// </para>
/// </remarks>
internal sealed record ClientCalendarEventDraftResponse(
    bool Drafted,
    string? Title,
    DateTimeOffset? Start,
    DateTimeOffset? End)
{
    /// <summary>The answer a description gets where nothing was drafted from it.</summary>
    internal static ClientCalendarEventDraftResponse NotDrafted { get; } = new(
        Drafted: false,
        Title: null,
        Start: null,
        End: null);

    /// <summary>Describes one extraction for the wire.</summary>
    /// <param name="extraction">What the port answered with.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="extraction" /> is <see langword="null" />.</exception>
    internal static ClientCalendarEventDraftResponse For(CalendarEventExtraction extraction)
    {
        ArgumentNullException.ThrowIfNull(extraction);

        return extraction.Events is [var drafted, ..]
            ? new ClientCalendarEventDraftResponse(Drafted: true, drafted.Title.Value, drafted.Start, drafted.End)
            : NotDrafted;
    }
}
