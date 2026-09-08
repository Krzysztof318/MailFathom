// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Enrichment;
using MailFathom.Application.Emails.Summaries;

namespace MailFathom.Application.Emails.BrowseTimeline;

/// <summary>One row of a message list: the summary every mailbox read publishes, the preview only a list shows, and what was derived about it.</summary>
/// <param name="Email">The email as every other read of this deployment describes it.</param>
/// <param name="Preview">The opening of the message's own text, or <see langword="null" /> where nothing has extracted the message yet.</param>
/// <param name="Enrichment">What a derivation concluded about the message, or <see langword="null" /> where none has reached it.</param>
/// <param name="ThreadMessageCount">How many messages this caller may see in the message's conversation, or <see langword="null" /> where nothing has placed it in one.</param>
/// <remarks>
/// <para>
/// The summary is composed rather than copied, so a list row and a tool listing cannot come to disagree about the same
/// message, and a field added to one arrives on the other. What a row adds are the preview, the enrichment and the
/// conversation's size — separate values because they come from separate tables, and a message may have any of them
/// without the others.
/// </para>
/// <para>
/// The count is of the conversation rather than of the page, which is the whole reason it is read here instead of
/// derived by whoever draws the list: a correspondence spans a page boundary as readily as it sits inside one, and
/// counting within a page would say a different number depending on where the page happened to be cut.
/// </para>
/// <para>
/// An absent enrichment is a message no derivation has reached, which a client draws as a row nothing has been said
/// about. An enrichment with no marks is a derivation that ran and found nothing worth saying, and the two are
/// deliberately distinguishable: one of them will change on a later run and the other will not.
/// </para>
/// </remarks>
public sealed record BrowsedEmail(
    EmailSummary Email,
    string? Preview,
    EmailEnrichment? Enrichment,
    int? ThreadMessageCount);
