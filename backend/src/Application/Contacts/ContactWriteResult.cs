// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Contacts;

namespace MailFathom.Application.Contacts;

/// <summary>What one write to the contact book produced, and what the caller has to tell its owner.</summary>
/// <remarks>
/// Every outcome carries the contact it is about where there is one, so a refusal is not merely a name: a writer refused
/// by an origin is handed the record whose origin refused it, and can therefore say that promoting it is what unlocks
/// the write.
/// </remarks>
public sealed record ContactWriteResult
{
    private ContactWriteResult(ContactWriteOutcome outcome, Contact? contact, ContactId? addressHolder)
    {
        this.Outcome = outcome;
        this.Contact = contact;
        this.AddressHolder = addressHolder;
    }

    /// <summary>Gets how the write ended.</summary>
    public ContactWriteOutcome Outcome { get; }

    /// <summary>Gets the contact the outcome is about, or <see langword="null" /> when the book held none.</summary>
    /// <remarks>
    /// The record as written for <see cref="ContactWriteOutcome.Written" />, and the record as held for a refusal the
    /// book could only reach by reading one.
    /// </remarks>
    public Contact? Contact { get; }

    /// <summary>Gets one contact that already holds an address the write claimed, when that is what refused it.</summary>
    /// <remarks>
    /// <para>
    /// Only the identity, because a caller resolving the clash asks the book for that contact through the ordinary read
    /// path; answering with somebody else's record here would hand a person out as a side effect of a failed write.
    /// </para>
    /// <para>
    /// One holder rather than every one of them. A record may claim addresses two other people hold, and a caller that
    /// resolves the clash this names is then refused again by the next — which is the same conversation one exchange
    /// later rather than a different answer, and it is what keeps a refused write from listing several third parties at
    /// once. A caller that wants them all reads the book by address before it writes.
    /// </para>
    /// </remarks>
    public ContactId? AddressHolder { get; }

    /// <summary>Reports the book holding what the caller asked for.</summary>
    /// <param name="contact">The record as it now stands.</param>
    /// <returns>The written outcome.</returns>
    public static ContactWriteResult Written(Contact contact)
    {
        ArgumentNullException.ThrowIfNull(contact);

        return new ContactWriteResult(ContactWriteOutcome.Written, contact, addressHolder: null);
    }

    /// <summary>Reports that no contact of that identity is in the book.</summary>
    /// <returns>The not-found outcome.</returns>
    public static ContactWriteResult NotFound() =>
        new(ContactWriteOutcome.NotFound, contact: null, addressHolder: null);

    /// <summary>Reports an address that already belongs to a different contact.</summary>
    /// <param name="addressHolder">The contact that holds it.</param>
    /// <returns>The refusal, naming the holder.</returns>
    public static ContactWriteResult AddressHeldBy(ContactId addressHolder) =>
        new(ContactWriteOutcome.AddressHeldByAnotherContact, contact: null, addressHolder);

    /// <summary>Reports a write the contact's origin does not admit.</summary>
    /// <param name="contact">The record as held, whose origin refused the write.</param>
    /// <returns>The refusal.</returns>
    public static ContactWriteResult OriginRefusesWriter(Contact contact)
    {
        ArgumentNullException.ThrowIfNull(contact);

        return new ContactWriteResult(ContactWriteOutcome.OriginRefusesWriter, contact, addressHolder: null);
    }

    /// <summary>Reports a promotion asked of a contact that is already asserted.</summary>
    /// <param name="contact">The record as held.</param>
    /// <returns>The refusal.</returns>
    public static ContactWriteResult AlreadyAsserted(Contact contact)
    {
        ArgumentNullException.ThrowIfNull(contact);

        return new ContactWriteResult(ContactWriteOutcome.AlreadyAsserted, contact, addressHolder: null);
    }
}
