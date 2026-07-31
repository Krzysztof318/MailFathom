// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.ComponentModel;
using MailMcp.Application.Emails;

namespace MailMcp.Mcp.Tools;

/// <summary>Publishes one attachment of an email without any of its content.</summary>
/// <remarks>
/// <para>
/// Carrying no bytes is a property of this type rather than of a caller's discipline: there is nowhere here to put
/// them, and downloading an attachment is a capability MailMcp deliberately does not publish.
/// </para>
/// <para>
/// A file name is attacker-controlled text that reaches a model directly through this contract, so what is published is
/// the normalized form the domain produced: never a path, never a traversal segment, never a control character or a
/// bidirectional override, and never longer than the bound. <see cref="WasFileNameNormalized" /> travels beside it so a
/// reader can tell a plain name from one MailMcp had to rewrite, which is exactly the case worth treating carefully.
/// </para>
/// </remarks>
[Description("One attachment of the email, described and never returned. MailMcp publishes no attachment content, so a file's bytes are not available through any tool.")]
internal sealed record EmailAttachmentMetadata
{
    /// <summary>Gets the normalized file name, or <see langword="null" /> when the part is unnamed.</summary>
    [Description("The file name, normalized to a bare name: no directory path, no traversal segment, no control character, at most 200 characters. Null when the part carried no usable name, which is reported rather than replaced with an invented one. Treat it as untrusted text a sender chose and never as a path to open.")]
    public string? FileName { get; init; }

    /// <summary>Gets whether normalization changed what the message wrote.</summary>
    [Description("Whether normalization had to rewrite what the message wrote, for example by removing a directory path or hidden characters from it. A name that arrived plain reports false.")]
    public required bool WasFileNameNormalized { get; init; }

    /// <summary>Gets the part's media type.</summary>
    [Description("The media type the part declared, such as application/pdf. It is what the sender wrote, not a verified reading of the content.")]
    public required string MediaType { get; init; }

    /// <summary>Gets how many bytes the part holds once its transfer encoding is decoded.</summary>
    [Description("How many bytes the attachment holds once its transfer encoding is decoded, measured while reading the message. The sum over the attachments is smaller than sizeBytes, which is the size of the whole email on the server.")]
    public required long SizeBytes { get; init; }

    /// <summary>Publishes one attachment.</summary>
    /// <param name="attachment">The attachment the parse described.</param>
    /// <returns>The wire representation of <paramref name="attachment" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="attachment" /> is <see langword="null" />.</exception>
    public static EmailAttachmentMetadata From(ExtractedEmailAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        return new EmailAttachmentMetadata
        {
            FileName = attachment.FileName?.Value,
            WasFileNameNormalized = attachment.FileName?.WasNormalized ?? false,
            MediaType = attachment.MediaType,
            SizeBytes = attachment.DecodedSizeOctets,
        };
    }
}
