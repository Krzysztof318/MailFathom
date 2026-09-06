// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Attachments;

/// <summary>One stored message parsed once, holding every attachment position open for as long as the walk lasts.</summary>
/// <remarks>
/// <para>
/// The download route opens one position per request and pays for one parse to do it. A pass that reads every
/// attachment of a message cannot pay that per position: parsing raw MIME is the most expensive local work in an
/// account run, and repeating it once per attachment would multiply the run's cost by the attachment count for no
/// answer that changes. This is the shape that pays for it once.
/// </para>
/// <para>
/// The walk owns the parse and every attachment taken from it, which is what makes the parse shared rather than copied.
/// An attachment it hands back is a view over that parse, so disposing one releases nothing and the walk itself is what
/// a caller disposes — and never before the last attachment it opened has been read.
/// </para>
/// </remarks>
public interface IOpenedEmailAttachmentWalk : IAsyncDisposable
{
    /// <summary>Gets how many attachments the message's structure was walked into.</summary>
    /// <remarks>
    /// Read rather than assumed from the count a stored row carries: the two disagree whenever the row was written by
    /// an older reading, and the walk is what a position means.
    /// </remarks>
    int Count { get; }

    /// <summary>Opens the attachment at one position of the message this walk parsed.</summary>
    /// <param name="attachmentPosition">The zero-based position in the walk order.</param>
    /// <param name="cancellationToken">Cancels the read of that part.</param>
    /// <returns>The opened attachment, or the reason there is none to open.</returns>
    /// <remarks>
    /// The same result the single-position read answers with, and for the same reason: a part whose octets no longer
    /// decode is a damaged local copy the caller records a repair request for, while a position past the end is the
    /// walk disagreeing with whatever asked for it.
    /// </remarks>
    Task<OpenedEmailAttachmentResult> OpenAsync(int attachmentPosition, CancellationToken cancellationToken);
}
