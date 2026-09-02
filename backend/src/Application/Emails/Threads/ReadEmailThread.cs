// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Threads;

/// <summary>The conversation one read email belongs to, as that read publishes it.</summary>
/// <remarks>
/// <para>
/// A read of one message answers the question a reader asks next: what else is in this exchange. The messages are named
/// rather than reproduced — an identifier, a subject, a sender, and a timestamp each — so the conversation can be
/// recognized and then read deliberately rather than being drawn out of a call that asked for one message.
/// </para>
/// <para>
/// Only messages the caller may see are here, and the count is of those. A message in a folder withheld from tools is in
/// no thread a tool publishes and in no thread size a tool publishes, because a size counting a message the caller may
/// not see reports that folder's contents one integer at a time.
/// </para>
/// </remarks>
public sealed record ReadEmailThread
{
    /// <summary>The greatest number of other emails one read names beside the email it returns.</summary>
    /// <remarks>
    /// Set where naming a conversation stops and reproducing a mailbox begins. Correspondence a person follows runs to a
    /// few dozen messages; past that the list is a mailing list's archive, which a reader asks for by the thread rather
    /// than receives inside every message of it. The result says when the bound cut the list, so a caller that wants the
    /// rest asks for the thread itself.
    /// </remarks>
    public const int MaximumNamedEmails = 50;

    /// <summary>Gets the conversation's identifier, which a content read may be repeated with to fetch the whole of it.</summary>
    public required EmailThreadId ThreadId { get; init; }

    /// <summary>
    /// Gets the zero-based place this email holds in the conversation's order, or <see langword="null" /> when the
    /// conversation was longer than one read assembles and this email fell outside what was assembled.
    /// </summary>
    /// <remarks>
    /// The absence is the honest answer rather than a gap. A place in an order is a statement about every message the
    /// order contains, and a read that stopped short of some of them is in no position to make it.
    /// </remarks>
    public int? Position { get; init; }

    /// <summary>Gets the message this email answers, or <see langword="null" /> when it is a root of what is shown.</summary>
    public StoredEmailId? AnsweredStoredEmailId { get; init; }

    /// <summary>Gets how many emails of the conversation one read assembled, this one included where it was assembled.</summary>
    /// <remarks>
    /// It counts what the caller may see rather than what the conversation holds, so a message in a folder withheld from
    /// tools is outside it. The same carve-out <see cref="Position" /> states applies here: a conversation longer than
    /// one read assembles is counted as far as the read reached, and where the email being read fell outside that, it is
    /// not among the counted. <see cref="MoreEmailsNotNamed" /> is set whenever that happened, and is set by the bound on
    /// how many emails one read names as well, so it does not say which of the two stopped the count.
    /// </remarks>
    public required int EmailCount { get; init; }

    /// <summary>Gets the conversation's other emails in its own order, bounded by <see cref="MaximumNamedEmails" />.</summary>
    public required IReadOnlyList<PlacedThreadedEmail> OtherEmails { get; init; }

    /// <summary>Gets whether the conversation holds emails this list does not name.</summary>
    /// <remarks>
    /// Stated rather than derived from the count, because two different bounds can cut the list — the one on what a read
    /// names and the one on how much of a conversation a query assembles at all — and a caller comparing lengths could
    /// not tell either from a conversation that simply ends there.
    /// </remarks>
    public required bool MoreEmailsNotNamed { get; init; }
}
