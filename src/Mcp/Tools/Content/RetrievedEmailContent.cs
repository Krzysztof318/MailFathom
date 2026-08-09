// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using MailFathom.Application.Emails.GetEmailContent;
using MailFathom.Mcp.Tools.Summaries;

namespace MailFathom.Mcp.Tools.Content;

/// <summary>Publishes one email as a reader receives it.</summary>
/// <remarks>
/// <para>
/// The record is part of the tool's structured output, so its shape is the advertised output schema and its descriptions
/// travel with it. It is the use case's projection republished rather than narrowed a second time here, which is what
/// keeps the privacy rules in one place: raw MIME has nowhere to go in the types it is built from, and attachment
/// content reaches it only through the one list a caller has to ask for by name.
/// </para>
/// <para>
/// The email's own identifier is not repeated here. The entry that carries this content names the email it answers for,
/// whether or not there was content to return, so publishing it twice would create two places for one fact.
/// </para>
/// <para>
/// This is the most sensitive result MailFathom publishes. Nothing in it may be logged, and every part of it inherits the
/// classification, retention, access, and erasure constraints of the mail it was read from.
/// </para>
/// </remarks>
[Description("One email read from the local mailbox copy: its normalized headers, its body in the representations that could be produced, and what it carries besides — described always, and with the files themselves when the call asked for them.")]
internal sealed record RetrievedEmailContent
{
    /// <summary>Gets the configured account the email was read from.</summary>
    [Description("The configured MailFathom account identifier the email was synchronized from.")]
    public required string AccountId { get; init; }

    /// <summary>Gets the folder alias the email was read from.</summary>
    [Description("The MailFathom folder alias the email was synchronized under, such as INBOX. This is MailFathom's own name for the folder rather than the path the mail server advertises.")]
    public required string FolderAlias { get; init; }

    /// <summary>Gets the size the mail server reported for the whole email.</summary>
    [Description("The size of the whole email in bytes, as reported by the mail server. No sum over the body and the attachments reproduces it, because it counts the headers and the MIME encoding as well.")]
    public required long SizeBytes { get; init; }

    /// <summary>Gets the normalized headers the email displays.</summary>
    public required NormalizedEmailHeaders Headers { get; init; }

    /// <summary>Gets the body representations, or the reason there are none.</summary>
    public required EmailBodyContent Body { get; init; }

    /// <summary>Gets one entry per attachment when the call asked to describe them, each with its content or the bound that withheld it.</summary>
    [Description("One entry per attachment: what it is called, what it declares itself to be, how large it is, and its content as base64 within this deployment's byte bounds. Null when the call did not set includeAttachmentDetails, which is not the same as an empty list: attachmentCounts states how many attachments exist either way, so call again with the flag set when the files themselves are what you need. Empty when the email carries none, and empty as well when its content was never stored locally, which the body availability states.")]
    public IReadOnlyList<RetrievedEmailAttachment>? Attachments { get; init; }

    /// <summary>Gets the counts for what the email carries besides its body, or <see langword="null" /> when nobody has counted them.</summary>
    [Description("The counts for what the email carries besides its body, returned whether or not attachment details were asked for, or null when nothing has ever read this email's parts — the case of an email whose content the size limit kept out of storage. Null rather than zero, because zero would claim the email carries no attachments and no local copy exists to support that claim.")]
    public EmailAttachmentCounts? AttachmentCounts { get; init; }

    /// <summary>Gets the flags a mail server last showed for the email.</summary>
    public required ObservedRemoteFlags RemoteFlags { get; init; }

    /// <summary>Publishes one email a read returned.</summary>
    /// <param name="content">The email to publish.</param>
    /// <returns>The wire representation of <paramref name="content" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="content" /> is <see langword="null" />.</exception>
    public static RetrievedEmailContent From(ReadEmailContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return new RetrievedEmailContent
        {
            AccountId = content.AccountId.Value,
            FolderAlias = content.FolderAlias.Value,
            SizeBytes = content.SizeOctets,
            Headers = NormalizedEmailHeaders.From(content.Headers),
            Body = EmailBodyContent.From(content.Body),
            Attachments = content.Attachments is { } attachments
                ? [.. attachments.Select(RetrievedEmailAttachment.From)]
                : null,
            AttachmentCounts = content.AttachmentSummary is { } attachmentSummary
                ? EmailAttachmentCounts.From(attachmentSummary)
                : null,
            RemoteFlags = ObservedRemoteFlags.From(content.RemoteFlags),
        };
    }
}
