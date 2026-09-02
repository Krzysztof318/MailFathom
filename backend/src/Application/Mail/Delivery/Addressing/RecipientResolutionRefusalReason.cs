// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Delivery.Addressing;

/// <summary>States what stopped a recipient an author named from becoming an address.</summary>
/// <remarks>
/// All three are about the book rather than about the message, which is what separates them from a composition's own
/// refusals: none of them is corrected by writing the message differently, and none of them can be resolved by anything
/// choosing on the author's behalf.
/// </remarks>
public enum RecipientResolutionRefusalReason
{
    /// <summary>The book holds no contact under the identity or the name the author used.</summary>
    /// <remarks>
    /// One answer for both, because the remedy is the same: name somebody the book holds, or write the address down. It
    /// is also what a name nobody carries produces, so a lookup that found nothing never reads as a lookup that found
    /// too many.
    /// </remarks>
    ContactUnknown = 0,

    /// <summary>Several contacts carry the name the author used, so the name addresses nobody.</summary>
    /// <remarks>
    /// The refusal carries how many matched and nothing else about them. Picking the closest, the most recently written
    /// down, or the one with the most addresses would each deliver the message to somebody nobody named.
    /// </remarks>
    ContactNameAmbiguous = 1,

    /// <summary>The address the author chose for the contact is not one that contact holds.</summary>
    /// <remarks>
    /// It covers text naming no mailbox at all, since nobody holds an address that is not one. Refusing rather than
    /// falling back to the preferred address is what keeps naming a contact from becoming a way to reach a mailbox
    /// alongside them.
    /// </remarks>
    ContactAddressNotHeld = 2,
}
