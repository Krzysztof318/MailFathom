// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Chunking;

/// <summary>One retrievable passage of a message, and the span of the extracted text it was cut from.</summary>
/// <remarks>
/// <para>
/// A chunk names its own span rather than only the message it belongs to, so a later citation can point at the passage
/// that answered a question instead of at the whole message. <see cref="StartOffset" /> and the length of
/// <see cref="Text" /> index the reading of the body named by the rules' source form, which makes the span verifiable:
/// the same offsets applied to the same extracted text return exactly this text.
/// </para>
/// <para>
/// The remaining source coordinates a chunk could carry — the account, the folder, the sender, the recipients, the
/// date, and the subject — are the stored email's own columns and are reached through the message a
/// persisted chunk hangs on. They are deliberately not copied here: every one of them is mail content or personal data,
/// and a second copy would widen the access, export, and erasure surface without answering a question the message
/// cannot already answer.
/// </para>
/// <para>
/// A chunk's text is mail content. Nothing here may be written to a log, a metric, a trace, or an error message; the
/// ordinal, the offsets, and the length are the only members safe to report.
/// </para>
/// </remarks>
/// <param name="Ordinal">The chunk's position in its message, counted from zero in reading order.</param>
/// <param name="StartOffset">Where the chunk begins in the extracted text it was cut from.</param>
/// <param name="Text">The passage itself.</param>
/// <param name="ContentHash">What identifies this text under the rules that produced it.</param>
/// <param name="RuleSetVersion">The version of the rules that produced it, copied so a backfill can select on it.</param>
/// <param name="IsDerivedFromLossyHtml">Whether the text was inferred from markup rather than read from a plain-text part.</param>
public sealed record EmailTextChunk(
    int Ordinal,
    int StartOffset,
    string Text,
    EmailChunkContentHash ContentHash,
    int RuleSetVersion,
    bool IsDerivedFromLossyHtml)
{
    /// <summary>Gets where the chunk ends in the extracted text it was cut from, one past its last character.</summary>
    public int EndOffset => this.StartOffset + this.Text.Length;
}
