// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Contacts.Failures;
using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Contacts;

/// <summary>Writes to the contact book for a caller that was granted writing it.</summary>
/// <remarks>
/// <para>
/// The four acts a caller performs on the book — record a person, amend one, erase one, promote one this deployment
/// collected — each behind <see cref="MailFathomPermission.MailContactsWrite" />. The permission is asked for here
/// rather than only at the transport, so an entrypoint added later cannot change what this deployment holds about
/// somebody by arriving another way, and erasure is behind the same grant as the rest: a caller that may edit the book
/// may take somebody out of it, and no smaller grant reaches an act that cannot be undone.
/// </para>
/// <para>
/// Every write acts under <see cref="ContactOrigin.Asserted" />, because a caller granted this permission is writing for
/// the owner: what an agent is told to record is a person somebody wrote down. What follows from it is the one refusal a
/// caller meets that is about the record rather than about the request — amending a contact this deployment collected is
/// refused until it has been promoted. Promotion is the fourth act for exactly that reason: leaving it to the
/// administrative surface alone would put every collected record permanently out of this one's reach, so it is offered
/// here as well and under the same grant.
/// </para>
/// <para>
/// The rules a record obeys are applied here rather than at whichever boundary the caller arrived by, so the book cannot
/// be reached having checked fewer of them. Nothing here logs: a name, an address, and a note are personal data about a
/// third party, and so is the fact that a particular person was written down.
/// </para>
/// </remarks>
public sealed class ContactBookWriter
{
    /// <summary>The origin every write from a caller acts under.</summary>
    /// <remarks>Named once rather than repeated per act, because it is one decision about what this surface is.</remarks>
    private const ContactOrigin CallerWriter = ContactOrigin.Asserted;

    private readonly ContactBook book;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the use case over the book it writes to and the authorization it asks first.</summary>
    /// <param name="book">Performs the acts the book supports.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public ContactBookWriter(ContactBook book, AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(authorization);

