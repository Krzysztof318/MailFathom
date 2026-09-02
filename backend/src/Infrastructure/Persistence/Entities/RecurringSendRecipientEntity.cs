// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Delivery;

namespace MailFathom.Infrastructure.Persistence.Entities;

[RequiresIntegrationCoverage]
internal sealed class RecurringSendRecipientEntity
{
    public Guid RecurringSendId { get; set; }

    /// <summary>Gets or sets this recipient's position in the declaration's recipient list, which completes the key.</summary>
    /// <remarks>The ordinal keys the row rather than the address, for the reason an outgoing record's recipients are keyed that way: an address is personal data and a key is repeated into every index over the table.</remarks>
    public int Ordinal { get; set; }

    public required RecurringSendEntity RecurringSend { get; set; }

    /// <summary>Gets or sets the address every occurrence is offered to, exactly as the declaration named it.</summary>
    public required string Address { get; set; }

    /// <summary>Gets or sets the contact the address was resolved from, and <see langword="null" /> when the author supplied the address.</summary>
    /// <remarks>No foreign key stands behind it, for the reason an outgoing recipient's carries none: it records how the address came to be on the declaration, and a contact amended or erased afterwards must not change who this message goes to.</remarks>
    public Guid? ContactId { get; set; }

    public OutgoingRecipientRole Role { get; set; }

    /// <summary>Gets or sets the PostgreSQL <c>xmin</c> token this row's optimistic concurrency is detected through.</summary>
    /// <remarks>
    /// A row here has no state of its own that changes — unlike an outgoing recipient, nothing is ever answered about
    /// it — so the token guards the one thing that can happen to it, which is two callers declaring the same repetition
    /// at once and one of them losing.
    /// </remarks>
    public uint ConcurrencyVersion { get; set; }
}
