// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.Application.Contacts.Relationship;
using MailFathom.Application.Discovery.Presentation.Citations;
using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves the note an opened contact is headed by: where the correspondence with that person stands.</summary>
/// <remarks>
/// <para>
/// It is the third and most expensive half of reading one contact. The record says who somebody is, the correlation
/// says what the two of them have been doing, and this reads that correlation into a few sentences somebody reads
/// instead of scrolling it — plus what to do next, when this person is reachable, what is outstanding, and which
/// matters the exchanges are about, each leading back to the conversation or the document it rests on.
/// </para>
/// <para>
/// It stands on a route of its own for the reason the correlation does, one cost further along: a record is a row, a
/// correlation is a bounded walk of a year of mail, and this is that walk plus a provider call charged to the
/// deployment's answering allowance. A client that draws no card never pays for one, and one that does asks for it
/// deliberately.
/// </para>
/// <para>
/// <b>Nothing here runs unless somebody opened that contact.</b> There is no sweep over the book, no schedule, and no
/// background pass; a contact nobody opened costs nothing at all.
/// </para>
/// <para>
/// It is published under the grant that governs asking a question rather than the one that governs reading mail,
/// because that is what it does: a correspondence leaves this deployment for a chat provider, and the call is charged
/// to the same allowance a question is. The two grants the correlation beneath it needs are asked by the use cases
/// that need them, so a caller granted this one and not those is refused naming the one they are missing.
/// </para>
/// <para>
/// A deployment that derives no card answers <c>200</c> saying so, without reading the contact and without touching
/// mail. That is what a client reads to know whether to draw the card at all, and it is the same answer a provider
/// that could not be reached produces — because what follows either is the contact page without a card.
/// </para>
/// <para>
/// It speaks to no mail server, so a request from a browser cannot wait on IMAP and cannot set the remote <c>\Seen</c>
/// flag.
/// </para>
/// </remarks>
internal static class ClientContactRelationshipEndpoint
{
    /// <summary>The route reporting where a correspondence with one contact stands, relative to the client prefix.</summary>
    internal const string ContactRelationshipRoute = "/contacts/{contactId:guid}/relationship";

    /// <summary>Maps the route into the client group, so it inherits the group's requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientContactRelationship(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(ContactRelationshipRoute, ReadRelationshipAsync)
            .RequirePermission(MailFathomPermission.MailAsk);
    }

    /// <summary>Derives the card for one of the acting user's own contacts.</summary>
    /// <param name="contactId">The contact to read, as the book published it.</param>
    /// <param name="contacts">Reads the contact, for a caller the book's own grant admits.</param>
    /// <param name="relationship">Derives the card, or <see langword="null" /> where this deployment declared no chat endpoint or declined this.</param>
    /// <param name="cancellationToken">Cancels the derivation when the client disconnects.</param>
    /// <returns><c>200</c> with the card or with the statement that none was derived, <c>404</c> where no book in this caller's scope holds the contact, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.ask</c>.</returns>
    /// <remarks>
    /// A contact this caller does not hold and one no deployment ever held answer identically, so nothing here tells a
    /// caller that somebody else's correspondent exists. A person the readable mail says nothing about answers with no
    /// card rather than with a refusal, which is the accurate answer: there is nothing for a derivation to rest on.
    /// </remarks>
    internal static async Task<Results<Ok<ClientContactRelationshipResponse>, NotFound>> ReadRelationshipAsync(
        [FromRoute] Guid contactId,
        [FromServices] ContactBookReader contacts,
        [FromServices] ContactRelationshipReader? relationship,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contacts);

        if (contactId == Guid.Empty)
        {
            return TypedResults.NotFound();
        }

        // Answered before the book is read, because a deployment that derives nothing has the same answer for every
        // contact and reading one would put a query behind a question already settled. It is answered after the
        // identifier is judged all the same, so that one contact answers the same way whatever this deployment derives.
        if (relationship is null)
        {
            return TypedResults.Ok(ClientContactRelationshipResponse.NotDerived(contactId));
        }

        var contact = await contacts.FindAsync(ContactId.Create(contactId), cancellationToken);

        if (contact is null)
        {
            return TypedResults.NotFound();
        }

        var card = await relationship.ReadAsync(contact, cancellationToken);

        return TypedResults.Ok(ClientContactRelationshipResponse.For(contactId, card));
    }
}

