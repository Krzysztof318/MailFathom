// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Rendering;

/// <summary>How many octets one attachment may return, and which bound decided it.</summary>
/// <param name="MaxOctets">The greatest number of decoded octets this attachment may return.</param>
/// <param name="AvailabilityWhenExceeded">What to report when the part decodes to more than this allows.</param>
/// <remarks>
/// The two travel together for the reason <see cref="EmailBodyCharacterAllowance" /> carries its truncation: which of
/// the two bounds applied is only knowable where the smaller of them is chosen, and re-deriving it afterwards from the
/// sizes would guess at what this type already knows.
/// </remarks>
public readonly record struct EmailAttachmentContentAllowance(
    int MaxOctets,
    EmailAttachmentContentAvailability AvailabilityWhenExceeded)
{
    /// <summary>Chooses the allowance one attachment receives from the two bounds that apply to it.</summary>
    /// <param name="maxOctetsPerAttachment">The bound every attachment is subject to, whatever else the read allows.</param>
    /// <param name="remainingOctetsForRead">What the whole read's attachment budget still allows when this attachment is reached.</param>
    /// <returns>The smaller of the two bounds, carrying the identity of whichever one it was.</returns>
    /// <remarks>
    /// A budget already spent yields an allowance of zero rather than a negative one, so an attachment reached after the
    /// budget ran out is described with no content and the budget named, instead of failing the read.
    /// </remarks>
    public static EmailAttachmentContentAllowance Of(int maxOctetsPerAttachment, int remainingOctetsForRead) =>
        remainingOctetsForRead < maxOctetsPerAttachment
            ? new EmailAttachmentContentAllowance(
                Math.Max(remainingOctetsForRead, 0),
                EmailAttachmentContentAvailability.ReadByteBudgetExhausted)
            : new EmailAttachmentContentAllowance(
                maxOctetsPerAttachment,
                EmailAttachmentContentAvailability.ExceededAttachmentByteLimit);
}
