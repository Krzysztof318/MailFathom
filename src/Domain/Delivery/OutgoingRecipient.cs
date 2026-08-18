// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;

namespace MailFathom.Domain.Delivery;

/// <summary>Names one person an outgoing email is addressed to, and the header they are named in.</summary>
/// <remarks>
/// <para>
/// The display name a composed message writes is deliberately absent. What the envelope needs is the address, what an
/// attempt compares is the normalized form of it, and what an outgoing record is obliged to hold is the smallest thing
/// that answers both — so the name a sender chose to write stays in the stored MIME and nowhere else.
/// </para>
/// <para>
/// A recipient address is personal data of somebody who is not this mailbox's owner. It is on the record because a send
/// cannot be resumed without it, and it reaches no log line, metric dimension, span attribute, or exception message.
/// </para>
/// <para>
/// The address is here even when a contact was what the author named, and the contact is beside it rather than instead
/// of it. A record naming only the person would answer the wrong question a year later: a message sent to somebody whose
/// address changed afterwards was sent to the address they had, and an attempt resuming from the record has to offer
/// that address rather than whichever one the book holds now.
/// </para>
/// </remarks>
public readonly record struct OutgoingRecipient
{
    /// <summary>The greatest length an address may have, which bounds the column it is stored in.</summary>
    /// <remarks>
    /// It is the addr-spec RFC 5321 permits — 64 octets of local part, an at-sign, and 255 of domain. A longer one is
    /// refused where the request is built rather than dropped from it, which is the opposite of what a received
    /// message's participants get: dropping an address there costs a filter one participant, and dropping one here
    /// would be a person the owner wrote to who never receives the message and is told nothing about it.
    /// </remarks>
    public const int MaximumAddressLength = 320;

    private OutgoingRecipient(EmailAddress address, OutgoingRecipientRole role, ContactId? contact)
    {
        this.Address = address;
        this.Role = role;
        this.Contact = contact;
    }

    /// <summary>Gets the address the message is offered to.</summary>
    public EmailAddress Address { get; }

    /// <summary>Gets the header the composed message names this recipient in.</summary>
    public OutgoingRecipientRole Role { get; }

    /// <summary>Gets the contact the address was resolved from, or <see langword="null" /> when the author supplied the address itself.</summary>
    /// <remarks>
    /// It is the identity rather than anything about the person, which is the one part of a contact that is not personal
    /// data and therefore the only part a record of a send may keep beside the address. Absence is the ordinary case and
    /// says the author wrote an address down; presence says the book was asked, and it survives the contact being
    /// amended, promoted, or erased because nothing reads it to address anybody again.
    /// </remarks>
    public ContactId? Contact { get; }

    /// <summary>Names one recipient of an outgoing email.</summary>
    /// <param name="address">The address the message is offered to.</param>
    /// <param name="role">The header the composed message names them in.</param>
    /// <param name="contact">The contact the address was resolved from, or <see langword="null" /> when the author supplied it.</param>
    /// <returns>The recipient those name.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="address" /> names no mailbox or is longer than <see cref="MaximumAddressLength" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="role" /> is not a declared role.</exception>
    public static OutgoingRecipient Create(
        EmailAddress address,
        OutgoingRecipientRole role,
        ContactId? contact = null)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(
                nameof(role),
                role,
                "An outgoing recipient is named in one of the declared headers.");
        }

        // A default instance carries no address at all, which a record could be written with and never offered to
        // anybody. It is refused here rather than at the insert, where the failure would name a column.
        if (string.IsNullOrEmpty(address.Address))
        {
            throw new ArgumentException("An outgoing recipient names a mailbox.", nameof(address));
        }

        if (address.Address.Length > MaximumAddressLength)
        {
            // The address stays out of the message: it is personal data, and the caller holds the value they passed.
            throw new ArgumentException(
                $"An outgoing recipient's address may be at most {MaximumAddressLength} characters long.",
                nameof(address));
        }

        return new OutgoingRecipient(address, role, contact);
    }

    /// <summary>Describes the recipient by the header they are named in, and never by their address.</summary>
    /// <returns>The role alone.</returns>
    /// <remarks>
    /// The override exists to suppress the one a record struct would synthesize, which prints every property and would
    /// therefore put the address into any interpolated string, log template, or exception message that mentions a
    /// recipient. That is the invariant this type's remarks state, and nothing but an override enforces it.
    /// </remarks>
    public override string ToString() => $"{this.Role} recipient";
}
