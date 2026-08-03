// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using MailFathom.Application.Emails.Summaries;

namespace MailFathom.Mcp.Tools.Summaries;

/// <summary>Publishes what one read email carries besides its body, counted while it was read.</summary>
/// <remarks>
/// It is a type of its own rather than the listing's <see cref="ListedEmailAttachments" /> because the two make
/// different claims about where the numbers come from. A listing publishes what the stored row recorded, which a
/// message stored before extraction reached it does not yet describe; these counts come from the same parse that
/// produced the body and the attachment list, so they cannot disagree with what is published beside them.
/// </remarks>
[Description("What the email carries besides its body, counted while the message was read, so these numbers always describe the attachments listed beside them.")]
internal sealed record EmailAttachmentCounts
{
    /// <summary>Gets how many parts MailFathom's classification counted as attachments.</summary>
    [Description("How many parts MailFathom classified as attachments, which is the length of the attachments list. Inline images and cryptographic signature parts are not counted, so a signed message with a logo in its signature block reports zero.")]
    public required int AttachmentCount { get; init; }

    /// <summary>Gets the decoded size of those attachments together.</summary>
    [Description("The decoded size of those attachments together, in bytes.")]
    public required long TotalSizeBytes { get; init; }

    /// <summary>Gets how many parts an HTML body embeds as resources.</summary>
    [Description("How many parts are resources the HTML body embeds, such as images the message itself references. They are removed from the sanitized HTML, so this count is how a reader learns the message showed embedded images at all.")]
    public required int InlineResourceCount { get; init; }

    /// <summary>Gets whether the body arrived inside a cryptographic envelope.</summary>
    [Description("Whether the body arrived inside a cryptographic envelope and is therefore unreadable by MailFathom, which the body availability states as well.")]
    public required bool IsEncrypted { get; init; }

    /// <summary>Gets whether a signature part is present, verified by nothing.</summary>
    [Description("Whether a cryptographic signature part is present. MailFathom does not verify it, so this states presence and nothing about authenticity.")]
    public required bool CarriesUnverifiedSignature { get; init; }

    /// <summary>Gets whether a TNEF part was recorded without being expanded.</summary>
    [Description("Whether a TNEF winmail.dat part was recorded without being expanded, which means the files inside it are neither counted above nor listed.")]
    public required bool ContainsUnexpandedTnefPart { get; init; }

    /// <summary>Publishes the counts one parse produced.</summary>
    /// <param name="attachments">The attachment summary the read returned.</param>
    /// <returns>The wire representation of <paramref name="attachments" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="attachments" /> is <see langword="null" />.</exception>
    public static EmailAttachmentCounts From(StoredEmailAttachmentSummary attachments)
    {
        ArgumentNullException.ThrowIfNull(attachments);

        return new EmailAttachmentCounts
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
