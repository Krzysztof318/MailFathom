// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.ReplyDrafts;

/// <summary>Everything one drafting is written from: the correspondence it answers, who is in it, and how its author writes.</summary>
/// <param name="Subject">The subject the conversation is read under, or <see langword="null" /> where no message carried one.</param>
/// <param name="Messages">The conversation's most recent messages in its own order, which is what the draft is grounded in and cites.</param>
/// <param name="Participants">The people the conversation names, which is the whole set a proposed recipient may come from.</param>
/// <param name="StyleSamples">What the person themselves has recently sent, which the draft's manner is derived from, and which is empty where a deployment derives no style.</param>
/// <remarks>
/// <para>
/// Deliberately narrow, exactly as a conversation's own derivation is. What a draft is written from is what the people
/// in the exchange said and how this person writes; no folder, no account, and no mailbox is part of either, and every
/// one of them would be somebody's data sent to a provider for nothing the reply would gain.
/// </para>
/// <para>
/// The participants carry an address and the messages do not, and the difference is load-bearing: an address never
/// travels to a provider, and a proposed recipient is answered as a position in this list rather than as text a model
/// wrote. That is what makes it impossible for a drafting to address a reply to somebody the conversation never named.
/// </para>
/// </remarks>
public sealed record ReplyDraftSources(
    string? Subject,
    IReadOnlyList<ReplyDraftMessage> Messages,
    IReadOnlyList<ReplyDraftParticipant> Participants,
    IReadOnlyList<string> StyleSamples);

/// <summary>One message of the conversation as a drafting is shown it.</summary>
/// <param name="StoredEmailId">The message, which is what a statement drawn from it cites.</param>
/// <param name="Position">The zero-based place the message holds in the order the turn publishes, which is how a citation names it.</param>
/// <param name="AuthorDisplayName">The name the message was written under, or <see langword="null" /> where it carried none.</param>
/// <param name="SentAt">When the message was written, which is what a date its text states relatively is read against.</param>
/// <param name="Text">What the message added, with the history it quoted already trimmed off, bounded by what one turn may carry.</param>
public sealed record ReplyDraftMessage(
    StoredEmailId StoredEmailId,
    int Position,
    string? AuthorDisplayName,
    DateTimeOffset? SentAt,
    string Text);

/// <summary>One person the conversation names, as a proposed recipient resolves back to.</summary>
/// <param name="Position">The zero-based place the person holds in the list the turn publishes, which is how a proposal names them.</param>
/// <param name="Address">Their address, which stays inside this deployment and is never put to a provider.</param>
public sealed record ReplyDraftParticipant(int Position, EmailAddress Address);
