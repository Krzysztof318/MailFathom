// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Contacts;

namespace MailFathom.Application.Contacts;

/// <summary>States what erasing one contact removed.</summary>
/// <param name="ContactId">The contact the erasure was asked for.</param>
/// <param name="WasHeld">Whether the book held that contact when the erasure ran.</param>
/// <param name="AddressesErased">How many addresses went with it.</param>
/// <remarks>
/// The counts are the point rather than a courtesy. Erasure is a data-subject obligation, so an owner asking for one is
/// entitled to an answer saying what was removed instead of a call that returned without complaint — and a count is what
/// lets a test prove that everything derived from the contact went with it rather than that the call did not throw.
/// It carries no name, address, or note: what an erasure reports about a person is that they are gone.
/// </remarks>
public sealed record ContactErasure(ContactId ContactId, bool WasHeld, int AddressesErased);