        this.book = book;
        this.authorization = authorization;
    }

    /// <summary>Records a person the book does not yet hold.</summary>
    /// <param name="draft">The record to write.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The record as written, or the refusal naming what stopped it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="draft" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold the writing grant.</exception>
    /// <exception cref="ContactRecordInvalidException">Thrown when the record breaks a rule the book holds.</exception>
    public Task<ContactWriteResult> RecordAsync(ContactRecordDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        this.authorization.RequirePermission(MailFathomPermission.MailContactsWrite);

        var record = ReadRecord(draft);

        return this.book.RecordAsync(
            new NewContact
            {
                DisplayName = record.DisplayName,
                Addresses = record.Addresses,
                PreferredAddress = record.PreferredAddress,
                Note = record.Note,
                Origin = CallerWriter,
            },
            cancellationToken);
    }

    /// <summary>Amends one contact to the record the caller states.</summary>
    /// <param name="contactId">The contact to amend.</param>
    /// <param name="draft">The record the contact is to have afterwards.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The amended record, or the refusal naming what stopped it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="draft" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold the writing grant.</exception>
    /// <exception cref="ContactRecordInvalidException">Thrown when the record breaks a rule the book holds.</exception>
    public Task<ContactWriteResult> AmendAsync(
        ContactId contactId,
        ContactRecordDraft draft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        this.authorization.RequirePermission(MailFathomPermission.MailContactsWrite);

        var record = ReadRecord(draft);

        return this.book.AmendAsync(
            new ContactAmendment
            {
                ContactId = contactId,
                Writer = CallerWriter,
                DisplayName = record.DisplayName,
                Addresses = record.Addresses,
                PreferredAddress = record.PreferredAddress,
                Note = record.Note,
            },
            cancellationToken);
    }

    /// <summary>Takes on a contact this deployment collected, so it becomes one the owner asserted.</summary>
    /// <param name="contactId">The contact to promote.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The promoted record, or the refusal naming what stopped it.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold the writing grant.</exception>
    /// <remarks>
    /// The act that turns a record nobody wrote down into one somebody did, and the only crossing between the origins.
    /// It is what unlocks amending a collected contact: a caller refused an amendment is told the record was collected,
    /// promotes it, and then amends it like any other. It acts under <see cref="CallerWriter" /> for the same reason
    /// every other write here does — a caller granted this permission is writing for the owner — which is also what
    /// keeps collection from performing it on its own output.
    /// </remarks>
    public Task<ContactWriteResult> PromoteAsync(ContactId contactId, CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailContactsWrite);

        return this.book.PromoteAsync(contactId, CallerWriter, cancellationToken);
    }

    /// <summary>Erases one person and everything the book derived from them.</summary>
    /// <param name="contactId">The contact to erase.</param>
    /// <param name="cancellationToken">Cancels the erasure.</param>
    /// <returns>What the erasure removed, including a book that held no such contact.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold the writing grant.</exception>
    /// <remarks>
    /// The data-subject erasure path, so it removes rather than marks and no origin gates it: somebody asking to be
    /// taken out of a contact book is not answered with which half of the book they happen to be in. That is also why a
    /// caller may erase a contact it could not have amended.
    /// </remarks>
    public Task<ContactErasure> EraseAsync(ContactId contactId, CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailContactsWrite);

        return this.book.EraseAsync(contactId, cancellationToken);
    }

    /// <summary>Reads the record a draft states, refusing the one rule it broke.</summary>
    /// <remarks>
    /// Each rule is checked here rather than left to the domain's own guards, because what a caller has to be told is
    /// which rule it broke and a guard says that by naming a constructor parameter. What is left to the domain is the
    /// pair of rules only it can state — which characters carry no glyph — and each is translated into a refusal of its
    /// own rather than into the exception's text.
    /// </remarks>
    private static ContactRecord ReadRecord(ContactRecordDraft draft)
    {
        var displayName = ReadDisplayName(draft.DisplayName);

        if (draft.Addresses is not { Count: > 0 } supplied)
        {
            throw ContactRecordInvalidException.NoAddress();
        }

        var addresses = ReadAddresses(supplied);
        var preferredAddress = ReadAddress(draft.PreferredAddress);

        if (!addresses.Contains(preferredAddress))
        {
            throw ContactRecordInvalidException.PreferredAddressNotHeld();
        }

        return new ContactRecord(displayName, addresses, preferredAddress, ReadNote(draft.Note));
    }

    /// <summary>Reads the addresses a draft states, as the mailboxes they name rather than as the spellings it carried.</summary>
    /// <remarks>
    /// <para>
    /// One ceiling, applied twice over. It bounds the values a caller sent before any of them is trimmed or parsed, so
    /// the work one record costs is this system's to decide rather than the caller's, and it bounds the mailboxes those
    /// values named, which is what the book holds and what every surface over it publishes: two spellings of one address
    /// are the same address, so a record naming one of them twice is a record of one address rather than one that has
    /// spent two of its thirty-two. The administrative reader applies the same number to the same two things, so the two
    /// entrypoints over one book accept the same records.
    /// </para>
    /// <para>
    /// The order the caller wrote is kept, which decides which spelling of a repeated mailbox the record keeps. Nothing
    /// downstream reads the order otherwise: a preferred address is stated rather than inferred, and
    /// <see cref="Contact" /> puts it first and sorts the rest by comparison form.
    /// </para>
    /// </remarks>
    private static List<EmailAddress> ReadAddresses(IReadOnlyList<string> supplied)
    {
        if (supplied.Count > Contact.MaximumAddressCount)
        {
            throw ContactRecordInvalidException.TooManyAddresses();
        }

        List<EmailAddress> addresses = [];

        foreach (var value in supplied)
        {
            var address = ReadAddress(value);

            if (addresses.Contains(address))
            {
                continue;
            }

            addresses.Add(address);
        }

        return addresses;
    }

    /// <summary>Reads the name a draft states.</summary>
    private static ContactDisplayName ReadDisplayName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw ContactRecordInvalidException.NoDisplayName();
        }

        try
        {
            return ContactDisplayName.Create(value);
        }
        catch (ArgumentException cause)
        {
            throw ContactRecordInvalidException.NotADisplayName(cause);
        }
    }

    /// <summary>Reads one address a draft states.</summary>
    /// <remarks>
    /// The length is checked before the address is parsed, because the parse scans what it is handed and the caller
    /// decides how long that is. What is accepted is the addr-spec alone, so a caller that supplied a header's
    /// <c>Anna Kowalska &lt;anna@example.test&gt;</c> is refused rather than read leniently — the book records a
    /// person's own name rather than one correspondent's spelling of it.
    /// </remarks>
    private static EmailAddress ReadAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw ContactRecordInvalidException.NotAnAddress();
        }

        var trimmed = value.Trim();

        if (trimmed.Length > Contact.MaximumAddressLength
            || ContactAddressText.IsAngleAddress(trimmed)
            || !EmailAddress.TryCreate(displayName: null, trimmed, out var address))
        {
            throw ContactRecordInvalidException.NotAnAddress();
        }

        return address;
    }

    /// <summary>Reads the note a draft states, treating blank text as no note at all.</summary>
    private static ContactNote? ReadNote(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return ContactNote.Create(value);
        }
        catch (ArgumentException cause)
        {
            throw ContactRecordInvalidException.NotANote(cause);
        }
    }

    /// <summary>The validated parts of a record a draft stated.</summary>
    private sealed record ContactRecord(
        ContactDisplayName DisplayName,
        IReadOnlyCollection<EmailAddress> Addresses,
        EmailAddress PreferredAddress,
        ContactNote? Note);
}
