// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Attachments;

/// <summary>How an attachment's content is reachable from one read, or why it is not reachable from this one.</summary>
/// <remarks>
/// <para>
/// A read never carries a file's octets. What it can carry is a capability to fetch them, and this is either that
/// capability or the reason none was minted. The distinction is the whole point: describing an attachment costs nothing
/// and happens on every read, while minting a link is what a caller asks for and what a deployment has to be configured
/// to do.
/// </para>
/// <para>
/// The link is a bearer capability rather than mail content, and it is treated as the more sensitive of the two: mail
/// content requires the reader to already hold it, and this obtains some for whoever holds the URL.
/// </para>
/// </remarks>
public sealed record AttachmentDownload
{
    private AttachmentDownload(AttachmentDownloadAvailability availability, AttachmentDownloadLink? link)
    {
        this.Availability = availability;
        this.Link = link;
    }

    /// <summary>Gets whether a link was minted, and why it was not when it was not.</summary>
    public AttachmentDownloadAvailability Availability { get; }

    /// <summary>Gets the minted link, which is present for <see cref="AttachmentDownloadAvailability.Issued" /> and absent otherwise.</summary>
    public AttachmentDownloadLink? Link { get; }

    /// <summary>Gets the download of an attachment nothing asked to fetch.</summary>
    public static AttachmentDownload NotRequested { get; } =
        new(AttachmentDownloadAvailability.NotRequested, link: null);

    /// <summary>Gets the download of an attachment this deployment can mint no link for.</summary>
    public static AttachmentDownload Unavailable { get; } =
        new(AttachmentDownloadAvailability.Unavailable, link: null);

    /// <summary>Carries the link a read minted for one attachment.</summary>
    /// <param name="link">The minted capability.</param>
    /// <returns>The issued download.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="link" /> is <see langword="null" />.</exception>
    public static AttachmentDownload Issued(AttachmentDownloadLink link)
    {
        ArgumentNullException.ThrowIfNull(link);

        return new AttachmentDownload(AttachmentDownloadAvailability.Issued, link);
    }
}
