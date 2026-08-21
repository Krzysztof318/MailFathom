// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Domain.Contacts;

/// <summary>Holds one person the book knows: their name, every address they use, and what the owner recorded about them.</summary>
/// <remarks>
/// <para>
/// A contact is a person rather than an address, which is the whole reason the book exists rather than a list. One
/// person uses a work address, a personal one, and an old one they still receive on; a record keyed on the address could
/// not say those were the same person, so the record is the person and the addresses hang off them. Which address to use
/// by default is the owner's choice, kept as <see cref="PreferredAddress" />, never an ordering accident.
/// </para>
/// <para>
/// <b>Matching is decided here and nowhere else.</b> Two addresses name the same mailbox when
/// <see cref="EmailAddress.NormalizedAddress" /> is equal, which upper-cases the whole address rather than only its
/// domain. RFC 5321 makes the local part case-sensitive and almost no provider honours that, so a rule that split
/// <c>Anna@example.test</c> from <c>anna@example.test</c> would store one person twice for a distinction their mail
/// server does not make. The cost is stated rather than hidden: a server that genuinely distinguishes them is served one
/// contact where it has two mailboxes. Nothing else in the book compares addresses any other way, and the same value is
/// what a stored row is indexed by.
/// </para>
/// <para>
/// It is an entity rather than a value: <see cref="Id" /> is what makes two records the same person, and every method
/// here answers with a new instance carrying that same identity. Value equality would be the wrong question to be able
/// to ask of it — two people with one name and one address are still two contacts if the owner recorded them as such.
/// </para>
/// <para>
/// Everything on this record but <see cref="Id" /> and <see cref="Origin" /> is personal data about a third party. It is
/// never logged, never a metric dimension, and never written into a failure message; the identifier is what a failure
/// names.
/// </para>
/// </remarks>
public sealed class Contact
{
    /// <summary>How many addresses one person may be recorded as using.</summary>
    /// <remarks>
    /// Well above the several mailboxes a person actually has, and bounded because every address is a row a lookup, a
    /// page, and an erasure carry: without a ceiling one record could decide the cost of reading the book. Two spellings
    /// of one address count once, so the limit is on mailboxes rather than on how a caller wrote them.
    /// </remarks>
    public const int MaximumAddressCount = 32;

    /// <summary>The greatest length one address may carry.</summary>
    /// <remarks>
    /// The longest path SMTP admits: a local part of 64 octets, the at-sign, and a domain of 255. An address beyond it
    /// is refused rather than dropped, which is the opposite of what extraction does with one it met in a header —
    /// nobody chose that address, while this one an owner typed and is entitled to be told about.
    /// </remarks>
    public const int MaximumAddressLength = 320;

    private Contact(
        ContactId id,
        ContactDisplayName displayName,
        IReadOnlyList<EmailAddress> addresses,
        EmailAddress preferredAddress,
        ContactNote? note,
        ContactOrigin origin,
        DateTimeOffset recordedAt,
        DateTimeOffset amendedAt)
    {
        this.Id = id;
        this.DisplayName = displayName;
        this.Addresses = addresses;
        this.PreferredAddress = preferredAddress;
        this.Note = note;
        this.Origin = origin;
        this.RecordedAt = recordedAt;
        this.AmendedAt = amendedAt;
    }

    /// <summary>Gets what addresses this person, which no amendment and no promotion ever changes.</summary>
    public ContactId Id { get; }

    /// <summary>Gets the name the owner recorded for this person.</summary>
    public ContactDisplayName DisplayName { get; }

    /// <summary>Gets every address this person uses, the preferred one first and the rest in comparison order.</summary>
    /// <remarks>
    /// The order is derived rather than recorded, so two reads of one contact answer alike and an export of it is stable
    /// between one production and the next. It is never a preference ranking beyond the first entry, which
    /// <see cref="PreferredAddress" /> states in its own right.
    /// </remarks>
    public IReadOnlyList<EmailAddress> Addresses { get; }

    /// <summary>Gets the address to use when something addresses this person without naming which of theirs to use.</summary>
    public EmailAddress PreferredAddress { get; }

