// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using MailFathom.Application.Emails.Summaries;

namespace MailFathom.Mcp.Tools.Summaries;

/// <summary>Publishes what one listed email carries besides its body.</summary>
/// <remarks>
/// The counts stay separate on the wire for the reason the stored row keeps them separate: collapsing them would make
/// every signed message and every message with a logo in its signature block look like mail with a file attached. File
/// names, media types, and per-attachment sizes are absent because a file name is mail content and is not persisted.
/// </remarks>
[Description("What the email carries besides its body, as far as the stored row records it. File names and media types are not returned; a reader that needs them parses the stored raw content.")]
internal sealed record ListedEmailAttachments
{
    /// <summary>Gets how many parts MailFathom's classification counted as attachments.</summary>
    [Description("How many parts MailFathom classified as attachments. Inline images and cryptographic signature parts are not counted, so a signed message with a logo in its signature block reports zero.")]
    public required int AttachmentCount { get; init; }

    /// <summary>Gets the decoded size of those attachments together.</summary>
    [Description("The decoded size of those attachments together, in bytes.")]
    public required long TotalSizeBytes { get; init; }

    /// <summary>Gets how many parts an HTML body embeds as resources.</summary>
    [Description("How many parts are resources an HTML body embeds, such as images the message itself references, rather than files a person would open.")]
    public required int InlineResourceCount { get; init; }

    /// <summary>Gets whether the body arrived inside a cryptographic envelope.</summary>
    [Description("Whether the body arrived inside a cryptographic envelope and is therefore unreadable by MailFathom, which also means no text was extracted from it.")]
    public required bool IsEncrypted { get; init; }

    /// <summary>Gets whether a signature part is present, verified by nothing.</summary>
    [Description("Whether a cryptographic signature part is present. MailFathom does not verify it, so this states presence and nothing about authenticity.")]
    public required bool CarriesUnverifiedSignature { get; init; }

    /// <summary>Gets whether a TNEF part was recorded without being expanded.</summary>
    [Description("Whether a TNEF winmail.dat part was recorded without being expanded, which means attachments inside it are not counted above.")]
    public required bool ContainsUnexpandedTnefPart { get; init; }

    /// <summary>Publishes one attachment summary.</summary>
    /// <param name="attachments">The attachment summary the email carried.</param>
    /// <returns>The wire representation of <paramref name="attachments" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="attachments" /> is <see langword="null" />.</exception>
    public static ListedEmailAttachments From(StoredEmailAttachmentSummary attachments)
    {
        ArgumentNullException.ThrowIfNull(attachments);

        return new ListedEmailAttachments
        {
            AttachmentCount = attachments.AttachmentCount,
            TotalSizeBytes = attachments.TotalSizeOctets,
            InlineResourceCount = attachments.InlineResourceCount,
            IsEncrypted = attachments.IsEncrypted,
            CarriesUnverifiedSignature = attachments.CarriesUnverifiedSignature,
            ContainsUnexpandedTnefPart = attachments.ContainsUnexpandedTnefPart,
        };
    }
}
