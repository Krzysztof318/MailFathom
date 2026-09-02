// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Contacts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;

namespace MailFathom.Infrastructure.Persistence.Delivery;

/// <summary>Restores one person a stored send, draft, or declaration is addressed to.</summary>
/// <remarks>
/// An address that no longer parses fails the read rather than being dropped. A message offered to fewer people than it
/// was written for is somebody who never receives it and is told nothing about it, which is a worse answer than a
/// record that refuses to be read. An empty contact identifier is read as no contact instead: that value records how
/// the address came to be on the message and nothing addresses anybody by it.
/// </remarks>
internal static class StoredOutgoingRecipient
{
    /// <summary>Restores the recipient one stored row names.</summary>
    /// <param name="carrier">What the row belongs to, as the refusal names it — an outgoing email record, a draft, or a recurring send.</param>
    /// <param name="carrierId">The identity of that record, which is what an operator reaches the row by.</param>
    /// <param name="ordinal">The position the row holds in the message's headers.</param>
    /// <param name="address">The stored address.</param>
    /// <param name="role">The header the composed message names them in.</param>
    /// <param name="contactId">The contact the address was resolved from, where the row names one.</param>
    /// <returns>The recipient that row states.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the stored address names no mailbox.</exception>
    internal static OutgoingRecipient ToRecipient(
        string carrier,
        Guid carrierId,
        int ordinal,
        string address,
        OutgoingRecipientRole role,
        Guid? contactId)
    {
        if (!EmailAddress.TryCreate(displayName: null, address, out var recipientAddress))
        {
            // The address itself stays out of the message: it is personal data, and the ordinal names the row exactly.
            throw new InvalidOperationException(
                $"{carrier} {carrierId} carries a recipient at position {ordinal} whose address names no mailbox.");
        }

        return OutgoingRecipient.Create(
            recipientAddress,
            role,
            contactId is { } resolvedContact && resolvedContact != Guid.Empty
                ? ContactId.Create(resolvedContact)
                : null);
    }
}
