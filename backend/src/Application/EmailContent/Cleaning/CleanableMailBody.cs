// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Cleaning;

/// <summary>One message's reduced body as the outline a cleaning is decided from, rather than as the body itself.</summary>
/// <param name="Subject">The subject the message carried, or <see langword="null" /> where it carried none.</param>
/// <param name="SenderName">The envelope sender, as the address or the name it was written under, or <see langword="null" /> where the message named nobody.</param>
/// <param name="Blocks">One entry per top-level block of the reduced document, in reading order and numbered by it.</param>
/// <remarks>
/// <para>
/// <b>This is the whole of what may reach a provider, and it is an outline rather than a body.</b> A block contributes its
/// kind, how many links it holds, and the opening of its text — so a decision can be made about a block without the
/// block's own content being the thing sent. What comes back is a set of index ranges, which is why fidelity is a
/// property of the answer's type here rather than of how firmly an instruction asked for it.
/// </para>
/// <para>
/// The envelope is in it deliberately and is not decoration. The rule that drops a <c>From</c>/<c>Sent</c>/<c>To</c>
/// block pasted into a body by a forwarding system turns on whether that block repeats the envelope, so a cleaning asked
/// without the envelope drops the forwarded message's only record of who wrote what is being read.
/// </para>
/// <para>
/// All of it is somebody's mail. None of it reaches a log line, a span attribute, or a telemetry event, and what leaves
/// the deployment leaves through the egress guard like every other text this system sends.
/// </para>
/// </remarks>
public sealed record CleanableMailBody(
    string? Subject,
    string? SenderName,
    IReadOnlyList<CleanableMailBlock> Blocks);

/// <summary>One top-level block of a reduced body, described rather than carried.</summary>
/// <param name="Index">The block's place in the document's reading order, which is how an answer names it.</param>
/// <param name="Kind">The catalogued identity of the block, which is the word the document itself publishes.</param>
/// <param name="LinkCount">How many links the block holds, counted through whatever nesting it has.</param>
/// <param name="Opening">The start of the block's text, bounded, and empty for a block that carries no words.</param>
/// <remarks>
/// The index is a position in a list this deployment composed rather than anything it stored, which is what keeps an
/// answer's reach inside the message it was asked about: a number outside the list names no block and takes the answer
/// with it.
/// </remarks>
public sealed record CleanableMailBlock(int Index, string Kind, int LinkCount, string Opening);
