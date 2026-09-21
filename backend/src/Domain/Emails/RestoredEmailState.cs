// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Emails;

/// <summary>The state a held message carries back onto the source server when custody returns to mirroring.</summary>
/// <param name="IsSeen">Whether the message is read, as MailFathom holds it.</param>
/// <param name="IsFlagged">Whether the message is starred, as MailFathom holds it.</param>
/// <param name="Keywords">The keywords the message carries, as MailFathom holds them.</param>
/// <remarks>
/// <para>
/// It is a sibling of <see cref="Delivery.Filing.AppendedMailFlags" /> rather than a widening of it, because the two
/// append different things. That one carries the two flags a copy of a message MailFathom composed can honestly
/// assert; this one carries a message somebody else sent, whose flags and keywords are the stored state a person left
/// on it while the mailbox was held — so <c>\Draft</c> is not expressible here and <c>\Flagged</c> and the keywords
/// are.
/// </para>
/// <para>
/// The set stays closed for the reason
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0007-remote-mailbox-mutation-boundary-and-write-session.md">ADR 0007</see>
/// keeps the mutations closed: <c>\Answered</c> and <c>\Deleted</c> are flags MailFathom never writes, and a restore
/// that carried either would be asserting something about the message that MailFathom never observed. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0034-holding-a-mailbox-mailfathom-alone-keeps.md">ADR 0034</see>.
/// </para>
/// </remarks>
public sealed record RestoredEmailState(bool IsSeen, bool IsFlagged, RemoteEmailKeywords Keywords);
