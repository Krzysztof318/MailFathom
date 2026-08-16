// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Emails.Threads;

/// <summary>One message of a conversation, as much of it as placing and naming the message in that conversation needs.</summary>
/// <remarks>
/// <para>
/// Narrower than <see cref="Summaries.EmailSummary" /> deliberately, and the narrowing is the data-minimization control:
/// a caller reading one message is shown the rest of the conversation so it can recognize what else is there, not so it
/// can read the conversation through the back of a content call. The subject and the sender are what a person picks a
/// message out of a list by, and the timestamp is what tells them when — nothing else about the other messages is
/// published here.
/// </para>
/// <para>
/// The account and the folder are carried because they are what the read decides visibility on. A message in a folder an
/// operator withheld from tools is outside every mailbox read, so it is outside the thread a tool publishes, and that
/// decision belongs to the use case rather than to the query that produced these rows.
/// </para>
/// <para>
/// It still carries personal data — a subject and an address — and inherits the classification of the mail it describes.
/// </para>
/// </remarks>
public sealed record EmailThreadMessage
{
    /// <summary>Gets the stable local identity of the message, which a caller names it by to read its content.</summary>
    public required StoredEmailId StoredEmailId { get; init; }

    /// <summary>Gets the account whose mailbox the message was read from.</summary>
    public required MailAccountId AccountId { get; init; }

    /// <summary>Gets the folder alias the message was read from.</summary>
    public required MailFolderAlias FolderAlias { get; init; }

    /// <summary>Gets the message this one answers, or <see langword="null" /> when it answers none stored here.</summary>
    /// <remarks>
    /// The stored relation rather than the published one. A message whose parent is withheld from the caller is
    /// published as a root of what they are shown, which the ordering decides; this value is what it decides from.
    /// </remarks>
    public StoredEmailId? ParentStoredEmailId { get; init; }

    /// <summary>Gets the subject, or <see langword="null" /> when the message carried none.</summary>
    public string? Subject { get; init; }

    /// <summary>Gets when the message says it was sent, or <see langword="null" /> when no header carried a usable date.</summary>
    /// <remarks>
    /// It settles the order between messages answering the same parent and decides nothing else. A <c>Date</c> header is
    /// what a sending client asserted, written by a clock this deployment does not control, so a reply arriving before
    /// the message it answers is ordinary and must not reorder what the reply relation already placed.
    /// </remarks>
    public DateTimeOffset? SentAt { get; init; }

    /// <summary>Gets the sender's address as the message wrote it, or <see langword="null" /> when it carried none usable.</summary>
    public string? SenderAddress { get; init; }
}
