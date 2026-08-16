// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Delivery;

/// <summary>States what one recipient of an outgoing message has durably settled at.</summary>
/// <remarks>
/// <para>
/// The set answers one question and only that one: is this recipient offered on the next attempt. A recipient the
/// message reached must never be offered again, because a second offer accepted is a second copy in their mailbox; a
/// recipient permanently refused must never be offered again either, because the answer will not change. Everything
/// else is offered.
/// </para>
/// <para>
/// There is deliberately no member for a temporary rejection. A recipient a server deferred is one the next attempt
/// offers, which is what <see cref="Pending" /> already means, and the reply that deferred them is recorded beside the
/// status rather than encoded in it. A fourth member saying the same thing would let the same recipient be described
/// two ways, and the one that decides what happens next is this one.
/// </para>
/// <para>
/// The status is stored as its name for the reason the stage is: it stays readable in an ad-hoc query and survives any
/// later reordering of this enum.
/// </para>
/// </remarks>
public enum OutgoingRecipientStatus
{
    /// <summary>Nothing has settled this recipient, so the next attempt offers them.</summary>
    /// <remarks>
    /// It covers a recipient never offered and one a server temporarily rejected, which are the same thing to the
    /// attempt that follows. Which of the two it is reads from the recorded reply beside it.
    /// </remarks>
    Pending = 0,

    /// <summary>The message was transmitted with this recipient accepted, so nothing offers them again.</summary>
    /// <remarks>
    /// It is written when an acknowledged transmission covered the recipient rather than when the server accepted the
    /// address, because those are different facts: an envelope accepted by a session that then failed delivered
    /// nothing, and treating it as delivery would silently drop a recipient from every later attempt.
    /// </remarks>
    Accepted = 1,

    /// <summary>The server permanently refused this recipient, so nothing offers them again and nothing reaches them.</summary>
    Refused = 2,
}
