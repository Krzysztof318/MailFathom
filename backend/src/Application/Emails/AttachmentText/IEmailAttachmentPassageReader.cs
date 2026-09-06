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
    /// <summary>Reads the passages cut from one message's attachments.</summary>
    /// <param name="emailId">The message whose attachment passages are read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The passages ordered by attachment and then by their place inside it, or an empty list where the message has none.</returns>
    /// <remarks>
    /// An empty answer covers every way a message can have no attachment passage — none was ever read, every one of
    /// them was refused, the message carries no attachment at all — because none of those is a state a caller acts on
    /// differently. What the reason was is on the attachment's own row, which is where an owner asking why is answered.
    /// </remarks>
    Task<IReadOnlyList<AttachmentPassage>> ReadAttachmentPassagesAsync(
        StoredEmailId emailId,
        CancellationToken cancellationToken);
}
