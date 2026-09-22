// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Emails;

/// <summary>The flags and keywords a copied message hands to the copy made of it.</summary>
/// <param name="IsSeen">Whether the copied message is read.</param>
/// <param name="IsFlagged">Whether the copied message is starred.</param>
/// <param name="Keywords">The keywords the copied message carries.</param>
/// <remarks>
/// <para>
/// It is the set a copy carries rather than a general flag bag, and it differs from
/// <see cref="Delivery.Filing.AppendedMailFlags" /> because the two describe different acts: that one is the initial
/// state of a message MailFathom is creating out of what somebody wrote, and this one is what a message already in a
/// mailbox hands to a duplicate of itself. <c>\Draft</c> is absent for the same reason <c>\Seen</c> is present — the
/// copy's state is the copied message's state, and nothing about copying makes a message a draft.
/// </para>
/// <para>
/// <c>\Answered</c> and <c>\Deleted</c> are not carried. Both describe what became of one occurrence rather than what
/// the message is, and a copy is a message nobody has answered and nothing has marked for expunge.
/// </para>
/// </remarks>
public readonly record struct CopiedMailFlags(bool IsSeen, bool IsFlagged, RemoteEmailKeywords Keywords);
