// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Summaries;

namespace MailFathom.Application.Emails.BrowseTimeline;

/// <summary>One row of a message list: the summary every mailbox read publishes, and the preview only a list shows.</summary>
/// <param name="Email">The email as every other read of this deployment describes it.</param>
/// <param name="Preview">The opening of the message's own text, or <see langword="null" /> where nothing has extracted the message yet.</param>
/// <remarks>
/// The summary is composed rather than copied, so a list row and a tool listing cannot come to disagree about the same
/// message, and a field added to one arrives on the other. What a row adds is the preview and nothing else — the two
/// are separate values because they come from separate tables and a message may have the first without the second.
/// </remarks>
public sealed record BrowsedEmail(EmailSummary Email, string? Preview);
