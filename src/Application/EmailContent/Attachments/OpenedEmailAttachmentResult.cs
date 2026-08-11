// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Attachments;

/// <summary>What opening one attachment of a stored message produced.</summary>
/// <param name="Attachment">The opened attachment, which the caller owns and must dispose, or <see langword="null" /> when none was opened.</param>
/// <param name="ContentIsUnreadable">Whether the stored bytes could not be parsed at all, as opposed to naming no such attachment.</param>
/// <remarks>
/// The two absences are separated for the reason a read separates them: bytes that no longer parse are a damaged local
/// copy the caller records a repair request for, while a position the message does not have is an ordinary refusal of a
/// capability that has outlived the message it described. Neither reaches the reader as anything but one refusal.
/// </remarks>
public sealed record OpenedEmailAttachmentResult(IOpenedEmailAttachment? Attachment, bool ContentIsUnreadable)
{
    /// <summary>Reports that the stored bytes yielded nothing that could be parsed.</summary>
    /// <returns>The unreadable result.</returns>
    public static OpenedEmailAttachmentResult Unreadable() => new(Attachment: null, ContentIsUnreadable: true);

    /// <summary>Reports that the message parsed and carries no attachment at the named position.</summary>
    /// <returns>The absent result.</returns>
    public static OpenedEmailAttachmentResult NoSuchAttachment() => new(Attachment: null, ContentIsUnreadable: false);

    /// <summary>Carries the attachment that was opened.</summary>
    /// <param name="attachment">The opened attachment.</param>
    /// <returns>The opened result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="attachment" /> is <see langword="null" />.</exception>
    public static OpenedEmailAttachmentResult Opened(IOpenedEmailAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        return new OpenedEmailAttachmentResult(attachment, ContentIsUnreadable: false);
    }
}
