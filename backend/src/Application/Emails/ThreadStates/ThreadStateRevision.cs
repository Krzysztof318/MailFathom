// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.ThreadStates;

/// <summary>Which shape of a conversation a state was derived from, so a later run can tell whether it still describes it.</summary>
/// <param name="MessageCount">How many of the conversation's messages the derivation was shown.</param>
/// <param name="LatestArrival">When the most recent of them arrived, or <see langword="null" /> where none of them recorded an arrival.</param>
/// <remarks>
/// <para>
/// The alternative to this pair is a state that goes stale silently, which is the thing the product refuses: a
/// conversation that gained a reply after its state was derived would keep showing what was true two messages ago and
/// nothing would say so. Comparing the pair against the conversation as it stands now is what puts the thread back into
/// the queue, and writing the new state is what takes it out again.
/// </para>
/// <para>
/// It is a count and an instant rather than a version column on the conversation, because a conversation has no row of
/// its own that changes when a message joins it — a thread is the relation its messages declare, so what changed is
/// visible in the messages and nowhere else. Both halves are needed: a count alone misses a message deleted and another
/// received, and an instant alone misses a message received out of order.
/// </para>
/// <para>
/// One membership change escapes it — a message removed and another carrying exactly the same arrival instant added
/// between two runs — and that is accepted rather than solved. The alternative is a digest over every message
/// identifier of every conversation on every pass, which costs a scan per run to close a case no mailbox produces.
/// </para>
/// </remarks>
public readonly record struct ThreadStateRevision(int MessageCount, DateTimeOffset? LatestArrival);
