// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.EmailContent.Attachments;

/// <summary>Mints the short-lived capabilities a read hands back in place of a file's octets.</summary>
/// <remarks>
/// <para>
/// The port exists so the signature, the key material, and the address stay inside one adapter. A use case decides
/// <em>whether</em> a caller asked for links and <em>which</em> attachments exist; it never composes a URL and never
/// touches key material.
/// </para>
/// <para>
/// One email's links are minted together rather than one at a time, because the key behind the signature is resolved
/// per operation and erased with it: minting per attachment would resolve, decode, and erase the deployment's key
/// material once for every file a message carries.
/// </para>
/// </remarks>
public interface IAttachmentDownloadLinkIssuer
{
    /// <summary>Gets whether this deployment can mint a link at all.</summary>
    /// <remarks>
    /// False when no public address is declared, and false when the deployment configures no key ring to sign with.
    /// Both are configuration rather than failure, so a read reports the attachments and says no link was issued instead
    /// of failing.
    /// </remarks>
    bool CanIssueLinks { get; }

    /// <summary>Mints one link per attachment of one email, in the order the message's structure was walked.</summary>
    /// <param name="storedEmailId">The email the attachments belong to.</param>
    /// <param name="attachmentCount">How many attachments the message carries.</param>
    /// <param name="cancellationToken">Cancels resolving the signing material.</param>
    /// <returns>One link per attachment, positionally matching the read's attachment list, or nothing when <see cref="CanIssueLinks" /> is <see langword="false" />.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="attachmentCount" /> is negative.</exception>
    Task<IReadOnlyList<AttachmentDownloadLink>> IssueAsync(
        StoredEmailId storedEmailId,
        int attachmentCount,
        CancellationToken cancellationToken);
}
