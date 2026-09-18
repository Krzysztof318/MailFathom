// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Contacts.Correspondence;

/// <summary>One conversation a contact's addresses appear in, as an opened contact reports it.</summary>
/// <param name="ThreadId">The conversation, which is what a client opens the exchange by.</param>
/// <param name="LatestStoredEmailId">The most recent message of it naming this person, so a client can open the message as well as the exchange.</param>
/// <param name="Subject">What that message was about, or <see langword="null" /> where it carried no subject.</param>
/// <param name="LastCorrespondedAt">When that message arrived.</param>
/// <remarks>
/// <para>
/// The message is the one that names this person rather than the conversation's own latest, and the subject and the
/// instant are read off it for the same reason: what an opened contact answers is when this exchange last involved
/// <em>them</em>, which a conversation that has run on without them would otherwise misreport as recent.
/// </para>
/// <para>
/// Nothing here is stored. Every field is computed from the mail index on the read, which is what keeps a contact from
/// growing a second copy of a correspondence that the mailbox already holds.
/// </para>
/// </remarks>
public sealed record CorrespondingThread(
    EmailThreadId ThreadId,
    StoredEmailId LatestStoredEmailId,
    string? Subject,
    DateTimeOffset LastCorrespondedAt);
