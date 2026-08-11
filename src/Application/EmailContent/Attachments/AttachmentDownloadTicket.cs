// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.EmailContent.Attachments;

/// <summary>What a redeemed link turned out to authorize: one attachment of one email.</summary>
/// <param name="StoredEmailId">The email the attachment belongs to.</param>
/// <param name="AttachmentPosition">The attachment's zero-based position in the order the message's structure is walked.</param>
/// <remarks>
/// <para>
/// A ticket exists only after a signature verified, so its two values are this deployment's own rather than a caller's.
/// That is what lets the use case behind it look the email up directly instead of validating text again.
/// </para>
/// <para>
/// The position is the identity because it is the only stable one a message's parts have: MIME gives an attachment no
/// identifier, a <c>Content-ID</c> is optional and sender-chosen, and a file name is neither unique nor required. The
/// walk that produces it is the same one <c>get_email_content</c> lists attachments with, over bytes that never change
/// once stored, so the position a link names is the position the listing showed.
/// </para>
/// </remarks>
public sealed record AttachmentDownloadTicket(StoredEmailId StoredEmailId, int AttachmentPosition);
