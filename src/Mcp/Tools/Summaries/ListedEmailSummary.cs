// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Domain.Emails;
using MailFathom.Mcp.Tools.Authorship;
using MailFathom.Mcp.Tools.Senders;

namespace MailFathom.Mcp.Tools.Summaries;

/// <summary>Publishes one email of a listed page.</summary>
/// <remarks>
/// <para>
/// The projection is the use case's, republished rather than narrowed a second time here: it carries what identifies and
/// characterizes an email and never its body, its raw MIME, or its attachment bytes. <c>Cc</c> and <c>Reply-To</c> are
/// filterable but deliberately not listed, because a listing exists to let a reader recognize a message and the full
/// participant set belongs to reading it.
/// </para>
/// <para>
/// The descriptions are part of the published output schema. They name the unit and the absence meaning of each value,
/// because a caller reading a null timestamp or a zero count otherwise has to guess whether the mail lacked the header or
/// MailFathom lacked the data.
/// </para>
/// </remarks>
[Description("A summary of one locally stored email. Contains no body text, no raw MIME, and no attachment content.")]
internal sealed record ListedEmailSummary
{
    /// <summary>Gets the stable local identity a caller reads content by.</summary>
    [Description("The stable local identifier of the email. Pass it to a single-email read to retrieve content; it does not change when the mail server renumbers or moves the message.")]
    public required string StoredEmailId { get; init; }

    /// <summary>Gets the configured account the email was synchronized from.</summary>
    [Description("The configured MailFathom account identifier the email was synchronized from.")]
    public required string AccountId { get; init; }

    /// <summary>Gets the name that account is published under.</summary>
    [Description("The display name the account is published under, which is the operator's own name for the mailbox. Quote this rather than accountId when telling a person which mailbox an email came from; either value may be used to name the account in a later request.")]
    public required string AccountDisplayName { get; init; }

    /// <summary>Gets the folder alias the email was synchronized under.</summary>
    [Description("The MailFathom folder alias the email was synchronized under, such as INBOX. This is MailFathom's own name for the folder rather than the path the mail server advertises.")]
    public required string FolderAlias { get; init; }

    /// <summary>Gets the conversation the email belongs to, or <see langword="null" /> when nothing has placed it in one.</summary>
    [Description("The identifier of the conversation this email belongs to, or null when it has not been assembled into one. Two emails carrying the same threadId are the same exchange; a matching subject is not. Pass it to a content read as threadId to retrieve the conversation itself.")]
    public string? ThreadId { get; init; }

    /// <summary>Gets the <c>Message-ID</c> the email carried, or <see langword="null" /> when it carried none.</summary>
    [Description("The Message-ID header the email carried, or null when it carried none this reader accepted. Name the email by storedEmailId rather than by this value in a later request.")]
    public string? InternetMessageId { get; init; }

    /// <summary>Gets the decoded subject, or <see langword="null" /> when the email carried none.</summary>
    [Description("The decoded subject, or null when the email carried no subject header.")]
    public string? Subject { get; init; }

    /// <summary>Gets the sender address as the email wrote it, or <see langword="null" /> when it carried no usable one.</summary>
    [Description("The sender address as written by the email, or null when it carried no usable sender address. This is a claim the email made about itself and nothing here verified it; senderVerification is what says whether anything did.")]
    public string? SenderAddress { get; init; }

    /// <summary>Gets the sender display name, or <see langword="null" /> when the email carried none.</summary>
    [Description("The display name the sender wrote, or null when the header carried none.")]
    public string? SenderDisplayName { get; init; }

    /// <summary>Gets what was established about the author the email displays, and what this deployment made of it.</summary>
    /// <remarks>
    /// It sits beside the sender address deliberately: the address is what the email wrote about itself, and this is
    /// what a trusted server established and what this deployment recognized. A listing carries the verdict and not the
    /// evidence behind it, which the single-email read publishes.
    /// </remarks>
    public required ReportedSenderVerification SenderVerification { get; init; }

    /// <summary>Gets how much the email's own text read as machine written.</summary>
    /// <remarks>
    /// A second, independent reading beside the sender verdict and never a refinement of it: that one is about who sent
    /// the email and this one is about how its text was written. A listing carries the reading and not the signals
    /// behind it, which the single-email read publishes.
    /// </remarks>
    public required ReportedMachineAuthorship MachineAuthorship { get; init; }

