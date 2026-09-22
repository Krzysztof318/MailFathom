// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Emails;

/// <summary>The state a held message carries back onto the source server when custody returns to mirroring.</summary>
/// <param name="IsSeen">Whether the message is read, as MailFathom holds it.</param>
/// <param name="IsAnswered">Whether the message has been replied to, as MailFathom holds it.</param>
/// <param name="IsFlagged">Whether the message is starred, as MailFathom holds it.</param>
/// <param name="IsDraft">Whether the source presented the message as one being composed, as MailFathom holds it.</param>
/// <param name="Keywords">The keywords the message carries, as MailFathom holds them.</param>
/// <remarks>
/// <para>
/// It is a sibling of <see cref="Delivery.Filing.AppendedMailFlags" /> rather than a widening of it, because the two
/// append different things. That one carries what a copy of a message MailFathom composed can honestly assert; this
/// one carries a message somebody else sent, whose flags and keywords are the state the source last showed together
/// with whatever a person did to it while the mailbox was held.
/// </para>
/// <para>
/// Four of the five system flags, and the omission is <c>\Deleted</c>. The other four are observations MailFathom
/// records per message and would otherwise be lost on the way back, so leaving one out would put a message onto the
/// source asserting less than was taken off it. <c>\Deleted</c> is not an observation about the message but a
/// request that the folder stop holding it, and a restore carrying it would ask the source to expunge the copy it is
/// being handed. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0034-holding-a-mailbox-mailfathom-alone-keeps.md">ADR 0034</see>.
/// </para>
/// <para>
/// None of this reopens the closed mutation surface
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0007-remote-mailbox-mutation-boundary-and-write-session.md">ADR 0007</see>
/// defines: these travel on one <c>APPEND</c> stating what the message was, never as a <c>STORE</c> changing what it
/// is.
/// </para>
/// </remarks>
public sealed record RestoredEmailState(
    bool IsSeen,
    bool IsAnswered,
    bool IsFlagged,
    bool IsDraft,
    RemoteEmailKeywords Keywords);
