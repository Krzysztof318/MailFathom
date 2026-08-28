// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Client.Backend.Mail;
using MailFathom.Client.Presentation.Messages;
using Microsoft.Extensions.Localization;

namespace MailFathom.Client.Presentation.Spaces.Mail.Reading;

/// <summary>One message as the reading pane draws everything around its body.</summary>
public sealed record MailMessageReading(
    string Subject,
    IReadOnlyList<MailMessageHeaderRow> Headers,
    bool ShowsSenderNotice,
    bool WarnsAboutSender,
    string SenderNotice,
    IReadOnlyList<MailAttachmentRow> Attachments)
{
    /// <summary>Whether the sender notice is the positive statement that this deployment trusts the authenticated author.</summary>
    public bool ShowsTrustedSender => this.ShowsSenderNotice && !this.WarnsAboutSender;

    /// <summary>Whether the sender notice is a warning.</summary>
    public bool ShowsSenderWarning => this.ShowsSenderNotice && this.WarnsAboutSender;

    /// <summary>Whether the message carries attachments to list.</summary>
    public bool HasAttachments => this.Attachments.Count > 0;

    internal static MailMessageReading Of(
        DeploymentMailMessageDetail message,
        IReadOnlyDictionary<int, MailAttachmentStanding> downloads,
        IStringLocalizer words)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(downloads);
        ArgumentNullException.ThrowIfNull(words);

        var headers = message.Headers.Participants
            .Select(participant => new MailMessageHeaderRow(
                words[MailMessageWords.HeaderRoleKey(participant.Role)],
                AddressOf(participant)))
            .ToList();

        Add(headers, "SentAt", message.Headers.SentAt?.ToString("f", CultureInfo.CurrentCulture), words);
        Add(headers, "ReceivedAt", message.Headers.ReceivedAt?.ToString("f", CultureInfo.CurrentCulture), words);
        Add(headers, "MessageId", message.Headers.MessageId, words);
        Add(headers, "InReplyTo", message.Headers.InReplyTo, words);
        Add(
            headers,
            "References",
            message.Headers.References.Count is 0 ? null : string.Join(", ", message.Headers.References),
            words);

        var from = message.Headers.Participants.FirstOrDefault(
            static participant => string.Equals(participant.Role, "From", StringComparison.OrdinalIgnoreCase));

        var sender = SenderNoticeOf(message.Sender, from?.Address, words);

        return new MailMessageReading(
            message.Headers.Subject ?? words[MessageWords.NoSubjectKey],
            headers,
            sender.Show,
            sender.Warn,
            sender.Text,
            [.. message.Attachments.Select(attachment => MailAttachmentRow.Of(
                message.StoredEmailId,
                attachment,
                downloads.GetValueOrDefault(attachment.Position),
                words))]);
    }

    private static void Add(
        List<MailMessageHeaderRow> headers,
        string role,
        string? value,
        IStringLocalizer words)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            headers.Add(new MailMessageHeaderRow(words[MailMessageWords.HeaderRoleKey(role)], value));
        }
    }

    private static string AddressOf(DeploymentMailParticipant participant) =>
        string.IsNullOrWhiteSpace(participant.DisplayName)
            ? participant.Address
            : $"{participant.DisplayName} <{participant.Address}>";

    private static (bool Show, bool Warn, string Text) SenderNoticeOf(
        DeploymentMailSenderVerdict sender,
        string? displayedAuthor,
        IStringLocalizer words)
    {
        if (string.Equals(sender.AuthorAuthentication, "Authenticated", StringComparison.Ordinal)
            && string.Equals(sender.DeploymentTrust, "Unknown", StringComparison.Ordinal))
        {
            return (false, false, string.Empty);
        }

        if (AuthenticatedAuthorOf(displayedAuthor) is { } authenticatedAuthor
            && string.Equals(sender.AuthorAuthentication, "Authenticated", StringComparison.Ordinal)
            && string.Equals(sender.DeploymentTrust, "Trusted", StringComparison.Ordinal))
        {
            return (true, false, words[MailMessageWords.TrustedSenderKey, authenticatedAuthor]);
        }

        if (!string.IsNullOrWhiteSpace(displayedAuthor)
            && string.Equals(sender.AuthorAuthentication, "Failed", StringComparison.Ordinal))
        {
            return (true, true, words[MailMessageWords.FailedSenderKey, displayedAuthor]);
        }

        if (string.Equals(sender.AuthorAuthentication, "NotEstablished", StringComparison.Ordinal)
            && string.Equals(sender.DeploymentTrust, "Unknown", StringComparison.Ordinal))
        {
            return (false, false, string.Empty);
        }

        return (true, true, words[MailMessageWords.UnrecognizedSenderKey]);
    }

    private static string? AuthenticatedAuthorOf(string? displayedAddress)
    {
        var separator = displayedAddress?.LastIndexOf('@') ?? -1;

        return separator >= 0 && separator < displayedAddress!.Length - 1
            ? displayedAddress[(separator + 1)..]
            : null;
    }
}

/// <summary>One header line, already labelled in the language the application is being read in.</summary>
public sealed record MailMessageHeaderRow(string Role, string Value);

/// <summary>The identity passed to attachment commands.</summary>
public sealed record MailAttachmentRequest(Guid Message, int Position);

/// <summary>One attachment as the pane draws it before, during, and after a download.</summary>
public sealed record MailAttachmentRow(
    MailAttachmentRequest Request,
    string FileName,
    string MediaType,
    string Size,
    bool WasFileNameNormalized,
    string NormalizedFileNameNotice,
    bool CanDownload,
    bool CanCancel,
    bool DownloadFailed,
    bool Downloaded)
{
    internal static MailAttachmentRow Of(
        Guid message,
        DeploymentMailAttachment attachment,
        MailAttachmentStanding standing,
        IStringLocalizer words) => new(
        new MailAttachmentRequest(message, attachment.Position),
        attachment.FileName ?? words[MailMessageWords.AttachmentFallbackKey, attachment.Position + 1],
        attachment.MediaType,
        MailSize.Format(attachment.SizeOctets),
        attachment.WasFileNameNormalized,
        words[MailMessageWords.NormalizedFileNameKey],
        standing is MailAttachmentStanding.None,
        standing is MailAttachmentStanding.Downloading,
        standing is MailAttachmentStanding.Failed,
        standing is MailAttachmentStanding.Downloaded);
}

/// <summary>What the latest attempt to save one attachment is doing.</summary>
internal enum MailAttachmentStanding
{
    None = 0,
    Downloading = 1,
    Failed = 2,
    Downloaded = 3,
}

/// <summary>Writes an octet count at the scale a person deciding whether to download it needs.</summary>
internal static class MailSize
{
    internal static string Format(long octets) => octets switch
    {
        < 1024 => string.Create(CultureInfo.CurrentCulture, $"{octets:N0} B"),
        < 1024 * 1024 => string.Create(CultureInfo.CurrentCulture, $"{octets / 1024d:N1} KB"),
        < 1024L * 1024 * 1024 => string.Create(CultureInfo.CurrentCulture, $"{octets / (1024d * 1024):N1} MB"),
        _ => string.Create(CultureInfo.CurrentCulture, $"{octets / (1024d * 1024 * 1024):N1} GB"),
    };
}
