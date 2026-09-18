// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.Application.Contacts.Failures;
using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves the signed-in user their own address book: the two ways they read it, and the four acts they perform on it.</summary>
/// <remarks>
/// <para>
/// A second surface over the book beside the administrative <see cref="ContactEndpoints" />, not a replacement for it.
/// That one serves <c>mfctl</c> under the administrative grants and names the user it acts for; this one serves the
/// person holding the session and names nobody — the books reached are <see cref="ContactBookOwnership" />'s answer
/// about the acting user, so a request about somebody else's correspondents is something a caller cannot express here
/// rather than something this surface has to refuse.
/// </para>
/// <para>
/// <b>The book is read as two, because it is two.</b> What the user wrote down and what their mailboxes picked up are
/// different things to a person looking at a screen — one is their address book and the other is everybody they have
/// ever been written to by — so each is its own paginated route rather than one listing with a filter a client has to
/// know to send. Both walk the same order, so a cursor from either continues in the other.
/// </para>
/// <para>
/// Every write acts under <see cref="ContactOrigin.Asserted" />, which is <see cref="ContactBookWriter" />'s decision
/// rather than this file's: what somebody types into their own client is a person they wrote down. What follows from it
/// is that amending a contact one of their mailboxes collected is refused until it has been promoted, and promotion is
/// published here as the act it is.
/// </para>
/// <para>
/// <b>Two routes the administrative surface publishes are deliberately absent.</b> There is no export, because that is
/// what a data-subject request is answered from and it belongs on the surface an operator answers one from rather than
/// on a session-scoped one. And there is no bulk erase of the collected book: what a person disposes of here is one
/// person at a time, and emptying what a mailbox picked up is an act about the mailbox that an operator takes.
/// </para>
/// <para>
/// Reading is <see cref="MailFathomPermission.MailContactsRead" /> and every write is
/// <see cref="MailFathomPermission.MailContactsWrite" />, the same split the tools over this book already publish, and
/// the erasure is behind the writing grant rather than a narrower one — a grant that may edit the book may take
/// somebody out of it, and no smaller grant reaches an act that cannot be undone.
/// </para>
/// <para>
/// <b>Nothing a contact carries reaches a refusal.</b> Every message a refusal states comes from this system rather
/// than from the request, so a malformed address is reported as an address that is not usable instead of being echoed
/// into a problem document a proxy log keeps.
/// </para>
/// </remarks>
internal static class ClientContactEndpoints
{
    /// <summary>The route the user's own book is listed and written to, relative to the client prefix.</summary>
    internal const string ContactsRoute = "/contacts";

    /// <summary>The route the contacts the user's mailboxes collected are listed at, relative to the client prefix.</summary>
    /// <remarks>
    /// A literal segment where the single-contact route takes an identifier, which routing prefers over a parameter, so
    /// the two cannot be confused. The segment names the origin rather than an action, because what it lists is the
    /// half of the book the user did not write.
    /// </remarks>
    internal const string CollectedContactsRoute = "/contacts/collected";

    /// <summary>The route one contact is read, amended, and erased at, relative to the client prefix.</summary>
    internal const string ContactRoute = "/contacts/{contactId:guid}";

    /// <summary>The route a collected contact is taken on at, relative to the client prefix.</summary>
    internal const string ContactPromotionRoute = "/contacts/{contactId:guid}/promotion";

    /// <summary>Maps the contact routes into the client group, so they inherit its requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientContacts(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(ContactsRoute, ReadOwnBookAsync)
            .RequirePermission(MailFathomPermission.MailContactsRead);

        api.MapGet(CollectedContactsRoute, ReadCollectedAsync)
            .RequirePermission(MailFathomPermission.MailContactsRead);

        api.MapGet(ContactRoute, FindAsync)
            .RequirePermission(MailFathomPermission.MailContactsRead);

        // The attribute is reached for its metadata rather than as an MVC filter, exactly as the administrative writes
        // reach it: it implements IRequestSizeLimitMetadata, which the routing pipeline applies to the request body
        // feature, so a body over the bound is answered 413 before the handler is reached. The bound is the
        // administrative one because it bounds the same record.
        api.MapPost(ContactsRoute, RecordAsync)
            .WithMetadata(new RequestSizeLimitAttribute(ContactEndpoints.MaxRecordRequestBytes))
            .RequirePermission(MailFathomPermission.MailContactsWrite);

        api.MapPut(ContactRoute, AmendAsync)
            .WithMetadata(new RequestSizeLimitAttribute(ContactEndpoints.MaxRecordRequestBytes))
            .RequirePermission(MailFathomPermission.MailContactsWrite);

        api.MapPost(ContactPromotionRoute, PromoteAsync)
            .RequirePermission(MailFathomPermission.MailContactsWrite);

