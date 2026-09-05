// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Discovery.Citations;

/// <summary>The message one resolved citation belongs to, as much of it as showing the citation in place needs.</summary>
/// <param name="StoredEmailId">The stable local identity, which is the same one every other read names an email by.</param>
/// <param name="AccountId">The account whose mailbox it was read from.</param>
/// <param name="FolderAlias">MailFathom's own name for the folder it was read from.</param>
/// <param name="Subject">The subject it carried, or <see langword="null" /> when it carried none.</param>
/// <param name="SentAt">When the sender says it was sent, or <see langword="null" /> when it wrote no usable date.</param>
/// <param name="ReceivedAt">When the last receiving hop recorded it, or <see langword="null" /> when no header carried a usable date.</param>
/// <remarks>
/// <para>
/// It travels with every resolution the caller may read, including one whose place inside the message is gone, so a
/// citation can be drawn where it stands rather than as a link somebody has to follow to find out what it names. That
/// is the one way this differs from <see cref="Retrieval.AskMail.MailAnswerCitation" />, which deliberately carries no
/// extract: there the passage had already reached a provider and republishing it would have widened nothing but the
/// response, while here the whole request is somebody checking a fact against the words behind it.
/// </para>
/// <para>
/// The subject is mail content and the two dates are personal data, so none of this reaches a log, a span attribute, or
/// a telemetry event.
/// </para>
/// </remarks>
public sealed record CitedMessage(
    StoredEmailId StoredEmailId,
    MailAccountId AccountId,
    MailFolderAlias FolderAlias,
    string? Subject,
    DateTimeOffset? SentAt,
    DateTimeOffset? ReceivedAt);
