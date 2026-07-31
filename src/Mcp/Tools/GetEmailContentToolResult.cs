// Copyright © 2026 Krzysztof Kasprowicz

using System.ComponentModel;
using MailMcp.Application.Emails.GetEmailContent;

namespace MailMcp.Mcp.Tools;

/// <summary>Publishes one email as a reader receives it.</summary>
/// <remarks>
/// <para>
/// The record is the tool's structured output, so its shape is the advertised output schema and its descriptions travel
/// with it. It is the use case's projection republished rather than narrowed a second time here, which is what keeps
/// the privacy rules in one place: the projection carries no attachment bytes and no raw MIME because the types it is
/// built from have nowhere to put them.
/// </para>
/// <para>
/// This is the most sensitive result MailMcp publishes. Nothing in it may be logged, and every part of it inherits the
/// classification, retention, access, and erasure constraints of the mail it was read from.
/// </para>
/// </remarks>
[Description("One email read from the local mailbox copy: its normalized headers, its body in the representations that could be produced, and a description of what it carries besides. Attachment content is never included.")]
internal sealed record GetEmailContentToolResult
{
    /// <summary>Gets the stable local identity of the email, which is the one the request named.</summary>
    [Description("The stable local identifier of the email, which is the one the request named.")]
    public required string StoredEmailId { get; init; }

    /// <summary>Gets the configured account the email was read from.</summary>
    [Description("The configured MailMcp account identifier the email was synchronized from.")]
    public required string AccountId { get; init; }

    /// <summary>Gets the folder alias the email was read from.</summary>
    [Description("The MailMcp folder alias the email was synchronized under, such as INBOX. This is MailMcp's own name for the folder rather than the path the mail server advertises.")]
    public required string FolderAlias { get; init; }

    /// <summary>Gets the size the mail server reported for the whole email.</summary>
    [Description("The size of the whole email in bytes, as reported by the mail server. No sum over the body and the attachments reproduces it, because it counts the headers and the MIME encoding as well.")]
    public required long SizeBytes { get; init; }

    /// <summary>Gets the normalized headers the email displays.</summary>
    public required NormalizedEmailHeaders Headers { get; init; }

    /// <summary>Gets the body representations, or the reason there are none.</summary>
    public required EmailBodyContent Body { get; init; }

    /// <summary>Gets one entry per attachment, and never any of their bytes.</summary>
    [Description("One entry per attachment, describing it without carrying any of its content. Empty when the email carries none, and empty as well when its content was never stored locally, which the body availability states.")]
    public required IReadOnlyList<EmailAttachmentMetadata> Attachments { get; init; }

    /// <summary>Gets the counts for what the email carries besides its body, or <see langword="null" /> when nobody has counted them.</summary>
    [Description("The counts for what the email carries besides its body, or null when nothing has ever read this email's parts — the case of an email whose content the size limit kept out of storage. Null rather than zero, because zero would claim the email carries no attachments and no local copy exists to support that claim.")]
    public EmailAttachmentCounts? AttachmentCounts { get; init; }

    /// <summary>Gets the flags a mail server last showed for the email.</summary>
    public required ObservedRemoteFlags RemoteFlags { get; init; }

    /// <summary>Publishes the email a read returned.</summary>
    /// <param name="result">The email to publish.</param>
    /// <returns>The wire representation of <paramref name="result" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="result" /> is <see langword="null" />.</exception>
    public static GetEmailContentToolResult From(GetEmailContentResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new GetEmailContentToolResult
        {
            StoredEmailId = result.StoredEmailId.ToString(),
            AccountId = result.AccountId.Value,
            FolderAlias = result.FolderAlias.Value,
            SizeBytes = result.SizeOctets,
            Headers = NormalizedEmailHeaders.From(result.Headers),
            Body = EmailBodyContent.From(result.Body),
            Attachments = [.. result.Attachments.Select(EmailAttachmentMetadata.From)],
            AttachmentCounts = result.AttachmentSummary is { } attachmentSummary
                ? EmailAttachmentCounts.From(attachmentSummary)
                : null,
            RemoteFlags = ObservedRemoteFlags.From(result.RemoteFlags),
        };
    }
}
