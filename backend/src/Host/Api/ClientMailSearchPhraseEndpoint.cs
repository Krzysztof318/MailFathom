// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.Search.Phrasing;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Domain.Access;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Reads a sentence somebody typed into the filters and criteria a search should be made of.</summary>
/// <remarks>
/// <para>
/// Two routes on one address, because a screen has two different questions and only one of them costs anything. The
/// read says whether this deployment reads a sentence at all, which is what decides whether the field may promise a
/// description before anybody types one — a field offering to take a description over a deployment that can only match
/// words fails a person at the one moment they trusted it. The write reads one sentence and costs a provider call.
/// </para>
/// <para>
/// <strong>What comes back is an interpretation rather than results.</strong> Nothing here searches: the filters and
/// the criteria are handed to the screen, which draws each of them as an object a person can see, change, and remove,
/// and then asks the search route with whatever survives that. A route that searched as well would make the
/// interpretation invisible, and an interpretation nobody can see is one nobody can correct.
/// </para>
/// <para>
/// The sentence travels in a body rather than in a query string, which is the whole reason this is a <c>POST</c> for an
/// operation that changes nothing. What somebody is looking for in their own mailbox is the most revealing value this
/// surface carries, and a query string is the part of a request that reaches an access log by default, on this
/// deployment and on every proxy in front of it.
/// </para>
/// <para>
/// It is published under the grant that governs asking a question rather than the one that governs reading mail,
/// because that is what it does: a sentence leaves this deployment for a chat provider and is charged to the same
/// allowance a question is. A caller holding only the reading grant is refused here and searches by words, which is the
/// same search a deployment with no provider serves.
/// </para>
/// </remarks>
internal static class ClientMailSearchPhraseEndpoint
{
    /// <summary>The route a sentence is read at, relative to the client prefix.</summary>
    internal const string MailSearchPhrasingRoute = "/emails/search/phrasing";

    /// <summary>The greatest size a sentence may have on the wire.</summary>
    /// <remarks>Generous against the bound the text itself carries and against the envelope around it, and small enough that a body is refused before it is read rather than after.</remarks>
    internal const int MaxPhraseRequestBytes = 4 * 1024;

    /// <summary>Maps the routes into the client group, so they inherit the group's requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientMailSearchPhrasing(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(MailSearchPhrasingRoute, ReadsPhrases)
            .RequirePermission(MailFathomPermission.MailAsk);

