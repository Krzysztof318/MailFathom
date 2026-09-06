// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Attachments;

/// <summary>What parsing one stored message for a walk over all of its attachments produced.</summary>
/// <param name="Walk">The walk, which the caller owns and must dispose, or <see langword="null" /> when the bytes yielded none.</param>
/// <param name="ContentIsUnreadable">Whether the stored bytes could not be parsed at all.</param>
/// <remarks>
/// One absence rather than the two <see cref="OpenedEmailAttachmentResult" /> separates, because a walk names no
/// position: bytes that no longer parse are the only way this fails, and a message carrying no attachment is a walk of
/// no positions rather than a refusal.
/// </remarks>
public sealed record OpenedEmailAttachmentWalkResult(IOpenedEmailAttachmentWalk? Walk, bool ContentIsUnreadable)
{
    /// <summary>Reports that the stored bytes yielded nothing that could be parsed.</summary>
    /// <returns>The unreadable result.</returns>
    public static OpenedEmailAttachmentWalkResult Unreadable() => new(Walk: null, ContentIsUnreadable: true);

    /// <summary>Carries the walk that was opened.</summary>
    /// <param name="walk">The walk over the parsed message.</param>
    /// <returns>The opened result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="walk" /> is <see langword="null" />.</exception>
    public static OpenedEmailAttachmentWalkResult Opened(IOpenedEmailAttachmentWalk walk)
    {
        ArgumentNullException.ThrowIfNull(walk);

        return new OpenedEmailAttachmentWalkResult(walk, ContentIsUnreadable: false);
    }
}
