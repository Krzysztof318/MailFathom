// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Presentation.Spaces.Mail.Reading;

/// <summary>The entries used to compose message details that no control authors on its own.</summary>
internal static class MailMessageWords
{
    internal const string TrustedSenderKey = "MailMessage.Sender.Trusted";
    internal const string FailedSenderKey = "MailMessage.Sender.Failed";
    internal const string UnrecognizedSenderKey = "MailMessage.Sender.Unrecognized";
    internal const string AttachmentFallbackKey = "MailMessage.Attachment.Fallback";
    internal const string NormalizedFileNameKey = "MailMessage.Attachment.Normalized";
    internal const string AttachmentFileTypeKey = "MailMessage.Attachment.FileType";

    internal static IReadOnlyList<string> ResourceKeys { get; } =
    [
        TrustedSenderKey,
        FailedSenderKey,
        UnrecognizedSenderKey,
        AttachmentFallbackKey,
        NormalizedFileNameKey,
        AttachmentFileTypeKey,
        HeaderRoleKey("From"),
        HeaderRoleKey("To"),
        HeaderRoleKey("Cc"),
        HeaderRoleKey("Bcc"),
        HeaderRoleKey("SentAt"),
        HeaderRoleKey("ReceivedAt"),
        HeaderRoleKey("MessageId"),
        HeaderRoleKey("InReplyTo"),
        HeaderRoleKey("References"),
    ];

    internal static string HeaderRoleKey(string role) => $"MailMessage.Header.{role}";
}
