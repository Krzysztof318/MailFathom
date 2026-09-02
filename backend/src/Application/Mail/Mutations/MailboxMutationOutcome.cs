// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Mail.Mutations;

/// <summary>Reports what asking for one mutation did, and where the record of it can be read.</summary>
/// <param name="RecordId">The durable record the request was written to, which is the same for every repeat of it.</param>
/// <param name="Status">What this call did.</param>
/// <param name="Placement">Where the destination folder put the email, as far as the record says.</param>
/// <remarks>
/// The record identifier is part of the outcome rather than something a caller looks up afterwards, because it is the
/// only stable name for a change that is in flight: the occurrence it targets can move, and the request that produced it
/// says nothing about how far it got.
/// </remarks>
public sealed record MailboxMutationOutcome(
    MailboxMutationRecordId RecordId,
    MailboxMutationStatus Status,
    RemoteEmailPlacement Placement);
