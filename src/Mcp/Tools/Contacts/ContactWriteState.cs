// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Mcp.Tools.Contacts;

/// <summary>States how a write to the contact book ended, as the protocol spells it.</summary>
/// <remarks>
/// <para>
/// The transport carries its own enumeration rather than the application's, so the published wire values are decided
/// here. It publishes the outcomes this surface can actually produce and no others: promoting a collected contact is the
/// owner's own act through the administrative surface, so the outcome that reports a promotion nothing was left to do is
/// not a state a tool can answer with.
/// </para>
/// <para>
/// A refusal is a state rather than a failed call, because each one is something the caller acts on and continues from:
/// somebody else already holds the address, the book holds nobody of that identity, or the record was collected rather
/// than written down. None of the three says the request was malformed, which is what a failed call means here.
/// </para>
/// </remarks>
internal enum ContactWriteState
{
    /// <summary>The book holds what the caller asked for.</summary>
    Written = 0,

    /// <summary>No contact of that identity is in the book.</summary>
    NotFound = 1,

    /// <summary>One of the addresses already belongs to a different contact, which the result names.</summary>
    AddressHeldByAnotherContact = 2,

    /// <summary>The contact was collected from mail rather than written down, so a caller may not amend it in place.</summary>
    ContactWasCollected = 3,
}
