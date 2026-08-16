// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Delivery;

namespace MailFathom.Infrastructure.Persistence.Entities;

[RequiresIntegrationCoverage]
internal sealed class OutgoingMessageRecipientEntity
{
    public Guid OutgoingMessageId { get; set; }

    /// <summary>Gets or sets this recipient's position in the message's recipient list, which completes the key.</summary>
    /// <remarks>
    /// The ordinal keys the row rather than the address, because an address is personal data and a key is repeated into
    /// every index over the table. It also keeps the recipients in the order the request named them, which is the order
    /// a composed message writes its headers in.
    /// </remarks>
    public int Ordinal { get; set; }

    public required OutgoingMessageEntity OutgoingMessage { get; set; }

    /// <summary>Gets or sets the address exactly as the request named it.</summary>
    /// <remarks>
    /// The comparison form is not stored beside it, unlike a received message's participants. Those are filtered and
    /// grouped by address in queries the database has to answer; these are read back with the record they belong to and
    /// compared in memory against the handful of answers one attempt produced, so a second copy of the same personal
    /// data would buy an index nothing asks for.
    /// </remarks>
    public required string Address { get; set; }

    public OutgoingRecipientRole Role { get; set; }

    public OutgoingRecipientStatus Status { get; set; }

    /// <summary>Gets or sets the reply code the server last answered about this recipient, and <see langword="null" /> while it has answered none.</summary>
    public int? LastReplyCode { get; set; }

    /// <summary>Gets or sets when that answer was recorded, and <see langword="null" /> while there has been none.</summary>
    public DateTimeOffset? AnsweredAt { get; set; }
}
