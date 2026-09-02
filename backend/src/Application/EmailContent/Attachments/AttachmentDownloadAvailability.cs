// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Attachments;

/// <summary>Says whether a read issued a link for an attachment, and why it did not when it did not.</summary>
/// <remarks>
/// The two absences lead a caller to different actions, which is why they are separate values rather than one empty
/// link. A read that did not ask for links gets what it asked for and can ask again; a deployment that issues none
/// cannot be made to by any request, and only an operator can change that.
/// </remarks>
public enum AttachmentDownloadAvailability
{
    /// <summary>A link was minted and is redeemable until it expires.</summary>
    Issued = 0,

    /// <summary>Nothing asked for a link, so the attachment was described and no capability was minted.</summary>
    NotRequested = 1,

    /// <summary>This deployment issues no attachment links at all, because it declares no public address or no key ring.</summary>
    Unavailable = 2,
}
