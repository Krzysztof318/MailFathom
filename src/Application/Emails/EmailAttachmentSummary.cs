// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

namespace MailFathom.Application.Emails;

/// <summary>Summarizes what a message carries besides its body.</summary>
/// <remarks>
/// What counts as an attachment is MailFathom's rule rather than a mail library's default, because the count is shown to a
/// caller and filtered on. A signature part, an embedded image a body references, and the members of an alternative
/// body are all things a mailbox owner does not call attachments, and each of them arrives with a
/// <c>Content-Disposition</c> header that a disposition-driven rule would count.
/// </remarks>
public sealed record EmailAttachmentSummary
{
    private EmailAttachmentSummary(
        IReadOnlyList<ExtractedEmailAttachment> attachments,
        long totalSizeOctets,
        int inlineResourceCount,
        bool isEncrypted,
        bool carriesUnverifiedSignature,
        bool containsUnexpandedTnefPart)
    {
        this.Attachments = attachments;
        this.TotalSizeOctets = totalSizeOctets;
        this.InlineResourceCount = inlineResourceCount;
        this.IsEncrypted = isEncrypted;
        this.CarriesUnverifiedSignature = carriesUnverifiedSignature;
        this.ContainsUnexpandedTnefPart = containsUnexpandedTnefPart;
    }

    /// <summary>Gets the attachments, in the order the message's structure was walked.</summary>
    public IReadOnlyList<ExtractedEmailAttachment> Attachments { get; }

    /// <summary>Gets how many attachments the message carries.</summary>
    public int AttachmentCount => this.Attachments.Count;

    /// <summary>Gets whether the message has attachments, which an inline-only or signature-only message does not.</summary>
    public bool HasAttachments => this.Attachments.Count > 0;

    /// <summary>Gets the decoded octets of every attachment together.</summary>
    public long TotalSizeOctets { get; }

    /// <summary>Gets how many parts are resources an HTML body embeds rather than files a person would open.</summary>
    public int InlineResourceCount { get; }

    /// <summary>Gets whether the message body arrived inside a cryptographic envelope and is therefore unreadable here.</summary>
    /// <remarks>Decrypting it is out of scope; the marker exists so a reader can say why a body is absent instead of recording an empty one.</remarks>
    public bool IsEncrypted { get; }

    /// <summary>Gets whether the message carries a signature part whose authenticity nothing has checked.</summary>
    /// <remarks>
    /// The name states presence only, deliberately. Anyone can attach a signature-typed part, and no verification runs
    /// here, so a marker named after signing would be read downstream as an authenticity result this never established.
    /// </remarks>
    public bool CarriesUnverifiedSignature { get; }

    /// <summary>Gets whether the message carries a TNEF <c>winmail.dat</c> part that was recorded rather than expanded.</summary>
    public bool ContainsUnexpandedTnefPart { get; }

    /// <summary>Gets the summary of a message that carries nothing besides its body.</summary>
    public static EmailAttachmentSummary None { get; } = new(
        attachments: new List<ExtractedEmailAttachment>().AsReadOnly(),
        totalSizeOctets: 0,
        inlineResourceCount: 0,
        isEncrypted: false,
        carriesUnverifiedSignature: false,
        containsUnexpandedTnefPart: false);

    /// <summary>Builds the summary of one classified message.</summary>
    /// <param name="attachments">The parts classified as attachments.</param>
    /// <param name="inlineResourceCount">How many parts an HTML body embeds.</param>
    /// <param name="isEncrypted">Whether the body arrived inside a cryptographic envelope.</param>
    /// <param name="carriesUnverifiedSignature">Whether a signature part is present, verified by nothing.</param>
    /// <param name="containsUnexpandedTnefPart">Whether a TNEF part was recorded without being expanded.</param>
    /// <returns>The summary.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="attachments" /> is <see langword="null" />.</exception>
    public static EmailAttachmentSummary Create(
        IEnumerable<ExtractedEmailAttachment> attachments,
        int inlineResourceCount,
        bool isEncrypted,
        bool carriesUnverifiedSignature,
        bool containsUnexpandedTnefPart)
    {
        ArgumentNullException.ThrowIfNull(attachments);

        // Wrapped rather than left as the array a collection expression produces: an IReadOnlyList backed directly by
        // an array can be cast back to that array and written through, which would leave TotalSizeOctets describing a
        // list that no longer exists.
        IReadOnlyList<ExtractedEmailAttachment> materializedAttachments = new List<ExtractedEmailAttachment>(attachments).AsReadOnly();

        return new EmailAttachmentSummary(
            materializedAttachments,
            materializedAttachments.Sum(attachment => attachment.DecodedSizeOctets),
            inlineResourceCount,
            isEncrypted,
            carriesUnverifiedSignature,
            containsUnexpandedTnefPart);
    }
}
