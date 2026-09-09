// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.ThreadStates;

/// <summary>One conversation as a derivation is shown it, and the shape of it the answer will be recorded against.</summary>
/// <param name="ThreadId">The conversation being derived from, which is the surviving thread of any merge.</param>
/// <param name="Subject">The subject the conversation is read under, or <see langword="null" /> where no message carried one.</param>
/// <param name="Revision">The shape of the conversation this reading was taken from.</param>
/// <param name="Messages">The messages in the conversation's own order, or empty where the conversation is past the bound.</param>
/// <param name="ExceedsBound">Whether the conversation holds more messages than one derivation may take in.</param>
/// <remarks>
/// <para>
/// Deliberately narrow, exactly as one message's derivation is. A reading is shown the subject, who wrote each message,
/// when they wrote it, and what they said; it is shown no folder, no account, no addressee list, and no mailbox. None
/// of those is what *where did we leave this* is derived from, and every one of them is somebody this deployment would
/// then be sending to a provider for no reading it buys.
/// </para>
/// <para>
/// A conversation past the bound arrives with no messages at all rather than with the leading ones. That is what makes
/// <see cref="ExceedsBound" /> honest: the text was never read out of the store, so nothing partial exists to be sent
/// by mistake, and the record written for it says the conversation was not read rather than summarizing a third of it.
/// </para>
/// </remarks>
public sealed record DerivableThread(
    EmailThreadId ThreadId,
    string? Subject,
    ThreadStateRevision Revision,
    IReadOnlyList<DerivableThreadMessage> Messages,
    bool ExceedsBound);

/// <summary>One message of a conversation as a derivation is shown it.</summary>
/// <param name="StoredEmailId">The message, which is what a statement drawn from it cites.</param>
/// <param name="Position">The zero-based place the message holds in the conversation's order, which is how a turn names it.</param>
/// <param name="AuthorDisplayName">The name the message was written under, or <see langword="null" /> where it carried none.</param>
/// <param name="SentAt">When the message was written, which is what a date its text states relatively is read against.</param>
/// <param name="Text">What the message added, with the history it quoted already trimmed off, bounded by what one turn may carry.</param>
/// <remarks>
/// The author is a display name rather than an address, because what a statement needs to say is *who* undertook
/// something and a name is what a reader recognizes. An address would add a second identifier to every turn for a
/// reading that would not change, and addresses are the part of a header worth not sending where a name will do.
/// </remarks>
public sealed record DerivableThreadMessage(
    StoredEmailId StoredEmailId,
    int Position,
    string? AuthorDisplayName,
    DateTimeOffset? SentAt,
    string Text);
