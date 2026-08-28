// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Backend.Mail;

/// <summary>Everything a reading pane draws around one message, with none of its body or attachment octets.</summary>
/// <param name="StoredEmailId">The message.</param>
/// <param name="Account">The account the message belongs to.</param>
/// <param name="Folder">The folder the message belongs to.</param>
/// <param name="ThreadId">The conversation the message belongs to, or <see langword="null" /> where none is known.</param>
/// <param name="SizeOctets">The size the mail server reported for the whole message.</param>
/// <param name="Headers">What the message displays above its body.</param>
/// <param name="Body">Whether the body can be read and which forms the sender wrote.</param>
/// <param name="Sender">What the deployment established about the displayed author.</param>
/// <param name="Attachments">The files the message carries, described without their octets.</param>
/// <param name="Carried">The counts for the message's non-body parts, or <see langword="null" /> where none were parsed.</param>
/// <param name="Unread">Whether the mail server last reported the message as unseen.</param>
/// <param name="Flagged">Whether the mail server last reported the message as flagged.</param>
/// <param name="Answered">Whether the mail server last reported the message as answered.</param>
public sealed record DeploymentMailMessageDetail(
    Guid StoredEmailId,
    string Account,
    string Folder,
    Guid? ThreadId,
    long SizeOctets,
    DeploymentMailHeaders Headers,
    DeploymentMailBodyForms Body,
    DeploymentMailSenderVerdict Sender,
    IReadOnlyList<DeploymentMailAttachment> Attachments,
    DeploymentMailCarriedParts? Carried,
    bool Unread,
    bool Flagged,
    bool Answered);

/// <summary>The headers one message displays above its body.</summary>
public sealed record DeploymentMailHeaders(
    string? Subject,
    DateTimeOffset? SentAt,
    DateTimeOffset? ReceivedAt,
    IReadOnlyList<DeploymentMailParticipant> Participants,
    string? MessageId,
    string? InReplyTo,
    IReadOnlyList<string> References);

/// <summary>One address a message wrote, and the header it appeared in.</summary>
public sealed record DeploymentMailParticipant(string Role, string Address, string? DisplayName);

/// <summary>Whether a message's body can be read and which forms the sender wrote.</summary>
public sealed record DeploymentMailBodyForms(string Availability, bool PlainText, bool Html);

/// <summary>What the deployment established about the displayed author.</summary>
public sealed record DeploymentMailSenderVerdict(string AuthorAuthentication, string DeploymentTrust);

/// <summary>One file a message carries, described without its octets.</summary>
public sealed record DeploymentMailAttachment(
    int Position,
    string? FileName,
    bool WasFileNameNormalized,
    string MediaType,
    long SizeOctets);

/// <summary>The counts for everything one message carries besides its body.</summary>
public sealed record DeploymentMailCarriedParts(
    int AttachmentCount,
    long TotalSizeOctets,
    int InlineResourceCount,
    bool Encrypted,
    bool UnverifiedSignature,
    bool UnexpandedTnefPart);
