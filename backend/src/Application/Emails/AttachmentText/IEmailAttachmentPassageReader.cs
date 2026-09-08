// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.AttachmentText;

/// <summary>Reads back the passages one message's attachments were cut into, each with the place inside its file.</summary>
/// <remarks>
/// <para>
/// The port exists because an attachment passage is content a caller may legitimately receive, which is the one way
/// this model differs from the message's. Nothing serves a message chunk back — a search or an answer that needs a
/// message reads it in full — and there is deliberately no equivalent of this port for one.
/// </para>
/// <para>
/// It resolves the coordinate rather than making a caller do it. A passage records where it begins in the attachment's
/// extracted text and the attachment records where each of its pages begins in that same text, so turning the first
/// into the second is one comparison — but it is a comparison every caller would otherwise write, and one of them would
/// write it as the <em>first</em> boundary at or after the offset and cite the following page.
/// </para>
/// <para>
/// It reaches no parser, no provider, and no mail server. Everything it returns was derived by a background pass and
/// stored, which is what lets a read path use it at all.
/// </para>
/// </remarks>
public interface IEmailAttachmentPassageReader
{
    /// <summary>Reads one window of the passages cut from a message's attachments.</summary>
    /// <param name="emailId">The message whose attachment passages are read.</param>
    /// <param name="resumeAfter">The passage to continue past, or <see langword="null" /> to start at the first one.</param>
    /// <param name="windowSize">How many passages this read may return, which it never exceeds.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The passages ordered by attachment and then by their place inside it, or an empty list where none remain.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="windowSize" /> is not positive.</exception>
    /// <remarks>
    /// <para>
    /// Windowed rather than whole, because what one message's attachments hold is the sender's to decide: twenty
    /// attachments of two hundred thousand characters each is what the per-attachment ceilings already permit, and a
    /// caller wanting the passage a search matched should not load four million characters to serve it. The window and
    /// the cursor are on the port rather than left to its first caller so that the bound holds for every later one.
    /// </para>
    /// <para>
    /// An empty answer covers every way a message can have no further attachment passage — none was ever read, every
    /// one of them was refused, the message carries no attachment at all, or the window has reached the end — because
    /// none of those is a state a caller acts on differently. What the reason was is on the attachment's own row, which
    /// is where a user asking why is answered.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<AttachmentPassage>> ReadAttachmentPassagesAsync(
        StoredEmailId emailId,
        AttachmentPassagePosition? resumeAfter,
        int windowSize,
        CancellationToken cancellationToken);
}
