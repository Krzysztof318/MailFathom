// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Enrichment;

/// <summary>One message as a derivation is shown it: what a reader would see at the top, and its passages.</summary>
/// <param name="StoredEmailId">The message being derived from.</param>
/// <param name="Subject">The subject, or <see langword="null" /> where the message carried none.</param>
/// <param name="ReceivedAt">When the last receiving hop recorded the message, which is what a date in the text is read against.</param>
/// <param name="Passages">The message's own passages, in reading order and bounded by what one derivation may read.</param>
/// <remarks>
/// <para>
/// Deliberately narrow. A derivation is shown the subject, the arrival instant, and the text — never the sender, the
/// addressees, the folder, or the account — because none of those is what a sentence about the message is derived from,
/// and every one of them is a participant this deployment would then be sending to a provider for no reading it buys.
/// </para>
/// <para>
/// The arrival instant is here so that a relative date the text states can be resolved into one a row can show. It is
/// the message's own metadata rather than the current time, which is what keeps a derivation reproducible: the same
/// message derived again next month resolves *by Friday* to the same day.
/// </para>
/// </remarks>
public sealed record EnrichableEmail(
    StoredEmailId StoredEmailId,
    string? Subject,
    DateTimeOffset? ReceivedAt,
    IReadOnlyList<EnrichablePassage> Passages);