        // The MVC attribute is what a minimal API states this with, whatever its namespace suggests: it implements
        // IRequestSizeLimitMetadata, which the routing pipeline applies to the request body feature, so a body over
        // the bound is refused before anything reads it.
        api.MapPost(MailSearchPhrasingRoute, ReadPhraseAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxPhraseRequestBytes))
            .RequirePermission(MailFathomPermission.MailAsk);
    }

    /// <summary>Says whether this deployment turns a sentence into filters.</summary>
    /// <param name="reader">Reads a sentence, or <see langword="null" /> where this deployment declared no chat endpoint or declined this.</param>
    /// <returns><c>200</c> saying whether a sentence is read, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.ask</c>.</returns>
    /// <remarks>
    /// A capability rather than a probe: it resolves a registration and calls nothing, so a screen asking it once
    /// costs no provider call and no mail read. The two reasons a deployment does not read a sentence — no endpoint,
    /// and an operator who turned it off — are one answer here on purpose, because what a client does about either is
    /// identical and naming which would publish a deployment's configuration to every signed-in browser.
    /// </remarks>
    internal static Ok<ClientMailSearchPhrasingResponse> ReadsPhrases(
        [FromServices] IMailSearchPhraseReader? reader) =>
        TypedResults.Ok(new ClientMailSearchPhrasingResponse(reader is not null));

    /// <summary>Reads one sentence, or reports what was wrong with the request.</summary>
    /// <param name="request">The sentence and the reader's own calendar day.</param>
    /// <param name="reader">Reads the sentence, or <see langword="null" /> where this deployment does not.</param>
    /// <param name="cancellationToken">Cancels the reading when the client disconnects.</param>
    /// <returns><c>200</c> with the interpretation, <c>400</c> naming what was wrong with the request, <c>429</c> where the deployment has spent what it allows a provider, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.ask</c>.</returns>
    /// <remarks>
    /// <para>
    /// A deployment that reads no sentence answers <c>200</c> saying so rather than <c>404</c>, because a client
    /// searching by words has not made a mistake and there is nothing for it to repair. The same answer is what a
    /// provider that failed produces, and deliberately so: the search that follows either is the words somebody typed.
    /// </para>
    /// <para>
    /// A spend ceiling is the one failure that travels, because falling back there would spend the search on a call the
    /// operator had already declined to pay for, and a person who is told nothing would go on typing sentences.
    /// </para>
    /// </remarks>
    internal static async Task<Results<Ok<ClientMailSearchPhraseResponse>, ProblemHttpResult>> ReadPhraseAsync(
        [FromBody] ClientMailSearchPhraseRequest? request,
        [FromServices] IMailSearchPhraseReader? reader,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Refuse("The request carries no sentence to read.");
        }

        if (!TryReadDay(request.AskedOn, out var askedOn))
        {
            return Refuse("The day the sentence was typed on is written as yyyy-mm-dd.");
        }

        EmailSearchQueryText text;
        try
        {
            text = EmailSearchQueryText.Create(request.Phrase);
        }
        catch (MailboxQueryFilterInvalidException refusal)
        {
            return Refuse(refusal.Message);
        }

        if (reader is null)
        {
            return TypedResults.Ok(ClientMailSearchPhraseResponse.NotRead);
        }

        try
        {
            var reading = await reader.ReadAsync(new MailSearchPhrase(text, askedOn), cancellationToken);

            return TypedResults.Ok(ClientMailSearchPhraseResponse.For(reading));
        }
        catch (MailAnsweringBudgetExhaustedException refusal)
        {
            return TypedResults.Problem(refusal.Message, statusCode: StatusCodes.Status429TooManyRequests);
        }
    }

    /// <summary>States what a caller has to change, without echoing what they sent.</summary>
    /// <remarks>Without echoing it because a sentence describing the mail somebody is looking for is the most revealing value this surface carries, and a problem detail is the one part of a response that reaches a log by default.</remarks>
    private static ProblemHttpResult Refuse(string stated) =>
        TypedResults.Problem(stated, statusCode: StatusCodes.Status400BadRequest);

    /// <summary>Reads the calendar day a client states it is standing on.</summary>
    /// <remarks>
    /// One format and the invariant culture, because the value is composed by a program rather than typed by a person:
    /// accepting whatever the server's culture would also parse is how a day and a month come to be read the wrong way
    /// round, and a search silently narrowed to the wrong three months is exactly the failure this screen exists to
    /// make visible.
    /// </remarks>
    private static bool TryReadDay(string? written, out DateOnly day) =>
        DateOnly.TryParseExact(
            written,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out day);
}

/// <summary>What this deployment does with a sentence, which is what says whether a field may promise one.</summary>
/// <param name="ReadsPhrases">Whether a typed sentence is read into filters and criteria before the search runs.</param>
/// <remarks>
/// One field and no reason beside it. A deployment that declared no chat endpoint and one whose operator turned this
/// off are the same answer to a screen — offer the word search — and naming which would publish a deployment's
/// configuration to every signed-in browser to answer a question nobody asked.
/// </remarks>
internal sealed record ClientMailSearchPhrasingResponse(bool ReadsPhrases);

/// <summary>One sentence to read, and the day whoever typed it is standing on.</summary>
/// <param name="Phrase">What was typed, which the deployment bounds exactly as it bounds a search's own text.</param>
/// <param name="AskedOn">The client's own calendar day as <c>yyyy-mm-dd</c>, which every relative time expression is resolved against.</param>
/// <remarks>
/// The day comes from the client rather than from the deployment's clock, and that is the point of it: <em>last
/// quarter</em> means the quarter of whoever typed it, and a deployment in another zone would resolve it to a range
/// they never asked for on the two days a year the two disagree.
/// </remarks>
internal sealed record ClientMailSearchPhraseRequest(string? Phrase, string? AskedOn);

