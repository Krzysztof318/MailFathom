// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Contacts;

/// <summary>How a write to the contact book ended.</summary>
/// <remarks>
/// Each value but the first is a refusal a caller acts on and continues from, which is why the book answers with a
/// result rather than raising: an address already held by somebody else, a contact that is not there, and a write a
/// record's origin does not admit are all things a surface reports to its owner, not failures to unwind through.
/// </remarks>
public enum ContactWriteOutcome
{
    /// <summary>The book holds what the caller asked for.</summary>
    Written = 0,

    /// <summary>No contact of that identity is in the book.</summary>
    NotFound = 1,

    /// <summary>One of the addresses already belongs to a different contact, which the result names.</summary>
    AddressHeldByAnotherContact = 2,

    /// <summary>The contact's origin does not admit a writer of the origin that asked.</summary>
    OriginRefusesWriter = 3,

    /// <summary>The contact is already asserted, so there is no promotion left to perform.</summary>
    AlreadyAsserted = 4,
}
