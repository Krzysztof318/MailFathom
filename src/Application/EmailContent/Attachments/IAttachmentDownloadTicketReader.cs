// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Attachments;

/// <summary>Turns the capability a caller presents back into what it authorizes, or refuses it.</summary>
/// <remarks>
/// The other half of <see cref="IAttachmentDownloadLinkIssuer" />, kept a separate port because the two are reached from
/// opposite directions: one is called while a mailbox is being read, the other by an unauthenticated request that has
/// presented nothing but the capability itself.
/// </remarks>
public interface IAttachmentDownloadTicketReader
{
    /// <summary>Verifies a presented capability and reports what it authorizes.</summary>
    /// <param name="capability">The text the request carried, which is entirely untrusted.</param>
    /// <param name="cancellationToken">Cancels resolving the signing material.</param>
    /// <returns>What the capability authorizes, or <see langword="null" /> when it authorizes nothing.</returns>
    /// <remarks>
    /// A malformed capability, one whose signature does not verify, one naming key material this deployment no longer
    /// holds, and one that has expired are one answer on purpose. Distinguishing them would tell whoever presented a
    /// forgery which part of it was wrong, and the caller acts identically on all of them.
    /// </remarks>
    Task<AttachmentDownloadTicket?> RedeemAsync(string capability, CancellationToken cancellationToken);
}
