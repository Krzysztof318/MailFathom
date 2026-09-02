// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves the contact book: the five acts an owner performs on it, and the three ways it is read.</summary>
/// <remarks>
/// <para>
/// Every write here acts under <see cref="ContactOrigin.Asserted" />, because this surface is the owner's own: what
/// somebody types into <c>mfctl</c> is a person they wrote down. That is what makes amending a collected contact answer
/// <c>OriginRefusesWriter</c> rather than silently taking a record collection owns — promoting it is the act that makes
/// it the owner's, and it is a command of its own.
/// </para>
/// <para>
/// They are here rather than on the MCP surface because the book is the most concentrated personal data this system
/// holds, and what bounds administrative access is what should bound who may add to it, correct it, read it out, or
/// erase somebody from it. The MCP tools over the book are a separate surface with separate reasoning.
/// </para>
/// <para>
/// These routes postdate ADR 0012's table and are allocated under its rule, which separates reading what was derived
/// from mail, causing work, and destroying. <strong>Reading the book — the listing, both lookups, and the export — is
/// <c>mailfathom.admin.audit.read</c></strong>, because a collected contact is a person this deployment learned about
/// from somebody's correspondence rather than a report of its own state, and the export is what a data-subject request
/// is answered from. Writing one is <c>mailfathom.admin.operate</c>, and erasing somebody is
/// <c>mailfathom.admin.erase</c> beside the mail erasure, because both destroy what this deployment holds about a
/// person and an operator granting one is granting the other.
/// </para>
/// <para>
/// <strong>Nothing a contact carries reaches a refusal.</strong> A name, an address, and a note travel in a request and
/// in the answer to the caller that asked for that person, and nowhere else: every refusal below names the rule that was
/// broken rather than the value that broke it, so a malformed address is reported as an address that is not usable
/// instead of being echoed into a problem document, a log, or a caller's shell history.
/// </para>
/// <para>
/// A lookup, an export, and an erasure of somebody the book does not hold all answer <c>200</c>. The caller asked a
/// question this deployment can answer and the answer is that nobody is recorded, so <c>404</c> keeps meaning what every
/// client already reads it as here — that the port serves no administrative endpoint at all.
/// </para>
/// </remarks>
internal static class ContactEndpoints
{
    /// <summary>The route the book is listed and written to, relative to the administrative prefix.</summary>
    internal const string ContactsRoute = "/contacts";

    /// <summary>The route one contact is read, amended, and erased at, relative to the administrative prefix.</summary>
    internal const string ContactRoute = "/contacts/{contactId:guid}";

    /// <summary>The route the person behind an address is read from, relative to the administrative prefix.</summary>
    /// <remarks>
    /// A route of its own rather than a filter on the listing, because it answers with one person rather than a page:
    /// at most one contact can hold an address, which is the book's uniqueness rule rather than a property of a query.
    /// The segment is not a UUID, so nothing can confuse it with the route above.
    /// </remarks>
    internal const string ContactByAddressRoute = "/contacts/by-address";

    /// <summary>The route a collected contact is promoted at, relative to the administrative prefix.</summary>
    internal const string ContactPromotionRoute = "/contacts/{contactId:guid}/promotion";

    /// <summary>The route the whole collected half of the book is erased at, relative to the administrative prefix.</summary>
    /// <remarks>
    /// A literal segment where the single-contact route takes an identifier, which routing prefers over a parameter, so
    /// the two cannot be confused. It is also why the segment names the origin rather than an action: what the owner is
    /// disposing of is the half of the book they did not write.
    /// </remarks>
    internal const string CollectedContactsRoute = "/contacts/collected";

    /// <summary>The route everything held about one person is exported from, relative to the administrative prefix.</summary>
    internal const string ContactExportRoute = "/contacts/{contactId:guid}/export";

