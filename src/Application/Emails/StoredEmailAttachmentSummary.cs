// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Application.Emails;

/// <summary>Reports what one stored email carries besides its body, as far as the persisted row records it.</summary>
/// <param name="AttachmentCount">How many parts the classification counted as attachments.</param>
/// <param name="TotalSizeOctets">The decoded octets of those attachments together.</param>
/// <param name="InlineResourceCount">How many parts are resources an HTML body embeds rather than files a person would open.</param>
/// <param name="IsEncrypted">Whether the body arrived inside a cryptographic envelope and is therefore unreadable here.</param>
/// <param name="CarriesUnverifiedSignature">Whether a signature part is present, verified by nothing.</param>
/// <param name="ContainsUnexpandedTnefPart">Whether a TNEF <c>winmail.dat</c> part was recorded without being expanded.</param>
/// <remarks>
/// <para>
/// This is the indexable part of what MIME extraction found, and only that. The per-attachment list of file names, media
/// types, and sizes is deliberately not persisted — file names are mail content — so a reader that needs it parses the
/// stored raw MIME instead. A summary is therefore not a shortened <see cref="EmailAttachmentSummary" /> but the shape
/// the row can answer from.
/// </para>
/// <para>
/// The counts are separate values because they answer different questions. A message whose only non-body parts are
/// inline resources or a signature carries no attachments, so collapsing them into one number would make every signed
/// message and every message with a logo in its signature block look like mail with a file attached.
/// </para>
/// </remarks>
public sealed record StoredEmailAttachmentSummary(
    int AttachmentCount,
    long TotalSizeOctets,
    int InlineResourceCount,
    bool IsEncrypted,
    bool CarriesUnverifiedSignature,
    bool ContainsUnexpandedTnefPart)
{
    /// <summary>Gets the summary of an email that carries nothing besides its body.</summary>
    public static StoredEmailAttachmentSummary None { get; } = new(
        AttachmentCount: 0,
        TotalSizeOctets: 0,
        InlineResourceCount: 0,
        IsEncrypted: false,
        CarriesUnverifiedSignature: false,
        ContainsUnexpandedTnefPart: false);

    /// <summary>Gets whether the email has attachments, which an inline-only or signature-only message does not.</summary>
    public bool HasAttachments => this.AttachmentCount > 0;
}
