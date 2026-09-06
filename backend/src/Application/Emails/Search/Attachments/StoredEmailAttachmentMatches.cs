// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Search.Attachments;

/// <summary>What one message's attachments contributed to a search result.</summary>
/// <remarks>
/// The identity travels beside the matches rather than on each of them, because a message is what a result is, and
/// repeating the identifier per attachment would let one read return two answers to the question of which message a
/// window row belongs to.
/// </remarks>
/// <param name="StoredEmailId">The message whose attachments matched.</param>
/// <param name="Matches">Its matching attachments, in the message's own walk order.</param>
public sealed record StoredEmailAttachmentMatches(
    StoredEmailId StoredEmailId,
    IReadOnlyList<EmailAttachmentMatch> Matches);
