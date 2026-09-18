// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts.Correspondence;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Contacts.Relationship;

/// <summary>What one statement about a correspondence rests on: a conversation naming the contact, or a document they sent.</summary>
/// <param name="StoredEmailId">The message the statement leads back to — the conversation's most recent message naming this person, or the message a document arrived on.</param>
/// <param name="AttachmentPosition">Where the document sits in that message's walk, or <see langword="null" /> where the statement rests on the conversation rather than on a file.</param>
/// <remarks>
/// <para>
/// A citation in the terms every other citation in this system is written in, so a reader follows one through the same
/// resolution a Discover answer's sources are followed through: a conversation resolves as the message it was last
/// carried by, and a document as that message's own attachment.
/// </para>
/// <para>
/// It names something the correspondence already published rather than anything a model wrote. What a producer
/// answers with is a position in the list the turn numbered, and that position is resolved here against the threads
/// and the documents the read produced — so a statement can cite no message outside the correspondence this contact
/// was correlated with, and a number naming nothing takes its statement with it.
/// </para>
/// </remarks>
public sealed record ContactRelationshipSource(StoredEmailId StoredEmailId, int? AttachmentPosition)
{
    /// <summary>Cites the conversation a statement rests on.</summary>
    /// <param name="thread">The conversation, as the correlation reported it.</param>
    /// <returns>The citation, which names that conversation's most recent message involving this person.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="thread" /> is <see langword="null" />.</exception>
    public static ContactRelationshipSource Conversation(CorrespondingThread thread)
    {
        ArgumentNullException.ThrowIfNull(thread);

        return new ContactRelationshipSource(thread.LatestStoredEmailId, AttachmentPosition: null);
    }

    /// <summary>Cites the document a statement rests on.</summary>
    /// <param name="document">The document, as the correlation reported it.</param>
    /// <returns>The citation, which names the message the file arrived on and the file's place in it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="document" /> is <see langword="null" />.</exception>
    public static ContactRelationshipSource Document(CorrespondingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new ContactRelationshipSource(document.StoredEmailId, document.AttachmentPosition);
    }
}
