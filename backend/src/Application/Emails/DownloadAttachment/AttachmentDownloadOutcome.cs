// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Attachments;

namespace MailFathom.Application.Emails.DownloadAttachment;

/// <summary>What asking for one attachment produced: the file, nothing, or a refusal this deployment states.</summary>
/// <remarks>
/// <para>
/// Two absences rather than one, and the reason is what each of them may be told. Every way a download finds nothing —
/// an expired capability, a message this deployment no longer serves, one belonging to somebody else, a damaged local
/// copy, a position the message does not carry — answers uniformly, because telling them apart would let whoever holds
/// a link learn what became of mail they cannot read. A file the screen stopped is the one refusal that may say so:
/// whoever asked was already told by the read beside it that the attachment exists and what it is called, so the answer
/// discloses nothing that description did not.
/// </para>
/// <para>
/// Which of the screen's reasons stopped it is deliberately absent. A category would say the file carries a credential,
/// to somebody this deployment has just decided may not have the file, and an unreadable-file reason would say which
/// shape of unreadable file the screen stops at.
/// </para>
/// </remarks>
public sealed record AttachmentDownloadOutcome
{
    private AttachmentDownloadOutcome(IOpenedEmailAttachment? attachment, bool screened)
    {
        this.Attachment = attachment;
        this.Screened = screened;
    }

    /// <summary>Gets the opened attachment, which the caller owns and must dispose, or <see langword="null" /> where there is nothing to serve.</summary>
    public IOpenedEmailAttachment? Attachment { get; }

    /// <summary>Gets whether this deployment's screen is what stopped the download.</summary>
    /// <remarks>
    /// A screened refusal carries no attachment: whatever was opened to screen it has already been disposed here, so a
    /// caller reading this never holds a file it may not serve.
    /// </remarks>
    public bool Screened { get; }

    /// <summary>Carries the attachment that was opened and, where a screen is active, read and found clean.</summary>
    /// <param name="attachment">The opened attachment.</param>
    /// <returns>The served outcome.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="attachment" /> is <see langword="null" />.</exception>
    public static AttachmentDownloadOutcome Served(IOpenedEmailAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        return new AttachmentDownloadOutcome(attachment, screened: false);
    }

    /// <summary>Reports that there is nothing to serve, whichever of the several reasons it was.</summary>
    /// <returns>The absent outcome.</returns>
    public static AttachmentDownloadOutcome NothingToServe() => new(attachment: null, screened: false);

    /// <summary>Reports a file this deployment screens and would not serve.</summary>
    /// <returns>The screened outcome.</returns>
    public static AttachmentDownloadOutcome ScreenedOut() => new(attachment: null, screened: true);
}