/// <summary>Where the correspondence with one contact stands, as the client endpoint serves it.</summary>
/// <param name="ContactId">The contact, as the request named it.</param>
/// <param name="Derived">Whether a card was derived at all, which is <see langword="false" /> where this deployment derives none, the allowance was spent, the provider could not be reached, or this person's mail carries nothing to rest on.</param>
/// <param name="Note">The note the card is headed by, or <see langword="null" /> where none was derived.</param>
/// <param name="NextAction">What the derivation suggests doing next, or <see langword="null" /> where it suggested nothing — which a settled correspondence does.</param>
/// <param name="Observations">What it observed beside the note, at most one per aspect, which is empty where it observed nothing.</param>
/// <remarks>
/// <para>
/// <c>derived</c> being <see langword="false" /> is not a failure a client reports. It is the contact page without a
/// card, which is the page a deployment with no provider serves and the page this one serves while its provider is
/// unreachable.
/// </para>
/// <para>
/// All of it is derived from somebody's mail and about a named person, so it carries the classification of both: none
/// of it reaches a log, a span attribute, or a telemetry event, and nothing here is stored — the next reader to open
/// this contact derives it again from whatever the correspondence says then.
/// </para>
/// </remarks>
internal sealed record ClientContactRelationshipResponse(
    Guid ContactId,
    bool Derived,
    ClientContactRelationshipStatementResponse? Note,
    ClientContactRelationshipStatementResponse? NextAction,
    IReadOnlyList<ClientContactRelationshipObservationResponse> Observations)
{
    /// <summary>Describes the answer every case that produced no card gives.</summary>
    /// <param name="contactId">The contact the request named.</param>
    /// <returns>The response body.</returns>
    internal static ClientContactRelationshipResponse NotDerived(Guid contactId) =>
        new(contactId, Derived: false, Note: null, NextAction: null, []);

    /// <summary>Describes one contact's card for the wire.</summary>
    /// <param name="contactId">The contact the request named.</param>
    /// <param name="card">What the use case derived.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="card" /> is <see langword="null" />.</exception>
    internal static ClientContactRelationshipResponse For(Guid contactId, ContactRelationship card)
    {
        ArgumentNullException.ThrowIfNull(card);

        return card.WasDerived
            ? new ClientContactRelationshipResponse(
                contactId,
                Derived: true,
                ClientContactRelationshipStatementResponse.ForOptional(card.Note),
                ClientContactRelationshipStatementResponse.ForOptional(card.NextAction),
                [.. card.Observations.Select(ClientContactRelationshipObservationResponse.For)])
            : NotDerived(contactId);
    }
}

/// <summary>One observation the card carries beside its note.</summary>
/// <param name="Aspect">Which of the three the observation is: <c>ActivePeriod</c>, <c>OpenItem</c>, or <c>Case</c>.</param>
/// <param name="Statement">What was observed, and what it rests on.</param>
/// <remarks>
/// The aspect is the label a client draws the value under and is this deployment's own rather than a producer's, so a
/// card cannot grow a heading nobody designed. An aspect the derivation said nothing about is absent rather than
/// present and empty.
/// </remarks>
internal sealed record ClientContactRelationshipObservationResponse(
    string Aspect,
    ClientContactRelationshipStatementResponse Statement)
{
    /// <summary>Describes one observation for the wire.</summary>
    /// <param name="observation">The observation the card carries.</param>
    /// <returns>The response body.</returns>
    internal static ClientContactRelationshipObservationResponse For(ContactRelationshipObservation observation) =>
        new(observation.Aspect.ToString(), ClientContactRelationshipStatementResponse.For(observation.Statement));
}

/// <summary>One thing the card says, with what it rests on.</summary>
/// <param name="Text">The statement itself, in the language this caller's mailbox is read in.</param>
/// <param name="Sources">The conversations and documents it rests on, best first, spelled as the presentation plan spells a citation target.</param>
/// <remarks>
/// Every statement carries at least one source, because a card is read instead of the correspondence it was derived
/// from and a sentence with nothing behind it would sit beside the sourced ones looking exactly like them. A
/// conversation is cited as its most recent message involving this person and a document as that message's own
/// attachment, so both resolve through the same <c>POST /citations/resolution</c> a Discover answer's sources do.
/// </remarks>
internal sealed record ClientContactRelationshipStatementResponse(
    string Text,
    IReadOnlyList<ClientCitationRequest> Sources)
{
    /// <summary>Describes one statement for the wire.</summary>
    /// <param name="statement">The statement the card carries.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="statement" /> is <see langword="null" />.</exception>
    internal static ClientContactRelationshipStatementResponse For(ContactRelationshipStatement statement)
    {
        ArgumentNullException.ThrowIfNull(statement);

        return new ClientContactRelationshipStatementResponse(
            statement.Text,
            [
                .. statement.Sources.Select(static source => source.AttachmentPosition is { } position
                    ? new ClientCitationRequest(
                        AttachmentCitationTarget.Kind,
                        source.StoredEmailId.Value,
                        null,
                        position)
                    : new ClientCitationRequest(
                        EmailCitationTarget.Kind,
                        source.StoredEmailId.Value,
                        null,
                        null)),
            ]);
    }

    /// <summary>Describes one statement the card may not carry at all.</summary>
    /// <param name="statement">The statement, or <see langword="null" /> where the derivation wrote none.</param>
    /// <returns>The response body, or <see langword="null" /> where there was no statement.</returns>
    internal static ClientContactRelationshipStatementResponse? ForOptional(ContactRelationshipStatement? statement) =>
        statement is null ? null : For(statement);
}
