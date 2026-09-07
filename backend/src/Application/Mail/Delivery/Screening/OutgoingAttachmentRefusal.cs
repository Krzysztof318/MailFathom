// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Delivery.Screening;

/// <summary>Why an outgoing message's attachments left the screen with nothing to judge them by.</summary>
/// <remarks>
/// <para>
/// Two reasons rather than one, because the author is told a different thing by each and only one of the two is about
/// their file. A document the extractor itself refused is one the deployment got no text out of, and the answer names
/// the remedy: attach it in a form something here parses. A message that exhausted the ceilings a whole message is read
/// within says nothing about any one file — every one of them may have been read successfully — so answering the same
/// way would tell an author to convert a document that was never the problem.
/// </para>
/// <para>
/// <b>One of the outcomes behind the first is not permanent, and the answer says so rather than being split off.</b> A
/// read that ran past the extraction timeout is a fact about how loaded the host was, so asking again unchanged is what
/// would work — while an encrypted, malformed, or unparsable file will answer the same way forever. A refusal of its
/// own would have to publish which of the two it was, and that is exactly what the paragraph below refuses to say; so
/// the one answer offers asking again before it offers converting the file, which is right for both and misleads
/// neither.
/// </para>
/// <para>
/// Neither carries which extraction outcome or which ceiling it was. That is a fact about how this deployment is
/// configured rather than about the message, and reporting it to whoever composed the message would say what the reader
/// found in somebody's file.
/// </para>
/// </remarks>
public enum OutgoingAttachmentRefusal
{
    /// <summary>One attached document was offered to the extractor and came back with no text: encrypted, unparsable, a format nothing here reads, or past a ceiling of its own.</summary>
    NotRead = 0,

    /// <summary>The message's attachments together reached a ceiling the whole message is read within, so the remainder was never opened.</summary>
    MessageCeilingReached = 1,
}