/// <summary>What one sentence was read as: the constraints, what is left to rank by, and the part nothing was made of.</summary>
/// <param name="Read">Whether a reading happened at all, which is <see langword="false" /> where this deployment reads no sentence or the provider could not be reached.</param>
/// <param name="Filters">The constraints the sentence states, each of which the screen draws as an object that can be removed.</param>
/// <param name="Criteria">What is left to rank by, best first, which orders results and excludes nothing.</param>
/// <param name="Unaccounted">The part of the sentence nothing was made of, or <see langword="null" /> where all of it was read.</param>
/// <remarks>
/// <para>
/// The three parts stay separate on the wire because they are three different promises to whoever typed the sentence,
/// and a client that received them folded together could not keep them apart on the screen either.
/// </para>
/// <para>
/// <c>read</c> being <see langword="false" /> is not a failure a client reports. It is the plain word search, which is
/// what a deployment with no provider serves and what this one serves while its provider is unreachable.
/// </para>
/// </remarks>
internal sealed record ClientMailSearchPhraseResponse(
    bool Read,
    ClientMailSearchPhraseFiltersResponse Filters,
    IReadOnlyList<string> Criteria,
    string? Unaccounted)
{
    /// <summary>The answer a sentence gets where nothing read it, which leaves the word search exactly as it was.</summary>
    internal static ClientMailSearchPhraseResponse NotRead { get; } = new(
        Read: false,
        ClientMailSearchPhraseFiltersResponse.None,
        [],
        Unaccounted: null);

    /// <summary>Describes one reading for the wire.</summary>
    /// <param name="reading">The reading the port answered with.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reading" /> is <see langword="null" />.</exception>
    internal static ClientMailSearchPhraseResponse For(MailSearchPhraseReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        return new ClientMailSearchPhraseResponse(
            reading.WasRead,
            ClientMailSearchPhraseFiltersResponse.For(reading.Filters),
            reading.Criteria,
            reading.Unaccounted);
    }
}

/// <summary>The constraints half of a reading, in the shape the search route is asked with.</summary>
/// <param name="Sender">The address the sender must carry, or <see langword="null" /> for any sender.</param>
/// <param name="Recipient">The address a recipient must carry, or <see langword="null" /> for any recipient.</param>
/// <param name="ReceivedFrom">The first calendar day the search reaches back to as <c>yyyy-mm-dd</c>, or <see langword="null" /> for no start.</param>
/// <param name="ReceivedTo">The last calendar day it reaches, inclusive, as <c>yyyy-mm-dd</c>, or <see langword="null" /> for no end.</param>
/// <param name="Unread">Whether only unread mail may come back.</param>
/// <param name="Flagged">Whether only flagged mail may come back.</param>
/// <param name="HasAttachments">Whether only mail carrying attachments may come back.</param>
/// <remarks>
/// Calendar days rather than instants, because that is what the screen's own date controls hold and what its chips say
/// out loud. Turning a day into the half-open range the search route takes is the client's, done in the reader's own
/// zone, and doing it here instead would resolve somebody's fifteenth in whichever zone the deployment happens to run.
/// </remarks>
internal sealed record ClientMailSearchPhraseFiltersResponse(
    string? Sender,
    string? Recipient,
    string? ReceivedFrom,
    string? ReceivedTo,
    bool Unread,
    bool Flagged,
    bool HasAttachments)
{
    /// <summary>The constraints of a sentence that stated none.</summary>
    internal static ClientMailSearchPhraseFiltersResponse None { get; } = new(
        Sender: null,
        Recipient: null,
        ReceivedFrom: null,
        ReceivedTo: null,
        Unread: false,
        Flagged: false,
        HasAttachments: false);

    /// <summary>Describes one set of constraints for the wire.</summary>
    /// <param name="filters">The constraints the reading produced.</param>
    /// <returns>The response body's filters.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="filters" /> is <see langword="null" />.</exception>
    internal static ClientMailSearchPhraseFiltersResponse For(MailSearchPhraseFilters filters)
    {
        ArgumentNullException.ThrowIfNull(filters);

        return new ClientMailSearchPhraseFiltersResponse(
            filters.SenderAddress,
            filters.RecipientAddress,
            Day(filters.ReceivedFrom),
            Day(filters.ReceivedTo),
            filters.Unread,
            filters.Flagged,
            filters.HasAttachments);
    }

    private static string? Day(DateOnly? day) => day?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
