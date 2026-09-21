// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;

namespace MailFathom.AI.Orchestration;

/// <summary>The one sentence every agent that has to resolve a relative period states its anchor with.</summary>
/// <remarks>
/// <para>
/// One wording rather than one per operation, because <em>Thursday</em>, <em>last quarter</em> and <em>tomorrow at
/// nine</em> are the same judgement about the same kind of words wherever they are read, and two wordings are two sets
/// of rules that come to differ in whichever one nobody is currently looking at. What is shared is the format and the
/// source; the insertion is not. An operation that wants an anchor states one on its own turn, and an operation that
/// has nothing to do with time — body cleanup, image description, passage relevance — receives none at all, because an
/// anchor in its prompt is tokens, noise, and a reading that stops being the same from one day to the next.
/// </para>
/// <para>
/// It is stated on the <em>turn</em> and never in the instruction, for two reasons that hold separately. An instruction
/// is composed once per process, so an agent carrying a date in its own would resolve <em>today</em> to whenever it
/// started until the next restart; and the instruction is the audited policy a run is conducted under, recorded as the
/// digest of its own text, which a value that changes per call would make name a text nothing produced.
/// </para>
/// <para>
/// The instant is written without its offset. What the model is being handed is the wall clock the text belongs to, and
/// what it is being asked for is the same, so an offset in the anchor is an invitation to write one back. The weekday
/// is stated beside it because <em>Tuesday</em> cannot be resolved from a date without counting, and a model that
/// counted it wrong resolves a period nobody asked for.
/// </para>
/// </remarks>
internal static class AgentTimeAnchor
{
    /// <summary>States the instant a turn's relative days, hours and periods are resolved against.</summary>
    /// <param name="instant">The anchor: the reader's own clock for text they are typing now, and the message's own arrival for text somebody else wrote.</param>
    /// <returns>The one line the anchor is stated as.</returns>
    internal static string Stated(DateTimeOffset instant) => string.Create(
        CultureInfo.InvariantCulture,
        $"Now, where this text was written: {instant:yyyy-MM-dd'T'HH:mm} ({instant:dddd}).");
}