        api.MapDelete(ContactRoute, EraseAsync)
            .RequirePermission(MailFathomPermission.MailContactsWrite);
    }

    /// <summary>Serves one bounded page of the people the user wrote down.</summary>
    /// <param name="pageSize">How many contacts the page may hold, or <see langword="null" /> for the default.</param>
    /// <param name="cursor">The cursor the previous page returned, or <see langword="null" /> for the first page.</param>
    /// <param name="contacts">Reads the page from the books the acting user reads.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the page, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>
    /// There is no unbounded reading of the book. A caller that names no page size is served the default rather than
    /// everything, and one that asks for more than the ceiling is refused rather than quietly served the ceiling —
    /// which is what stops a request from deciding how much of a person's correspondents leaves the database at once.
    /// </remarks>
    internal static Task<Results<Ok<ContactPageResponse>, ProblemHttpResult>> ReadOwnBookAsync(
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor,
        [FromServices] ContactBookReader contacts,
        CancellationToken cancellationToken) =>
        ReadPageAsync(ContactOrigin.Asserted, pageSize, cursor, contacts, cancellationToken);

    /// <summary>Serves one bounded page of the people the user's own mailboxes picked up.</summary>
    /// <param name="pageSize">How many contacts the page may hold, or <see langword="null" /> for the default.</param>
    /// <param name="cursor">The cursor the previous page returned, or <see langword="null" /> for the first page.</param>
    /// <param name="contacts">Reads the page from the books the acting user reads.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the page, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>
    /// A user assigned no mail account reads an empty page here rather than a refusal, because collecting nobody is the
    /// state their book is in.
    /// </remarks>
    internal static Task<Results<Ok<ContactPageResponse>, ProblemHttpResult>> ReadCollectedAsync(
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor,
        [FromServices] ContactBookReader contacts,
        CancellationToken cancellationToken) =>
        ReadPageAsync(ContactOrigin.Collected, pageSize, cursor, contacts, cancellationToken);

    /// <summary>Serves one contact of the acting user's by the identity the book gave it.</summary>
    /// <param name="contactId">The contact to read.</param>
    /// <param name="contacts">Answers what the books the acting user reads hold.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the contact, or <c>404</c> where those books hold no such person.</returns>
    /// <remarks>
    /// A contact outside the books this user reads and one nobody holds answer identically, which is what keeps this
    /// route from reporting whose correspondents exist beside their own.
    /// </remarks>
    internal static async Task<Results<Ok<ContactResponse>, NotFound>> FindAsync(
        [FromRoute] Guid contactId,
        [FromServices] ContactBookReader contacts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contacts);

        if (NamedContact(contactId) is not { } identity)
        {
            return TypedResults.NotFound();
        }

        return await contacts.FindAsync(identity, cancellationToken) is { } held
            ? TypedResults.Ok(ContactResponse.For(held))
            : TypedResults.NotFound();
    }

    /// <summary>Records a person the acting user's own book does not yet hold.</summary>
    /// <param name="request">The record to write.</param>
    /// <param name="contacts">Performs the write into the acting user's own book.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with the outcome, or <c>400</c> naming which rule the record broke.</returns>
    internal static async Task<Results<Ok<ContactWriteResponse>, ProblemHttpResult>> RecordAsync(
        [FromBody] ContactRecordRequest? request,
        [FromServices] ContactBookWriter contacts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contacts);

        if (DraftOf(request) is not { } draft)
        {
            return NoRecord();
        }

        try
        {
            return TypedResults.Ok(ContactWriteResponse.For(await contacts.RecordAsync(draft, cancellationToken)));
        }
        catch (ContactRecordInvalidException refusal)
        {
            return Refuse(refusal.Message);
        }
    }

    /// <summary>Amends one contact of the acting user's own book to the record they state.</summary>
    /// <param name="contactId">The contact to amend.</param>
    /// <param name="request">The record the contact is to have afterwards.</param>
    /// <param name="contacts">Performs the write.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with the outcome, or <c>400</c> naming which rule the record broke.</returns>
    /// <remarks>
    /// The whole record rather than the difference from the one held, which is what keeps adding an address, dropping
    /// one, choosing a different preferred address, and correcting a name one operation whose result the book's
    /// invariants are checked against. A contact a mailbox collected answers with the outcome that its origin refuses
    /// the writer, which is the answer that tells a screen to offer the promotion instead.
    /// </remarks>
    internal static async Task<Results<Ok<ContactWriteResponse>, ProblemHttpResult>> AmendAsync(
        [FromRoute] Guid contactId,
        [FromBody] ContactRecordRequest? request,
        [FromServices] ContactBookWriter contacts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contacts);

        if (NamedContact(contactId) is not { } identity)
        {
            return NoContact();
        }

        if (DraftOf(request) is not { } draft)
        {
            return NoRecord();
        }

        try
        {
            return TypedResults.Ok(
                ContactWriteResponse.For(await contacts.AmendAsync(identity, draft, cancellationToken)));
        }
        catch (ContactRecordInvalidException refusal)
        {
            return Refuse(refusal.Message);
        }
    }

    /// <summary>Takes on a contact one of the acting user's mailboxes collected, so it becomes one they asserted.</summary>
    /// <param name="contactId">The contact to promote.</param>
    /// <param name="contacts">Performs the write.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with the outcome and no record, including for a contact that was already asserted.</returns>
    /// <remarks>
    /// The only write here whose caller states no record, and therefore the only one whose answer would be the book's
    /// own contents rather than the request's. It is published under the writing grant while reading the book is its
    /// own, so it answers what happened and leaves reading the person to the route that publishes reading one.
    /// </remarks>
    internal static async Task<Results<Ok<ContactWriteResponse>, ProblemHttpResult>> PromoteAsync(
        [FromRoute] Guid contactId,
        [FromServices] ContactBookWriter contacts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contacts);

        if (NamedContact(contactId) is not { } identity)
        {
            return NoContact();
        }

        return TypedResults.Ok(
            ContactWriteResponse.OutcomeOf(await contacts.PromoteAsync(identity, cancellationToken)));
    }

    /// <summary>Erases one person the acting user reads, and everything the book derived from them.</summary>
    /// <param name="contactId">The contact to erase.</param>
    /// <param name="contacts">Performs the erasure.</param>
    /// <param name="cancellationToken">Cancels the erasure when the client disconnects.</param>
    /// <returns><c>200</c> with what was removed, including books that held no such contact.</returns>
    /// <remarks>
    /// The data-subject erasure path, so it removes rather than marks and no origin gates it: somebody asking to be
    /// taken out of a contact book is not answered with which book they happen to be in. It therefore reaches a record
    /// one of this user's mailboxes collected as well, and erasing one of those takes it out for every user assigned
    /// that mailbox, because the record was one record rather than a copy each.
    /// </remarks>
    internal static async Task<Results<Ok<ContactErasureResponse>, ProblemHttpResult>> EraseAsync(
        [FromRoute] Guid contactId,
        [FromServices] ContactBookWriter contacts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contacts);

        if (NamedContact(contactId) is not { } identity)
        {
            return NoContact();
        }

        var erasure = await contacts.EraseAsync(identity, cancellationToken);

        return TypedResults.Ok(new ContactErasureResponse(
            erasure.ContactId.Value,
            erasure.WasHeld,
            erasure.AddressesErased));
    }

    /// <summary>Serves one bounded page of one half of the book.</summary>
    /// <remarks>
    /// A blank cursor is read as no cursor, because a client that sent an empty argument asked for the first page
    /// rather than presented a boundary this deployment did not issue.
    /// </remarks>
    private static async Task<Results<Ok<ContactPageResponse>, ProblemHttpResult>> ReadPageAsync(
        ContactOrigin origin,
        int? pageSize,
        string? cursor,
        ContactBookReader contacts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contacts);

        var request = new ContactPageRequest
        {
            Origin = origin,
            PageSize = pageSize,
            Cursor = string.IsNullOrWhiteSpace(cursor) ? null : cursor,
        };

        try
        {
            var page = await contacts.ReadPageAsync(request, cancellationToken);

            return TypedResults.Ok(new ContactPageResponse(
                [.. page.Contacts.Select(ContactResponse.For)],
                page.NextCursor?.Encode()));
        }
        catch (ContactQueryInvalidException refusal)
        {
            return Refuse(refusal.Message);
        }
        catch (ContactCursorMalformedException refusal)
        {
            return Refuse(refusal.Message);
        }
    }

    /// <summary>Reads the contact a route named, refusing the one value a UUID route constraint still admits.</summary>
    /// <remarks>
    /// A contact identifier is never empty, and the constraint on the route cannot say so: it accepts the all-zero UUID
    /// like any other. Keeping it out here is what stops a caller that composed one meeting a stated answer rather than
    /// an unhandled guard reported as a fault in the deployment.
    /// </remarks>
    private static ContactId? NamedContact(Guid contactId) =>
        contactId == Guid.Empty ? null : ContactId.Create(contactId);

    /// <summary>Reads the record a request states, as the text the caller wrote rather than as anything judged.</summary>
    /// <remarks>
    /// Every rule the record obeys is <see cref="ContactBookWriter" />'s, applied in the one place every writer reaches
    /// the book through, so nothing here checks a value — only that a body arrived at all.
    /// </remarks>
    private static ContactRecordDraft? DraftOf(ContactRecordRequest? request) => request is null
        ? null
        : new ContactRecordDraft
        {
            DisplayName = request.DisplayName,
            Addresses = request.Addresses,
            PreferredAddress = request.PreferredAddress,
            Note = request.Note,
        };

    /// <summary>States that the route named the one identifier no contact can carry.</summary>
    private static ProblemHttpResult NoContact() => Refuse("A contact identifier cannot be empty.");

    /// <summary>States that the request carried no record to write.</summary>
    private static ProblemHttpResult NoRecord() => Refuse("The request carries no contact record.");

    /// <summary>States what a caller has to change, without echoing anything about the person it was writing.</summary>
    private static ProblemHttpResult Refuse(string stated) =>
        TypedResults.Problem(stated, statusCode: StatusCodes.Status400BadRequest);
}
