// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Delivery;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One person a draft is addressed to, as the current revision names them.</summary>
/// <remarks>
/// Unlike an outgoing recipient this row carries no status and no reply code, because nothing has been offered to
/// anybody: a draft's recipients are what a promotion would build an envelope from, and until then they are only the
/// list its author wrote. A revision replaces them outright rather than amending them, which is what keeps the list the
/// composed message's own.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailDraftRecipientEntity
{
    public Guid MailDraftId { get; set; }

    /// <summary>Gets or sets this recipient's position in the draft's list, which completes the key.</summary>
    /// <remarks>
    /// The ordinal keys the row rather than the address, for the reason it keys an outgoing recipient: an address is
    /// personal data and a key is repeated into every index over the table, and the ordinal keeps the recipients in the
    /// order the composed message writes its headers in.
    /// </remarks>
    public int Ordinal { get; set; }

    public required MailDraftEntity MailDraft { get; set; }

    /// <summary>Gets or sets the address exactly as the author named it.</summary>
    public required string Address { get; set; }

    /// <summary>Gets or sets the contact the address was resolved from, and <see langword="null" /> when the author supplied the address.</summary>
    /// <remarks>No foreign key stands behind it, for the reason none stands behind an outgoing recipient's: it records
    /// which person this draft was addressed by naming, and a contact amended or erased afterwards does not change
    /// that.</remarks>
    public Guid? ContactId { get; set; }

    public OutgoingRecipientRole Role { get; set; }
}
