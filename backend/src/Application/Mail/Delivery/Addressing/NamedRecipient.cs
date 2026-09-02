// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Contacts;
using MailFathom.Domain.Delivery;

namespace MailFathom.Application.Mail.Delivery.Addressing;

/// <summary>States how an author named one recipient: by an address, or by somebody the contact book holds.</summary>
/// <remarks>
/// <para>
/// This is the shape every author writes, and it is deliberately not the shape a message is composed from. An address is
/// what a submission server is offered, so a recipient named as a person becomes one before anything is composed —
/// naming a contact is a convenience of the authoring boundary rather than a second kind of recipient the rest of the
/// send has to understand.
/// </para>
/// <para>
/// A contact is named either by the identity the book gave it or by the name the owner recorded, and exactly one of the
/// two. The name is the whole name rather than part of one, and it addresses a message only where it belongs to one
/// person: a recipient chosen out of several by a ranking is a message delivered to somebody nobody named.
/// </para>
/// <para>
/// Everything a caller supplied is untrusted, and the address text is carried unparsed for the same reason
/// <see cref="Composition.AuthoredEmailRecipient" /> carries one: repairing an address before it is composed would mail
/// somebody nobody named.
/// </para>
/// </remarks>
public sealed record NamedRecipient
{
    private NamedRecipient(
        OutgoingRecipientRole role,
        string? address,
        string? displayName,
        ContactId? contact,
        ContactDisplayName? contactName,
        string? contactAddress)
    {
        this.Role = role;
        this.Address = address;
        this.DisplayName = displayName;
        this.Contact = contact;
        this.ContactName = contactName;
        this.ContactAddress = contactAddress;
    }

    /// <summary>Gets the header the author wants this person named in.</summary>
    public OutgoingRecipientRole Role { get; }

    /// <summary>Gets the addr-spec the author supplied, or <see langword="null" /> when they named a contact instead.</summary>
    public string? Address { get; }

    /// <summary>Gets the name to write beside a supplied address, or <see langword="null" /> to write the address alone.</summary>
    /// <remarks>
    /// It belongs to an address the author wrote down. A contact carries the name the owner recorded for that person, so
    /// resolution supplies it and nothing here overrides it.
    /// </remarks>
    public string? DisplayName { get; }

    /// <summary>Gets the contact named by identity, or <see langword="null" /> when the recipient was named another way.</summary>
    public ContactId? Contact { get; }

    /// <summary>Gets the contact named by the owner's own name for them, or <see langword="null" /> when the recipient was named another way.</summary>
    public ContactDisplayName? ContactName { get; }

    /// <summary>Gets which of the contact's addresses to use, or <see langword="null" /> to use the one they prefer.</summary>
    /// <remarks>
    /// It is present only where a contact is named, and it narrows rather than widens: an address the contact does not
    /// hold is refused rather than sent to, so naming a person cannot reach a mailbox that naming an address could not.
    /// </remarks>
    public string? ContactAddress { get; }

    /// <summary>Names a recipient by the address the author wrote.</summary>
    /// <param name="role">The header to name them in.</param>
    /// <param name="address">The addr-spec the author supplied, unparsed.</param>
    /// <param name="displayName">The name to write beside it, or <see langword="null" /> to write the address alone.</param>
    /// <returns>The recipient the author named.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="address" /> is blank, which names nobody at all.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="role" /> is not a declared role.</exception>
    public static NamedRecipient AtAddress(OutgoingRecipientRole role, string address, string? displayName = null)
    {
        RequireDeclared(role);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);

        return new NamedRecipient(role, address, displayName, contact: null, contactName: null, contactAddress: null);
    }

    /// <summary>Names a recipient by the identity the book gave them.</summary>
    /// <param name="role">The header to name them in.</param>
    /// <param name="contact">The contact to address.</param>
    /// <param name="contactAddress">Which of their addresses to use, or <see langword="null" /> for the one they prefer.</param>
    /// <returns>The recipient the author named.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="contactAddress" /> is supplied blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="role" /> is not a declared role.</exception>
    public static NamedRecipient ByContact(
        OutgoingRecipientRole role,
        ContactId contact,
        string? contactAddress = null)
    {
        RequireDeclared(role);
        RequireUsableContactAddress(contactAddress);

        return new NamedRecipient(
            role,
            address: null,
            displayName: null,
            contact,
            contactName: null,
            contactAddress);
    }

    /// <summary>Names a recipient by the name the owner recorded for them.</summary>
    /// <param name="role">The header to name them in.</param>
    /// <param name="contactName">The whole name the owner wrote down.</param>
    /// <param name="contactAddress">Which of their addresses to use, or <see langword="null" /> for the one they prefer.</param>
    /// <returns>The recipient the author named.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="contactAddress" /> is supplied blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="role" /> is not a declared role.</exception>
    public static NamedRecipient ByContactName(
        OutgoingRecipientRole role,
        ContactDisplayName contactName,
        string? contactAddress = null)
    {
        RequireDeclared(role);
        RequireUsableContactAddress(contactAddress);

        return new NamedRecipient(
            role,
            address: null,
            displayName: null,
            contact: null,
            contactName,
            contactAddress);
    }

    private static void RequireDeclared(OutgoingRecipientRole role)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(
                nameof(role),
                role,
                "An authored recipient is named in one of the declared headers.");
        }
    }

    /// <summary>Refuses a chosen address that carries nothing, which no contact can hold.</summary>
    /// <remarks>
    /// Whether the text names a mailbox this contact uses is the resolution's question, because the answer depends on the
    /// book. Blank text is not that question: it names nothing, and admitting it would silently become the preferred
    /// address the caller did not ask for.
    /// </remarks>
    private static void RequireUsableContactAddress(string? contactAddress)
    {
        if (contactAddress is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(contactAddress);
        }
    }
}
