// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Attachments;

/// <summary>One minted capability to fetch one attachment, and the instant it stops being one.</summary>
/// <param name="Address">The absolute address the attachment is fetched from, carrying the signed capability.</param>
/// <param name="ExpiresAt">When the link stops being redeemable.</param>
/// <remarks>
/// <para>
/// The address is a bearer capability written into a URL, so it reaches whoever holds it and is copied wherever URLs are
/// copied. What bounds it is that it names exactly one attachment of one email, expires within minutes, and resolves
/// through the live store when it is redeemed — a link cannot outlive the deletion of the message it points at.
/// </para>
/// <para>
/// It is neither logged nor persisted. The address is not itself mail content, but it is an unauthenticated way to
/// obtain some, which makes a log line carrying one worse than a log line carrying the file name it points at.
/// </para>
/// </remarks>
public sealed record AttachmentDownloadLink(Uri Address, DateTimeOffset ExpiresAt);
