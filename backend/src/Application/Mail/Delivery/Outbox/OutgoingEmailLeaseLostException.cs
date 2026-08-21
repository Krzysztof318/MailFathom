// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Delivery;
using MailFathom.Domain.Failures;

namespace MailFathom.Application.Mail.Delivery.Outbox;

/// <summary>Indicates that the record an attempt was writing to is held by a later attempt.</summary>
/// <remarks>
/// <para>
/// It is raised where a write is refused rather than where the lease expired, because nothing observes the expiry: a
/// lease runs out on the database's clock and the attempt that held it finds out by being told its write does not
/// apply. What that means for the caller is that the outcome it was about to record is not this record's outcome — the
/// attempt holding the lease now is the one whose answer counts — so it is dropped rather than forced.
/// </para>
/// <para>
/// The message names the record and the attempt, both of which are MailFathom's own identifiers. No address, subject,
/// or reply text reaches it.
/// </para>
/// </remarks>
public sealed class OutgoingEmailLeaseLostException : MailFathomException
{
    /// <summary>Initializes a new lease-lost failure naming the record whose lease had moved on.</summary>
    /// <param name="outgoingEmailId">The record the refused write was about.</param>
    /// <param name="owner">The attempt that was writing.</param>
    public OutgoingEmailLeaseLostException(OutgoingEmailId outgoingEmailId, Guid owner)
        : base($"Outgoing email record {outgoingEmailId} is no longer leased to attempt {owner}, so nothing was written for it.")
    {
        this.OutgoingEmailId = outgoingEmailId;
        this.Owner = owner;
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.OutgoingEmailLeaseLost;

    /// <summary>Gets the record whose lease had moved on.</summary>
    public OutgoingEmailId OutgoingEmailId { get; }

    /// <summary>Gets the attempt whose write was refused.</summary>
    public Guid Owner { get; }
}