    /// <summary>Gets the <c>To</c> addresses in header order.</summary>
    [Description("The To addresses in header order. Cc and Reply-To are searchable but not listed, and recipient display names are not returned; the full participant set belongs to a single-email read.")]
    public required IReadOnlyList<string> ToAddresses { get; init; }

    /// <summary>Gets when the email was sent according to its own header, or <see langword="null" /> when it carried no usable date.</summary>
    [Description("When the sender claims the email was sent, as an ISO 8601 timestamp, or null when the Date header was missing or unparseable. Prefer receivedAt for ordering, since a sender controls this value.")]
    public DateTimeOffset? SentAt { get; init; }

    /// <summary>Gets when the last receiving hop recorded the email, or <see langword="null" /> when no header carried a usable date.</summary>
    [Description("When the receiving infrastructure recorded the email, as an ISO 8601 timestamp, or null when no header carried a usable date. This is the value the timeline is ordered and date-filtered by, and an email without it sorts at the undated end of whichever direction is read.")]
    public DateTimeOffset? ReceivedAt { get; init; }

    /// <summary>Gets the size the mail server reported for the email.</summary>
    [Description("The size of the whole email in bytes, as reported by the mail server.")]
    public required long SizeBytes { get; init; }

    /// <summary>Gets what the email carries besides its body.</summary>
    public required ListedEmailAttachments Attachments { get; init; }

    /// <summary>Gets the flags the last synchronization run observed on the server.</summary>
    public required ObservedRemoteFlags RemoteFlags { get; init; }

    /// <summary>Gets whether raw content is available locally, and the reason when it is not.</summary>
    [Description("Whether the raw email content is stored locally: 'available' when a content read will succeed, 'exceededSizeLimit' when the email was deliberately stored without its content because it was larger than the configured limit, or 'awaitingStorageHeadroom' when local storage was full when it arrived and its content is fetched once there is room. Read this before asking for content; only the last state is worth asking about again later.")]
    public required ListedEmailContentAvailability ContentAvailability { get; init; }

    /// <summary>Publishes one summary a read returned.</summary>
    /// <param name="summary">The application summary to publish.</param>
    /// <param name="accountNames">Reads the name the summary's account is published under.</param>
    /// <returns>The wire representation of <paramref name="summary" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="summary" /> or <paramref name="accountNames" /> is <see langword="null" />.</exception>
    public static ListedEmailSummary From(EmailSummary summary, PublishedAccountNames accountNames)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(accountNames);

        return new ListedEmailSummary
        {
            StoredEmailId = summary.StoredEmailId.ToString(),
            AccountId = summary.AccountId.Value,
            AccountDisplayName = accountNames.For(summary.AccountId),
            FolderAlias = summary.FolderAlias.Value,
            ThreadId = summary.ThreadId?.ToString(),
            InternetMessageId = summary.InternetMessageId,
            Subject = summary.Subject,
            SenderAddress = summary.SenderAddress,
            SenderDisplayName = summary.SenderDisplayName,
            SenderVerification = ReportedSenderVerification.From(summary.SenderVerification),
            MachineAuthorship = ReportedMachineAuthorship.From(summary.MachineAuthorship),
            ToAddresses = summary.ToAddresses,
            SentAt = summary.SentAt,
            ReceivedAt = summary.ReceivedAt,
            SizeBytes = summary.SizeOctets,
            Attachments = ListedEmailAttachments.From(summary.Attachments),
            RemoteFlags = ObservedRemoteFlags.From(summary.RemoteFlags),
            ContentAvailability = PublishedAvailability(summary.ContentAvailability),
        };
    }

    /// <summary>Reads the published value the stored availability names.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when a stored state has no published value, which means one was added to the domain without deciding what
    /// a client should be told about it.
    /// </exception>
    private static ListedEmailContentAvailability PublishedAvailability(StoredEmailContentAvailability availability) =>
        availability switch
        {
            StoredEmailContentAvailability.Available => ListedEmailContentAvailability.Available,
            StoredEmailContentAvailability.ExceededSizeLimit => ListedEmailContentAvailability.ExceededSizeLimit,
            StoredEmailContentAvailability.AwaitingStorageHeadroom => ListedEmailContentAvailability.AwaitingStorageHeadroom,
            _ => throw new ArgumentOutOfRangeException(
                nameof(availability),
                availability,
                "The stored content availability has no published protocol value."),
        };
}
