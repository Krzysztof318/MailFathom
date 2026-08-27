// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Backend.Timeline;

/// <summary>One message of the list, as a deployment reports it.</summary>
/// <param name="Id">The stable local identity of the message, which every later request names it by.</param>
/// <param name="Account">The account the message was read from, as the accounts route names it.</param>
/// <param name="Folder">The folder alias the message was read from, as the folders route names it.</param>
/// <param name="ThreadId">The conversation the message belongs to, or <see langword="null" /> where nothing has placed it in one.</param>
/// <param name="Subject">The subject, or <see langword="null" /> where the message carried none.</param>
/// <param name="ReceivedAt">When the last receiving hop recorded the message, which is what the list is ordered by, or <see langword="null" /> where no header carried a usable date.</param>
/// <param name="SentAt">When the message says it was sent, or <see langword="null" /> where no header carried a usable date.</param>
/// <param name="SenderAddress">The sender's address as the message wrote it, or <see langword="null" /> where no usable sender was found.</param>
/// <param name="SenderDisplayName">The display name the sender wrote, or <see langword="null" /> where the header carried none.</param>
/// <param name="ToAddresses">The <c>To</c> addresses in header order, which is what a row of sent mail names instead of a sender.</param>
/// <param name="Unread">Whether the mail server last reported the message without <c>\Seen</c>.</param>
/// <param name="Flagged">Whether the mail server last reported it with <c>\Flagged</c>.</param>
/// <param name="Answered">Whether the mail server last reported it with <c>\Answered</c>.</param>
/// <param name="HasAttachments">Whether the message carries anything besides its body and its inline resources.</param>
/// <param name="AttachmentCount">How many of those there are.</param>
/// <param name="SizeOctets">The size the mail server reported for the message.</param>
/// <param name="Preview">The opening of the message's own text, bounded, or <see langword="null" /> where nothing has extracted the message yet.</param>
/// <remarks>
/// <para>
/// Everything a row draws arrives in the page that drew it, which is the whole point of this record being as wide as it
/// is: a list that asked a second route for a sender or a preview would be a request per visible row, and a list that
/// asked for bodies would be a megabyte to draw fifty lines. There is no body here and no raw MIME.
/// </para>
/// <para>
/// The three flags are the states a row draws rather than the observation they came from. A message no synchronization
/// run has read flags for reads as read, unflagged, and unanswered — which is what a folder still being backfilled
/// shows, and why how current a copy is belongs to the mailbox tree rather than to a row here.
/// </para>
/// <para>
/// All of it is this owner's own correspondence and carries the classification the root instructions put on mail: it is
/// put in front of that owner alone, and it reaches no log, no telemetry, and no local store.
/// </para>
/// </remarks>
public sealed record DeploymentMailMessage(
    Guid Id,
    string Account,
    string Folder,
    Guid? ThreadId,
    string? Subject,
    DateTimeOffset? ReceivedAt,
    DateTimeOffset? SentAt,
    string? SenderAddress,
    string? SenderDisplayName,
    IReadOnlyList<string> ToAddresses,
    bool Unread,
    bool Flagged,
    bool Answered,
    bool HasAttachments,
    int AttachmentCount,
    long SizeOctets,
    string? Preview)
{
    /// <summary>Gets the recipients, reading a document that named none as a message addressed to nobody this row draws.</summary>
    /// <remarks>
    /// A missing member deserializes to <see langword="null" /> rather than to an empty list, and every reader wants the
    /// same answer for the two. Said once here rather than at each reader, as the folders document already says it.
    /// </remarks>
    public IReadOnlyList<string> Recipients => this.ToAddresses ?? [];
}
