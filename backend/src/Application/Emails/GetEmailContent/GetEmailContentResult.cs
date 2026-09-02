// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.GetEmailContent;

/// <summary>What one content read produced for every email it named.</summary>
/// <param name="Emails">One outcome per named email, in the order the request named them.</param>
/// <param name="UnreadThreadEmails">
/// The emails of a named conversation this call did not carry, in the conversation's own order, and empty for every
/// read that named its emails itself. It names what one assembly of the conversation held past the read's own bound,
/// which is at most <see cref="Threads.IEmailThreadReader.MaximumAssembledEmails" /> emails: a conversation longer than
/// that is assembled as far as the bound reaches, so the identities past it are named here no more than they were read.
/// </param>
/// <remarks>
/// <para>
/// The order is the contract twice over: it is how a caller pairs an outcome with what it asked for, and it is the order
/// the read's character budget was spent in, so a body cut by that budget is one an earlier email in the same list drew
/// on. A read that named a conversation is answered in that conversation's order for the same reason it exists —
/// receiving an exchange out of sequence is receiving a different thing.
/// </para>
/// <para>
/// The unread messages are named rather than counted, so a caller reads the rest of a long conversation by asking for
/// those identities directly instead of by paging a call that has no cursor. It is empty when the conversation fitted,
/// which is the ordinary case.
/// </para>
/// <para>
/// This is the most sensitive projection MailFathom publishes. Everything reachable from it is message content and
/// inherits every classification, retention, access, and erasure constraint of the mail it was read from. Nothing in it
/// may be logged.
/// </para>
/// </remarks>
public sealed record GetEmailContentResult(
    IReadOnlyList<EmailContentReadOutcome> Emails,
    IReadOnlyList<StoredEmailId> UnreadThreadEmails);
