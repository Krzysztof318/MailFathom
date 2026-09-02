// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Delivery.Drafts;

/// <summary>Names one person a draft is addressed to, and how that address came to be on it.</summary>
/// <param name="Recipient">The address the message would be offered to, and the header it is named in.</param>
/// <param name="Provenance">Where the address came from, which the promotion judges the caller's own word by.</param>
/// <remarks>
/// <para>
/// It is a draft's recipient rather than a send's because of the second member alone. An outgoing record holds
/// <see cref="OutgoingRecipient" /> and nothing more, since a send was governed before it was written down; a draft is
/// written down before any of that governance has run, and the promotion that finally runs it has only what the draft
/// kept to run it on.
/// </para>
/// <para>
/// Nothing here reaches the composed message. The provenance is not a header, not an envelope address, and not
/// something a recipient ever sees — it is this deployment's own account of who chose the address.
/// </para>
/// </remarks>
public readonly record struct MailDraftRecipient(
    OutgoingRecipient Recipient,
    AuthoredRecipientProvenance Provenance);
