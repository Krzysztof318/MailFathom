// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.SyntheticMail.Generation;

/// <summary>One exchange between the watched mailbox and one invented correspondent, oldest message first.</summary>
/// <param name="Correspondent">The invented person on the other side of it, who writes every message the mailbox did not.</param>
/// <param name="Messages">The turns, alternating and never fewer than two.</param>
/// <remarks>
/// <para>
/// The threading a flat corpus carries is written into headers and asserted by nothing: the generator draws a parent,
/// proposes a <c>Message-Id</c>, and the submission server is free to replace it, at which point every reply in the
/// batch references a message the mailbox does not hold. An exchange is the shape that survives that, because it is
/// delivered one turn at a time and each turn's parent is read back from the mailbox rather than proposed.
/// </para>
/// <para>
/// The ancestry the messages carry here is therefore provisional. It is what the seed decided, so a dry run can list
/// a whole exchange without a server; delivery rewrites it from the identifiers the mailbox actually assigned, which
/// is why <see cref="SyntheticEmail" /> is a record and the rewrite is a <c>with</c> expression rather than a mutable
/// field.
/// </para>
/// </remarks>
internal sealed record SyntheticConversation(
    SyntheticParticipant Correspondent,
    IReadOnlyList<SyntheticEmail> Messages)
{
    /// <summary>Reports which side wrote the message at one position.</summary>
    /// <param name="turn">The message's position in <see cref="Messages" />.</param>
    /// <returns>The side that wrote it.</returns>
    /// <remarks>An exchange opens with the correspondent, because the mailbox being written to is what a development mailbox is filled for.</remarks>
    internal static SyntheticThreadSide SideOf(int turn) =>
        turn % 2 == 0 ? SyntheticThreadSide.Correspondent : SyntheticThreadSide.Mailbox;
}
