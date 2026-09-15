// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Mail.Delivery.Filing;

/// <summary>A message read and placed for filing into a held account's local folder, before the transaction that files it.</summary>
/// <param name="Account">The account the message is filed for.</param>
/// <param name="Filing">Which place the message is filed into, which carries its role and its flags.</param>
/// <param name="Binding">The folder binding of the source folder playing that role.</param>
/// <param name="Metadata">What was read out of the message's MIME, or <see langword="null" /> when nothing could be.</param>
/// <param name="Content">The payload, already placed where the content store keeps it.</param>
/// <remarks>
/// It is prepared outside the transaction for the reason synchronization places content outside one: under the object
/// backend the placement reaches the endpoint. A copy whose transaction never commits leaves an object nothing points at,
/// which reclamation removes.
/// </remarks>
public sealed record LocalMailCopy(
    MailAccountId Account,
    OutgoingMailFiling Filing,
    MailFolderResolutionId Binding,
    ExtractedEmailMetadata? Metadata,
    PlacedEmailContent Content);
