// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Mail.Delivery.Addressing;

/// <summary>Turns the people an author named into the addresses a message is composed and offered to.</summary>
/// <remarks>
/// <para>
/// This is the single place a contact becomes a recipient, and it sits between authoring and composition on the way to
/// the outgoing record — which is what makes naming a person a convenience rather than a second route out of the
/// deployment. Everything the record creation point applies to a recipient applies to a resolved one: the address it
/// produces is an ordinary address from that moment on, indistinguishable from one an author wrote down, so whatever
/// bounds, policy, and ceilings hold for a literal address hold for this one without being restated here.
/// </para>
/// <para>
/// A lookup either finds exactly one contact or it does not. Nothing ranks candidates, nothing prefers the most recently
/// written down, and nothing falls back to a near match: a recipient chosen that way is a message delivered to somebody
/// nobody named, which is a worse outcome than the send being refused.
/// </para>
/// <para>
/// It reads the book and writes nothing. Addressing somebody is not a fact about them, so no contact is created,
/// amended, or promoted by being written to.
/// </para>
/// <para>
/// It asks for no permission of its own, and that is deliberate rather than an omission. Every use case above it is
/// reached under a principal — a caller holding a grant, or the process running work nobody requested — and only the
/// first of those can hold one at all, so a grant demanded here would refuse a rule addressing a contact rather than
/// authorize anything. Whether a caller may name people out of the book is therefore the boundary's question, asked
/// where the caller is known and beside the grant that lets it send at all.
/// </para>
/// </remarks>
/// <param name="contacts">Reads the book a named contact is resolved against.</param>
public sealed class NamedRecipientResolver(IContactDirectory contacts)
{
    /// <summary>Resolves every recipient one authored message names.</summary>
    /// <param name="recipients">The people the author named, in the order they named them.</param>
    /// <param name="cancellationToken">Cancels the reads of the book.</param>
    /// <returns>The recipients to compose with, or the refusal that stopped one of them.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="recipients" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when more recipients are named than an outgoing message may hold, which is a caller that assembled something it did not mean rather than an author to refuse.</exception>
    /// <remarks>
    /// <para>
    /// One recipient that resolves to nobody refuses the whole message. The order the author wrote is kept, because it is
    /// the order the composition writes its headers in, and an address the author supplied is carried through unparsed:
    /// parsing it is the composition's to do and to refuse.
    /// </para>
    /// <para>
    /// The identities named and the names are each read in groups of at most a page of the book, so a message costs one
    /// read per way its recipients were named, and a second of one such way only past two hundred distinct people in it.
    /// Both ways are named out of one recipient list, and that list is bounded below twice two hundred, so at most one of
    /// them ever reaches its second read: three for the longest list an outgoing record can hold, against one per
    /// recipient. What addressing costs therefore follows from how a message was addressed rather than from how many
    /// people it goes to. The count is
    /// bounded before the first lookup all the same, because the reads carry what the caller supplied: the bound is the
    /// greatest number of recipients an outgoing record can hold at all, so a longer list describes a send that could not
    /// be written down whatever the book answered — and the deployment's own, smaller recipient bound is still the
    /// composition's to apply, where it is refused as a bound rather than raised as a defect.
    /// </para>
    /// </remarks>
    public async Task<RecipientResolution> ResolveAsync(
        IReadOnlyList<NamedRecipient> recipients,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recipients);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            recipients.Count,
            OutgoingEmailRequest.MaximumRecipientCount,
            nameof(recipients));

        var namedContacts = recipients.Where(named => named.Address is null).ToArray();
        var contactsByIdentity = await this.ReadContactsNamedByIdentityAsync(namedContacts, cancellationToken);
        var matchesByName = await this.MatchContactsNamedByNameAsync(namedContacts, cancellationToken);

        var resolved = new List<AuthoredEmailRecipient>(recipients.Count);

        foreach (var named in recipients)
        {
            if (named.Address is { } authoredAddress)
            {
                resolved.Add(new AuthoredEmailRecipient(named.Role, authoredAddress, named.DisplayName));

                continue;
            }

            var match = MatchOf(named, contactsByIdentity, matchesByName);

            if (match.OnlyMatch is not { } contact)
            {
                return match.MatchCount == 0
                    ? RecipientResolution.Refused(RecipientResolutionRefusalReason.ContactUnknown)
                    : RecipientResolution.Refused(
                        RecipientResolutionRefusalReason.ContactNameAmbiguous,
                        match.MatchCount);
            }

            if (AddressOf(named, contact) is not { } address)
            {
                return RecipientResolution.Refused(RecipientResolutionRefusalReason.ContactAddressNotHeld);
            }

            // The name written beside the address is the one the owner recorded for this person, which is the whole point
            // of addressing them by it: a message to a contact reads as a message to somebody rather than to a mailbox.
            resolved.Add(new AuthoredEmailRecipient(
                named.Role,
                address.Address,
                contact.DisplayName.Value,
                contact.Id,
                AuthoredRecipientProvenance.ResolvedFromContactBook));
        }

        return RecipientResolution.Resolved(resolved);
    }

    /// <summary>States who one recipient names, whichever of the two ways it names them, out of what the book answered.</summary>
    /// <remarks>
    /// An identity resolves to one contact or to none and can never be ambiguous, so both ways of naming answer in the
    /// same shape and the caller acts on the count rather than on which way was used.
    /// </remarks>
    private static ContactMatch MatchOf(
        NamedRecipient named,
        IReadOnlyDictionary<ContactId, Contact> contactsByIdentity,
        IReadOnlyDictionary<ContactDisplayName, ContactMatch> matchesByName)
    {
        if (named.Contact is { } contactId)
        {
            return contactsByIdentity.TryGetValue(contactId, out var contact)
                ? ContactMatch.Unique(contact)
                : ContactMatch.None;
        }

        if (named.ContactName is not { } contactName)
        {
            // A recipient naming neither an address nor a contact cannot be built, so reaching here is a defect in this
            // type rather than anything an author wrote.
            throw new InvalidOperationException("An authored recipient names an address or a contact.");
        }

        return matchesByName.GetValueOrDefault(contactName, ContactMatch.None);
    }

    /// <summary>Reads every contact the act named by the identity the book gave it.</summary>
    /// <remarks>
    /// The identities are read in groups the book answers in one read, so what a message costs is counted by that bound
    /// rather than by its recipients: the most an outgoing record can hold takes two of them.
    /// </remarks>
    private async Task<IReadOnlyDictionary<ContactId, Contact>> ReadContactsNamedByIdentityAsync(
        IReadOnlyList<NamedRecipient> namedContacts,
        CancellationToken cancellationToken)
    {
        var identities = namedContacts
            .Where(named => named.Contact is not null)
            .Select(named => named.Contact!.Value)
            .Distinct()
            .ToArray();

        var held = new Dictionary<ContactId, Contact>();

        foreach (var group in identities.Chunk(ContactQuery.MaximumPageSize))
        {
            foreach (var (contactId, contact) in await contacts.FindAllAsync(group, cancellationToken))
            {
                held[contactId] = contact;
            }
        }

        return held;
    }

    /// <summary>Resolves every name the act named a contact by.</summary>
    /// <remarks>Grouped for the same reason the identities are, and answered by the same bound.</remarks>
    private async Task<IReadOnlyDictionary<ContactDisplayName, ContactMatch>> MatchContactsNamedByNameAsync(
        IReadOnlyList<NamedRecipient> namedContacts,
        CancellationToken cancellationToken)
    {
        var contactNames = namedContacts
            .Where(named => named.Contact is null && named.ContactName is not null)
            .Select(named => named.ContactName!.Value)
            .Distinct()
            .ToArray();

        var matches = new Dictionary<ContactDisplayName, ContactMatch>();

        foreach (var group in contactNames.Chunk(ContactQuery.MaximumPageSize))
        {
            foreach (var (contactName, match) in await contacts.MatchDisplayNamesAsync(group, cancellationToken))
            {
                matches[contactName] = match;
            }
        }

        return matches;
    }

    /// <summary>Decides which of the contact's addresses the message is offered to.</summary>
    /// <returns>The address to use, or <see langword="null" /> when the act chose one the contact does not hold.</returns>
    /// <remarks>
    /// The book's own spelling is what comes back rather than the caller's, so the record and the composed headers carry
    /// the value the owner wrote down. Text naming no mailbox at all resolves to nothing here for the same reason an
    /// address the contact does not hold does: nobody holds an address that is not one.
    /// </remarks>
    private static EmailAddress? AddressOf(NamedRecipient named, Contact contact)
    {
        if (named.ContactAddress is not { } chosenAddress)
        {
            return contact.PreferredAddress;
        }

        if (!EmailAddress.TryCreate(displayName: null, chosenAddress, out var chosen))
        {
            return null;
        }

        // A default instance is what a contact holding no such address answers with, since the addresses compare by their
        // normalized form and an absent one leaves the struct's default behind.
        var held = contact.Addresses.FirstOrDefault(candidate => candidate == chosen);

        return string.IsNullOrEmpty(held.Address) ? null : held;
    }
}
