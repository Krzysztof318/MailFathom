// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent.Derivation;

namespace MailFathom.Application.Emails.AttachmentText;

/// <summary>What reading one message's attachments produced, and the configuration it was read under.</summary>
/// <remarks>
/// The stamp travels with the readings rather than being asked for again at the write, and it is taken before the scan
/// rather than after it — the same ordering the body's own redaction keeps, and for the same reason. A posture
/// republished while a message is being read then leaves its rows stamped with the older configuration, which reads as
/// stale and is re-derived; a stamp taken at the write would record a configuration the words never went through, and
/// the rows would never be revisited.
/// </remarks>
/// <param name="Attachments">What each attachment yielded, in walk order, which is empty for a message that yielded nothing.</param>
/// <param name="RedactedUnder">What the owner's mail was redacted under, or <see langword="null" /> where nothing scans it.</param>
public sealed record EmailAttachmentTextDerivation(
    IReadOnlyList<DerivedAttachmentText> Attachments,
    SensitiveContentDerivationStamp? RedactedUnder)
{
    /// <summary>Gets whether any attachment contributed words to cut passages from.</summary>
    public bool YieldedText => this.Attachments.Any(attachment => attachment.HasText);
}
