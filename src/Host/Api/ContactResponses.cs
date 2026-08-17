// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.Domain.Contacts;

namespace MailFathom.Host.Api;

/// <summary>The record a caller states for a contact, whether it is being written for the first time or amended.</summary>
/// <param name="DisplayName">The name to record for the person.</param>
/// <param name="Addresses">Every address the person uses; two spellings of one address count once.</param>
/// <param name="PreferredAddress">The address to use by default, which must be one of <paramref name="Addresses" />.</param>
/// <param name="Note">What the owner wrote about the person, or nothing to hold no note.</param>
/// <remarks>
/// One request shape for both operations, because an amendment states the whole record rather than the difference from
/// the one held: what a caller sends to create a contact and what it sends to correct one are the same four things, and
/// a second record differing in nothing would be two contracts to keep in agreement. Which operation is meant is the
/// route and the verb, never a field.
/// </remarks>
internal sealed record ContactRecordRequest(
    string? DisplayName,
    IReadOnlyList<string>? Addresses,
    string? PreferredAddress,
    string? Note);

/// <summary>One person as the book holds them.</summary>
/// <param name="Id">The identity the book gave them, which no amendment and no promotion changes.</param>
/// <param name="DisplayName">The name the owner recorded, in the casing they wrote it.</param>
/// <param name="Addresses">Every address they use, the preferred one first and the rest in comparison order.</param>
/// <param name="PreferredAddress">The address to use when something addresses them without naming which of theirs.</param>
/// <param name="Note">What the owner wrote about them, or nothing where they wrote nothing.</param>
/// <param name="Origin">How the contact came to be in the book, which decides who may amend it.</param>
/// <param name="RecordedAt">When the contact entered the book.</param>
/// <param name="AmendedAt">When it was last amended, which equals <paramref name="RecordedAt" /> until one happens.</param>
/// <remarks>
/// Every field but the identity and the origin is personal data about a third party. It travels to the caller that
/// asked for this person and reaches nothing else — no log line, no metric dimension, and no failure message.
/// </remarks>
internal sealed record ContactResponse(
    Guid Id,
    string DisplayName,
    IReadOnlyList<string> Addresses,
    string PreferredAddress,
    string? Note,
    string Origin,
    DateTimeOffset RecordedAt,
    DateTimeOffset AmendedAt)
{
    /// <summary>Describes one contact for a caller.</summary>
    /// <param name="contact">The contact as the book holds it.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="contact" /> is <see langword="null" />.</exception>
    internal static ContactResponse For(Contact contact)
    {
        ArgumentNullException.ThrowIfNull(contact);

        return new ContactResponse(
            contact.Id.Value,
            contact.DisplayName.Value,
            [.. contact.Addresses.Select(address => address.Address)],
            contact.PreferredAddress.Address,
            contact.Note?.Value,
            contact.Origin.ToString(),
            contact.RecordedAt,
            contact.AmendedAt);
    }
}

/// <summary>The person a lookup found, or that the book holds none.</summary>
/// <param name="Contact">The contact, or nothing when the book holds none matching what was asked.</param>
/// <remarks>
/// A book holding nobody of that identity or address is an outcome rather than a refusal, so the answer carries no
/// contact instead of a <c>404</c>: the caller asked a question this deployment can answer, and the answer is that
/// nobody is recorded. It also keeps <c>404</c> meaning what every client already reads it as on this surface — that
/// the port serves no administrative endpoint.
/// </remarks>
internal sealed record ContactLookupResponse(ContactResponse? Contact);

/// <summary>One bounded page of the book, and where the walk continues.</summary>
/// <param name="Contacts">The contacts this page holds, ordered by the name's comparison form and then by identity.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or nothing at the end of the book.</param>
/// <remarks>
/// The absent cursor is the end of the walk rather than a page that happened to be short, so a caller stops when the
/// cursor stops instead of comparing the count against the size it asked for.
/// </remarks>
internal sealed record ContactPageResponse(IReadOnlyList<ContactResponse> Contacts, string? NextCursor);

/// <summary>What one write to the book produced.</summary>
/// <param name="Outcome">How the write ended, by the name the application's own outcome carries.</param>
/// <param name="Contact">The record as written, present exactly when the write was performed.</param>
/// <param name="AddressHolder">The contact already holding an address the write claimed, present exactly when that is what refused it.</param>
/// <remarks>
/// <para>
/// A refusal is answered with <c>200</c> and a named outcome rather than a status code, because each one is something
/// the caller reports to its owner and continues from rather than a request that was malformed. Only the holder's
/// identity is named: answering with somebody else's record would hand a third party out as a side effect of a refused
/// write.
/// </para>
/// <para>
/// A refusal carries no record for the same reason, including the two the book can only reach by reading one — a write
/// the contact's origin refuses, and a promotion of somebody already asserted. The routes that write are published
/// under <c>mailfathom.admin.operate</c> and reading the book is <c>mailfathom.admin.audit.read</c>, so echoing the
/// held record back would serve a read to a grant that does not admit it, and a refused write is where that would be
/// least visible. What the caller is told is what it has to act on: which outcome refused the write.
/// </para>
/// </remarks>
internal sealed record ContactWriteResponse(string Outcome, ContactResponse? Contact, Guid? AddressHolder)
{
    /// <summary>Describes what a write to the book produced.</summary>
    /// <param name="result">The outcome the book answered with.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="result" /> is <see langword="null" />.</exception>
    internal static ContactWriteResponse For(ContactWriteResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new ContactWriteResponse(
            result.Outcome.ToString(),
            result is { Outcome: ContactWriteOutcome.Written, Contact: { } written } ? ContactResponse.For(written) : null,
            result.AddressHolder?.Value);
    }
}

/// <summary>What erasing one contact removed.</summary>
/// <param name="Contact">The contact the erasure was asked for.</param>
/// <param name="WasHeld">Whether the book held that contact when the erasure ran.</param>
/// <param name="AddressesErased">How many addresses went with them.</param>
/// <remarks>
/// The counts are what an owner is entitled to rather than a courtesy, and they are the whole of what an erasure says
/// about a person: that they are gone, and how much went. No name, address, or note is in this answer, deliberately —
/// a report of an erasure that echoed the record would be a copy of what was just removed.
/// </remarks>
internal sealed record ContactErasureResponse(Guid Contact, bool WasHeld, int AddressesErased);

/// <summary>Everything the deployment holds about one person, as of the instant it was taken.</summary>
/// <param name="Contact">The complete record, or nothing when the book holds no such contact.</param>
/// <param name="ProducedAt">When the export was produced, absent together with the contact.</param>
/// <remarks>
/// The data-subject access path. It carries the same complete record every other surface over the book renders, because
/// which parts of a person are handed back is not a decision any surface gets to make.
/// </remarks>
internal sealed record ContactExportResponse(ContactResponse? Contact, DateTimeOffset? ProducedAt);