    /// <summary>The greatest request body the two write routes read before refusing it.</summary>
    /// <remarks>
    /// A record carries a name, up to <see cref="Contact.MaximumAddressCount" /> addresses, and a note, which is around
    /// fifteen kilobytes of text before JSON escaping widens it. Stated because the server's own default is measured in
    /// tens of megabytes, which for these routes would let an authenticated client make the process buffer a body three
    /// orders of magnitude larger than any contact could be.
    /// </remarks>
    internal const int MaxRecordRequestBytes = 64 * 1024;

    /// <summary>The origin every write from this surface acts under.</summary>
    /// <remarks>
    /// Named once rather than repeated per handler, because it is one decision about what this endpoint is: the owner's
    /// own surface, whose writes are people the owner wrote down.
    /// </remarks>
    private const ContactOrigin AdministrativeWriter = ContactOrigin.Asserted;

    /// <summary>Maps the contact routes into the administrative group, so they inherit its authorization.</summary>
    /// <param name="api">The administrative route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapContacts(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(ContactsRoute, ListAsync)
            .RequirePermission(MailFathomPermission.AdminAuditRead);

        // The attribute is reached for its metadata rather than as an MVC filter: it implements
        // IRequestSizeLimitMetadata, which the routing pipeline applies to the request body feature, so a body over the
        // bound is answered 413 before the handler is reached.
        api.MapPost(ContactsRoute, RecordAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRecordRequestBytes))
            .RequirePermission(MailFathomPermission.AdminOperate);

        api.MapGet(ContactByAddressRoute, FindByAddressAsync)
            .RequirePermission(MailFathomPermission.AdminAuditRead);

        api.MapGet(ContactRoute, FindAsync)
            .RequirePermission(MailFathomPermission.AdminAuditRead);

        api.MapPut(ContactRoute, AmendAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRecordRequestBytes))
            .RequirePermission(MailFathomPermission.AdminOperate);

        api.MapDelete(ContactRoute, EraseAsync)
            .RequirePermission(MailFathomPermission.AdminErase);

        api.MapDelete(CollectedContactsRoute, EraseCollectedAsync)
            .RequirePermission(MailFathomPermission.AdminErase);

        api.MapPost(ContactPromotionRoute, PromoteAsync)
            .RequirePermission(MailFathomPermission.AdminOperate);

        api.MapGet(ContactExportRoute, ExportAsync)
            .RequirePermission(MailFathomPermission.AdminAuditRead);
    }

    /// <summary>Serves one bounded page of the book, or reports what was wrong with the request.</summary>
    /// <param name="origin">The origin to narrow to, or <see langword="null" /> for the whole book.</param>
    /// <param name="pageSize">How many contacts the page may hold, or <see langword="null" /> for the default.</param>
    /// <param name="cursor">The cursor the previous page returned, or <see langword="null" /> for the first page.</param>
    /// <param name="book">Reads the page, for a caller the book's own grant admits.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the page, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>
    /// There is no unbounded reading of the book. A caller that names no page size is served the default rather than
    /// everything, and a caller that asks for more than the ceiling is refused rather than quietly served the ceiling —
    /// which is what stops a request from deciding how much of a person's correspondents leave the database at once.
    /// </remarks>
    internal static async Task<Results<Ok<ContactPageResponse>, ProblemHttpResult>> ListAsync(
        [FromQuery] string? origin,
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor,
        [FromServices] ContactBook book,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(book);

        if (!TryReadOrigin(origin, out var narrowedOrigin))
        {
            return Refused($"A contact origin is either {nameof(ContactOrigin.Asserted)} or {nameof(ContactOrigin.Collected)}.");
        }

        ContactCursor? decodedCursor = null;

        if (cursor is not null && !ContactCursor.TryDecode(cursor, out decodedCursor))
        {
            return Refused("The continuation cursor is not one this deployment issued.");
        }

        ContactQuery query;

        try
        {
            query = ContactQuery.Create(narrowedOrigin, search: null, pageSize, decodedCursor);
        }
        catch (ArgumentOutOfRangeException)
        {
            return Refused($"A contact page holds between 1 and {ContactQuery.MaximumPageSize} contacts.");
        }

        var page = await book.ReadPageAsync(query, cancellationToken);

        return TypedResults.Ok(new ContactPageResponse(
            [.. page.Contacts.Select(ContactResponse.For)],
            page.NextCursor?.Encode()));
    }

    /// <summary>Records a person the book does not yet hold.</summary>
    /// <param name="request">The record to write.</param>
    /// <param name="book">Performs the write.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with the outcome, or <c>400</c> naming which rule the record broke.</returns>
    internal static async Task<Results<Ok<ContactWriteResponse>, ProblemHttpResult>> RecordAsync(
        [FromBody] ContactRecordRequest? request,
        [FromServices] ContactBook book,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(book);

        var read = ReadRecord(request);

        if (read.Record is not { } record)
        {
            return Refused(read.Refusal!);
        }

        var written = await book.RecordAsync(
            new NewContact
            {
                DisplayName = record.DisplayName,
                Addresses = record.Addresses,
                PreferredAddress = record.PreferredAddress,
                Note = record.Note,
                Origin = AdministrativeWriter,
            },
            cancellationToken);

        return TypedResults.Ok(ContactWriteResponse.For(written));
    }

    /// <summary>Reads one contact by the identity the book gave it.</summary>
    /// <param name="contactId">The contact to read.</param>
    /// <param name="book">Answers what the book holds, for a caller the book's own grant admits.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the contact, or <c>200</c> with none where the book holds no such person.</returns>
    internal static async Task<Results<Ok<ContactLookupResponse>, ProblemHttpResult>> FindAsync(
        Guid contactId,
        [FromServices] ContactBook book,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(book);

        if (!TryReadContactId(contactId, out var identity))
        {
            return EmptyIdentity();
        }

        var held = await book.FindAsync(identity, cancellationToken);

        return TypedResults.Ok(new ContactLookupResponse(held is null ? null : ContactResponse.For(held)));
    }

    /// <summary>Reads the person who uses one address.</summary>
    /// <param name="address">The address to resolve.</param>
    /// <param name="book">Answers what the book holds, for a caller the book's own grant admits.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the contact, <c>200</c> with none where nobody holds it, or <c>400</c> where the address is not one.</returns>
    /// <remarks>
    /// The lookup is by the address's comparison form, so a caller need not know which casing the book happens to have
    /// recorded. The refusal names neither the address nor its length, because an address a caller mistyped is still
    /// somebody's address.
    /// </remarks>
    internal static async Task<Results<Ok<ContactLookupResponse>, ProblemHttpResult>> FindByAddressAsync(
        [FromQuery] string? address,
        [FromServices] ContactBook book,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(book);

        if (!TryReadAddress(address, out var resolved))
        {
            return Refused("The lookup names no usable address.");
        }

        var held = await book.FindByAddressAsync(resolved, cancellationToken);

        return TypedResults.Ok(new ContactLookupResponse(held is null ? null : ContactResponse.For(held)));
    }

    /// <summary>Amends one contact to the record the caller states.</summary>
    /// <param name="contactId">The contact to amend.</param>
    /// <param name="request">The record the contact is to have afterwards.</param>
    /// <param name="book">Performs the write.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with the outcome, or <c>400</c> naming which rule the record broke.</returns>
    /// <remarks>
    /// The whole record rather than the difference from the one held, which is what keeps adding an address, dropping
    /// one, choosing a different preferred address, and correcting a name one operation whose result the book's
    /// invariants are checked against.
    /// </remarks>
    internal static async Task<Results<Ok<ContactWriteResponse>, ProblemHttpResult>> AmendAsync(
        Guid contactId,
        [FromBody] ContactRecordRequest? request,
        [FromServices] ContactBook book,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(book);

        if (!TryReadContactId(contactId, out var identity))
        {
            return EmptyIdentity();
        }

        var read = ReadRecord(request);

        if (read.Record is not { } record)
        {
            return Refused(read.Refusal!);
        }

        var amended = await book.AmendAsync(
            new ContactAmendment
            {
                ContactId = identity,
                Writer = AdministrativeWriter,
                DisplayName = record.DisplayName,
                Addresses = record.Addresses,
                PreferredAddress = record.PreferredAddress,
                Note = record.Note,
            },
            cancellationToken);

        return TypedResults.Ok(ContactWriteResponse.For(amended));
    }

    /// <summary>Promotes a collected contact to one the owner has taken responsibility for.</summary>
    /// <param name="contactId">The contact to promote.</param>
    /// <param name="book">Performs the write.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with the outcome and no record, including for a contact that was already asserted.</returns>
    /// <remarks>
    /// The only route here whose caller states no record, and therefore the only one whose answer would be the book's
    /// own contents rather than the request's. It is published under <c>mailfathom.admin.operate</c> while reading the
    /// book is <c>mailfathom.admin.audit.read</c>, so it answers what happened and leaves reading the person to the
    /// route that publishes reading one.
    /// </remarks>
    internal static async Task<Results<Ok<ContactWriteResponse>, ProblemHttpResult>> PromoteAsync(
        Guid contactId,
        [FromServices] ContactBook book,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(book);

        if (!TryReadContactId(contactId, out var identity))
        {
            return EmptyIdentity();
        }

        var promoted = await book.PromoteAsync(identity, AdministrativeWriter, cancellationToken);

        return TypedResults.Ok(ContactWriteResponse.OutcomeOf(promoted));
    }

    /// <summary>Erases one person and everything the book derived from them.</summary>
    /// <param name="contactId">The contact to erase.</param>
    /// <param name="book">Performs the erasure.</param>
    /// <param name="cancellationToken">Cancels the erasure when the client disconnects.</param>
    /// <returns><c>200</c> with what was removed, including a book that held no such contact.</returns>
    /// <remarks>
    /// The data-subject erasure path, so it removes rather than marks and no origin gates it: somebody asking to be
    /// taken out of a contact book is not answered with which half of the book they happen to be in.
    /// </remarks>
    internal static async Task<Results<Ok<ContactErasureResponse>, ProblemHttpResult>> EraseAsync(
        Guid contactId,
        [FromServices] ContactBook book,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(book);

        if (!TryReadContactId(contactId, out var identity))
        {
            return EmptyIdentity();
        }

        var erasure = await book.EraseAsync(identity, cancellationToken);

        return TypedResults.Ok(new ContactErasureResponse(
            erasure.ContactId.Value,
            erasure.WasHeld,
            erasure.AddressesErased));
    }

    /// <summary>Erases every contact this deployment collected, leaving the ones the owner asserted where they are.</summary>
    /// <param name="book">Performs the erasure.</param>
    /// <param name="cancellationToken">Cancels the erasure when the client disconnects.</param>
    /// <returns><c>200</c> with what was removed, including a book that had collected nobody.</returns>
    /// <remarks>
    /// The answer to an owner who changed their mind about collection. Everything collection produced is a contact of
    /// its own origin, so taking that origin out is taking out the whole of what it built and nothing of what the owner
    /// entered. It is behind the erasing grant rather than the operating one, because what it removes cannot be written
    /// back: switching collection on again rebuilds the book from mail that arrives afterwards rather than restoring
    /// what went.
    /// </remarks>
    internal static async Task<Results<Ok<CollectedContactErasureResponse>, ProblemHttpResult>> EraseCollectedAsync(
        [FromServices] ContactBook book,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(book);

        var erasure = await book.EraseCollectedAsync(cancellationToken);

        return TypedResults.Ok(new CollectedContactErasureResponse(
            erasure.ContactsErased,
            erasure.AddressesErased));
    }

    /// <summary>Produces everything the book holds about one person.</summary>
    /// <param name="contactId">The contact to export.</param>
    /// <param name="book">Produces the export.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the export, or <c>200</c> with none where the book holds no such person.</returns>
    internal static async Task<Results<Ok<ContactExportResponse>, ProblemHttpResult>> ExportAsync(
        Guid contactId,
        [FromServices] ContactBook book,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(book);

        if (!TryReadContactId(contactId, out var identity))
        {
            return EmptyIdentity();
        }

        var export = await book.ExportAsync(identity, cancellationToken);

        return TypedResults.Ok(export is null
            ? new ContactExportResponse(Contact: null, ProducedAt: null)
            : new ContactExportResponse(ContactResponse.For(export.Contact), export.ProducedAt));
    }

    /// <summary>Reads the identity a route named, refusing the one value a UUID route constraint still admits.</summary>
    /// <remarks>
    /// A contact identifier is never empty, and the constraint on the route cannot say so: it accepts the all-zero UUID
    /// like any other. Refusing it here is what keeps a caller that wrote one out by hand meeting a stated refusal
    /// rather than an unhandled guard reported as a fault in the deployment.
    /// </remarks>
    private static bool TryReadContactId(Guid contactId, out ContactId identity)
    {
        identity = default;

        if (contactId == Guid.Empty)
        {
            return false;
        }

        identity = ContactId.Create(contactId);

        return true;
    }

    /// <summary>States that the route named the one identifier no contact can carry.</summary>
    private static ProblemHttpResult EmptyIdentity() => Refused("A contact identifier cannot be empty.");

    /// <summary>Reads the origin a listing was narrowed to, refusing a value naming no origin.</summary>
    /// <remarks>
    /// An absent filter is the whole book rather than a refusal, and a blank one is read as absent: a caller writing
    /// <c>?origin=</c> asked for no narrowing rather than for an origin whose name is empty.
    /// </remarks>
    private static bool TryReadOrigin(string? origin, out ContactOrigin? narrowedOrigin)
    {
        narrowedOrigin = null;

        if (string.IsNullOrWhiteSpace(origin))
        {
            return true;
        }

        if (!Enum.TryParse<ContactOrigin>(origin.Trim(), ignoreCase: true, out var named) || !Enum.IsDefined(named))
        {
            return false;
        }

        narrowedOrigin = named;

        return true;
    }

    /// <summary>Reads one address a caller supplied, keeping the addr-spec alone.</summary>
    private static bool TryReadAddress(string? address, out EmailAddress resolved)
    {
        resolved = default;

        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        var trimmed = address.Trim();

        return trimmed.Length <= Contact.MaximumAddressLength
            && !ContactAddressText.IsAngleAddress(trimmed)
            && EmailAddress.TryCreate(displayName: null, trimmed, out resolved);
    }

    /// <summary>Reads the record a request states, or names the one rule it broke.</summary>
    /// <remarks>
    /// Every rule is checked here rather than left to the domain's own guards, because what a caller has to be told is
    /// which rule it broke and the guards say that by naming a parameter of a constructor. What is left to the domain is
    /// the pair of rules only it can state — which characters carry no glyph — and each is translated into a sentence of
    /// its own rather than into the exception's text.
    /// </remarks>
    private static ContactRecordReading ReadRecord(ContactRecordRequest? request)
    {
        if (request is null)
        {
            return ContactRecordReading.Refuse("The request carries no contact record.");
        }

        if (!TryReadDisplayName(request.DisplayName, out var displayName, out var nameRefusal))
        {
            return ContactRecordReading.Refuse(nameRefusal);
        }

        if (request.Addresses is not { Count: > 0 } supplied)
        {
            return ContactRecordReading.Refuse("A contact holds at least one address.");
        }

        if (supplied.Count > Contact.MaximumAddressCount)
        {
            return ContactRecordReading.Refuse(
                $"A contact cannot hold more than {Contact.MaximumAddressCount} addresses.");
        }

        var addresses = new List<EmailAddress>(supplied.Count);

        foreach (var candidate in supplied)
        {
            if (!TryReadAddress(candidate, out var address))
            {
                return ContactRecordReading.Refuse(
                    $"The record carries an address that is not a usable address of at most {Contact.MaximumAddressLength} characters.");
            }

            addresses.Add(address);
        }

        if (!TryReadAddress(request.PreferredAddress, out var preferred))
        {
            return ContactRecordReading.Refuse("The record names no usable preferred address.");
        }

        if (!addresses.Contains(preferred))
        {
            return ContactRecordReading.Refuse("The preferred address is one of the addresses the contact holds.");
        }

        if (!TryReadNote(request.Note, out var note, out var noteRefusal))
        {
            return ContactRecordReading.Refuse(noteRefusal);
        }

        return ContactRecordReading.Read(new ContactRecord(displayName, addresses, preferred, note));
    }

    /// <summary>Reads the name a request stated, or names the rule it broke.</summary>
    private static bool TryReadDisplayName(
        string? value,
        out ContactDisplayName displayName,
        out string refusal)
    {
        displayName = default;
        refusal = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return Broken("A contact record names no display name.", out refusal);
        }

        if (value.Trim().Length > ContactDisplayName.MaximumLength)
        {
            return Broken(
                $"A contact name cannot be longer than {ContactDisplayName.MaximumLength} characters.",
                out refusal);
        }

        try
        {
            displayName = ContactDisplayName.Create(value);
        }
        catch (ArgumentException)
        {
            return Broken("A contact name cannot contain characters that carry no glyph of their own.", out refusal);
        }

        return true;
    }

    /// <summary>Reads the note a request stated, or names the rule it broke.</summary>
    /// <remarks>
    /// An absent note and a blank one are both the absence of a note, because a contact without one holds none: two ways
    /// to say the same thing would leave every reader deciding which it was looking at, and a caller clearing a note
    /// sends the field empty rather than reaching for a second verb.
    /// </remarks>
    private static bool TryReadNote(string? value, out ContactNote? note, out string refusal)
    {
        note = null;
        refusal = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (value.Trim().Length > ContactNote.MaximumLength)
        {
            return Broken($"A contact note cannot be longer than {ContactNote.MaximumLength} characters.", out refusal);
        }

        try
        {
            note = ContactNote.Create(value);
        }
        catch (ArgumentException)
        {
            return Broken(
                "A contact note cannot contain characters that carry no glyph of their own, other than line breaks and tabs.",
                out refusal);
        }

        return true;
    }

    /// <summary>States the rule a value broke, and reports it as broken.</summary>
    private static bool Broken(string stated, out string refusal)
    {
        refusal = stated;

        return false;
    }

    /// <summary>States what a caller has to change, without echoing anything about the person it was writing.</summary>
    private static ProblemHttpResult Refused(string stated) =>
        TypedResults.Problem(stated, statusCode: StatusCodes.Status400BadRequest);

    /// <summary>The validated parts of a contact record a request stated.</summary>
    private sealed record ContactRecord(
        ContactDisplayName DisplayName,
        IReadOnlyCollection<EmailAddress> Addresses,
        EmailAddress PreferredAddress,
        ContactNote? Note);

    /// <summary>What reading a request's record produced: the record, or the one rule it broke.</summary>
    private sealed record ContactRecordReading(ContactRecord? Record, string? Refusal)
    {
        /// <summary>Reports a record every rule admits.</summary>
        internal static ContactRecordReading Read(ContactRecord record) => new(record, Refusal: null);

        /// <summary>Reports the rule the request broke.</summary>
        internal static ContactRecordReading Refuse(string refusal) => new(Record: null, refusal);
    }
}