    /// <summary>Gets what the owner wrote about this person, or <see langword="null" /> when they wrote nothing.</summary>
    public ContactNote? Note { get; }

    /// <summary>Gets how this contact came to be in the book, which decides who may amend it.</summary>
    public ContactOrigin Origin { get; }

    /// <summary>Gets when this contact entered the book.</summary>
    public DateTimeOffset RecordedAt { get; }

    /// <summary>Gets when this contact was last amended, which equals <see cref="RecordedAt" /> until one happens.</summary>
    public DateTimeOffset AmendedAt { get; }

    /// <summary>Builds a contact from what an owner or collection supplied, enforcing every invariant the book rests on.</summary>
    /// <param name="id">The identity this contact keeps for as long as it is held.</param>
    /// <param name="displayName">The name to record.</param>
    /// <param name="addresses">Every address this person uses; two spellings of one address count once.</param>
    /// <param name="preferredAddress">The address to use by default, which must be one of <paramref name="addresses" />.</param>
    /// <param name="note">What the owner wrote about this person, or <see langword="null" />.</param>
    /// <param name="origin">How this contact came to be in the book.</param>
    /// <param name="recordedAt">When this contact entered the book.</param>
    /// <param name="amendedAt">When it was last amended, which is <paramref name="recordedAt" /> for a new contact.</param>
    /// <returns>A contact whose addresses are deduplicated and ordered, with the preferred one first.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="addresses" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when no address is supplied, when more than <see cref="MaximumAddressCount" /> remain after two spellings of one address are merged, or when <paramref name="preferredAddress" /> is not among them.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="origin" /> names no declared value.</exception>
    /// <remarks>
    /// Two spellings of one address inside one record are one address, so they are merged rather than refused: they name
    /// the same mailbox of the same person, and refusing would ask an owner to resolve a difference their mail server
    /// does not make. Two spellings across two contacts are a different question, which the store answers by refusing
    /// the second holder.
    /// </remarks>
    public static Contact Create(
        ContactId id,
        ContactDisplayName displayName,
        IReadOnlyCollection<EmailAddress> addresses,
        EmailAddress preferredAddress,
        ContactNote? note,
        ContactOrigin origin,
        DateTimeOffset recordedAt,
        DateTimeOffset amendedAt)
    {
        ArgumentNullException.ThrowIfNull(addresses);

        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin), "A contact origin must name a declared value.");
        }

        if (addresses.Count == 0)
        {
            throw new ArgumentException("A contact holds at least one address.", nameof(addresses));
        }

        var preferred = WithoutDisplayName(preferredAddress, nameof(preferredAddress));
        var held = addresses
            .Select(address => WithoutDisplayName(address, nameof(addresses)))
            .Distinct()
            .ToArray();

        if (held.Length > MaximumAddressCount)
        {
            throw new ArgumentException($"A contact cannot hold more than {MaximumAddressCount} addresses.", nameof(addresses));
        }

        if (!held.Contains(preferred))
        {
            throw new ArgumentException("The preferred address is one of the addresses the contact holds.", nameof(preferredAddress));
        }

        return new Contact(
            id,
            displayName,
            Ordered(held, preferred),
            preferred,
            note,
            origin,
            recordedAt,
            amendedAt);
    }

    /// <summary>Answers whether this person uses the given address.</summary>
    /// <param name="address">The address to look for.</param>
    /// <returns><see langword="true" /> when the contact holds an address naming the same mailbox.</returns>
    public bool Holds(EmailAddress address) => this.Addresses.Contains(WithoutDisplayName(address, nameof(address)));

    /// <summary>Answers whether a writer of the given origin may amend this contact.</summary>
    /// <param name="writer">The origin the writer acts under.</param>
    /// <returns><see langword="true" /> when the writer's origin is this contact's own.</returns>
    /// <remarks>
    /// The rule is symmetric and deliberately so. Collection may not touch what an owner wrote down, and an owner does
    /// not amend a collected contact either — they promote it first, which is the act that makes the record theirs.
    /// </remarks>
    public bool IsAmendableBy(ContactOrigin writer) => this.Origin == writer;

    /// <summary>Answers whether a writer of the given origin may promote this contact.</summary>
    /// <param name="writer">The origin the writer acts under.</param>
    /// <returns><see langword="true" /> when the writer is one acting for the owner.</returns>
    /// <remarks>
    /// Promotion is the act of taking a record on, so only a writer acting under <see cref="ContactOrigin.Asserted" />
    /// performs it. Collection reads its own mail and would otherwise be able to declare the person it just inferred one
    /// the owner had written down, which is the whole distinction the origin exists to keep.
    /// </remarks>
    public bool IsPromotableBy(ContactOrigin writer) => writer == ContactOrigin.Asserted;

    /// <summary>Produces this contact with the parts an amendment replaced, keeping its identity, origin, and arrival.</summary>
    /// <param name="displayName">The name to record instead.</param>
    /// <param name="addresses">Every address this person uses after the amendment.</param>
    /// <param name="preferredAddress">The address to use by default after the amendment.</param>
    /// <param name="note">What the owner wrote about this person, or <see langword="null" /> to hold none.</param>
    /// <param name="amendedAt">When the amendment happened.</param>
    /// <returns>The amended contact.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="addresses" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when no address is supplied or when <paramref name="preferredAddress" /> is not among them.</exception>
    /// <remarks>
    /// An amendment states the record the owner wants rather than the difference from the one held, which is what keeps
    /// removing an address, adding one, and choosing a different preferred address one operation instead of three that
    /// could each leave the record in a shape the invariants above refuse.
    /// </remarks>
    public Contact AmendedWith(
        ContactDisplayName displayName,
        IReadOnlyCollection<EmailAddress> addresses,
        EmailAddress preferredAddress,
        ContactNote? note,
        DateTimeOffset amendedAt) =>
        Create(
            this.Id,
            displayName,
            addresses,
            preferredAddress,
            note,
            this.Origin,
            this.RecordedAt,
            amendedAt);

    /// <summary>Produces this contact as one the owner has taken responsibility for.</summary>
    /// <param name="promotedAt">When the promotion happened.</param>
    /// <returns>The same contact, asserted.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the contact is already asserted.</exception>
    /// <remarks>
    /// The one transition between origins, and it runs one way. Promoting is an act somebody performs, so an asserted
    /// contact drifting back to collected is not a transition this type declines to make but one that has no meaning:
    /// nothing can unsay that somebody wrote a person down.
    /// </remarks>
    public Contact PromotedToAsserted(DateTimeOffset promotedAt)
    {
        if (this.Origin == ContactOrigin.Asserted)
        {
            throw new InvalidOperationException("An asserted contact cannot be promoted again.");
        }

        return new Contact(
            this.Id,
            this.DisplayName,
            this.Addresses,
            this.PreferredAddress,
            this.Note,
            ContactOrigin.Asserted,
            this.RecordedAt,
            promotedAt);
    }

    /// <summary>Keeps the addr-spec alone, because the person's name is the contact's rather than one sender's spelling of it.</summary>
    /// <remarks>
    /// An address arriving with the display name a message carried would let the book hold two names for one person, one
    /// of which nobody chose. The address itself already passed validation, so rebuilding it cannot fail. The caller
    /// names the parameter it is checking, so a caught exception reports a parameter the public method actually declares
    /// rather than this one's own.
    /// </remarks>
    private static EmailAddress WithoutDisplayName(EmailAddress address, string parameterName)
    {
        if (!EmailAddress.TryCreate(displayName: null, address.Address, out var bookAddress))
        {
            throw new ArgumentException("A contact address must be a usable address.", parameterName);
        }

        if (bookAddress.Address.Length > MaximumAddressLength)
        {
            throw new ArgumentException($"A contact address cannot be longer than {MaximumAddressLength} characters.", parameterName);
        }

        return bookAddress;
    }

    /// <summary>Orders the held addresses with the preferred one first and the rest by their comparison form.</summary>
    private static IReadOnlyList<EmailAddress> Ordered(IReadOnlyCollection<EmailAddress> held, EmailAddress preferred) =>
    [
        preferred,
        .. held
            .Where(address => address != preferred)
            .OrderBy(address => address.NormalizedAddress, StringComparer.Ordinal),
    ];
}
