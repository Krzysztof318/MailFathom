// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Emails;

/// <summary>Describes one stored email as a mailbox listing shows it, without any of its content.</summary>
/// <remarks>
/// <para>
/// The summary is the bounded projection a list operation returns, which is the data-minimization control the privacy
/// design names for listing mail: it carries no raw MIME, no body, and no attachment bytes, and no query that produces
/// it reads the columns that hold them. What it does carry is personal data — a subject, a sender, and the addressees —
/// and inherits the classification of the mail it summarizes.
/// </para>
/// <para>
/// The addressees are the <c>To</c> addresses only. <c>Cc</c> and <c>Reply-To</c> are stored and filterable but not
/// listed, because a listing exists to let a reader recognize a message and the full participant set belongs to reading
/// it. Display names are absent for the same reason they are not persisted beside the sender's.
/// </para>
/// </remarks>
public sealed record EmailSummary
{
    /// <summary>Gets the stable local identity of the email, which every later request names it by.</summary>
    public required StoredEmailId StoredEmailId { get; init; }

    /// <summary>Gets the account whose mailbox the email was read from.</summary>
    public required MailAccountId AccountId { get; init; }

    /// <summary>Gets the folder alias the email was read from, which is MailFathom's own name for that folder.</summary>
    public required MailFolderAlias FolderAlias { get; init; }

    /// <summary>Gets the <c>Message-ID</c> the message carried, or <see langword="null" /> when it carried none this reader accepted.</summary>
    public string? InternetMessageId { get; init; }

    /// <summary>Gets the subject, or <see langword="null" /> when the message carried none.</summary>
    public string? Subject { get; init; }

    /// <summary>Gets when the message says it was sent, or <see langword="null" /> when no header carried a usable date.</summary>
    public DateTimeOffset? SentAt { get; init; }

    /// <summary>Gets when the last receiving hop recorded the message, or <see langword="null" /> when no header carried a usable date.</summary>
    /// <remarks>This is the timeline's ordering column, and an email that has none sorts at the undated end of the direction being read.</remarks>
    public DateTimeOffset? ReceivedAt { get; init; }

    /// <summary>Gets the size the mail server reported for the message.</summary>
    public long SizeOctets { get; init; }

    /// <summary>Gets the display name the sender wrote, or <see langword="null" /> when the header carried none.</summary>
    public string? SenderDisplayName { get; init; }

    /// <summary>Gets the sender's address as the message wrote it, or <see langword="null" /> when no usable sender was found.</summary>
    public string? SenderAddress { get; init; }

    /// <summary>Gets the comparison forms of the <c>To</c> addresses, in header order.</summary>
    public IReadOnlyList<string> ToAddresses { get; init; } = [];

    /// <summary>Gets what the email carries besides its body.</summary>
    public required StoredEmailAttachmentSummary Attachments { get; init; }

    /// <summary>Gets whether the raw MIME of the email is stored locally, or why it is not.</summary>
    /// <remarks>A caller reads this before asking for content, because a listing is served from local state whether or not a mail server is reachable.</remarks>
    public required StoredEmailContentAvailability ContentAvailability { get; init; }

    /// <summary>Gets the flags a mail server last showed for the email, and when they were read.</summary>
    public required RemoteEmailFlagSnapshot RemoteFlags { get; init; }

    /// <summary>Gets where the email sits in the timeline order, which is the boundary a continuation cursor is built from.</summary>
    public EmailTimelinePosition Position => new(this.ReceivedAt, this.StoredEmailId);
}
