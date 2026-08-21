// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Delivery;

namespace MailFathom.Application.Mail.Delivery;

/// <summary>The record one request has, and whether writing it down is what this call did.</summary>
/// <remarks>
/// An idempotent enqueue answers a repeated request with the record the first one left, which is what makes a retry
/// safe. That leaves the two outcomes indistinguishable in the record itself — it is the same row either way — so
/// anything a send should cause exactly once, however often the request arrives, reads it here instead. The audit of
/// an authored send is the case that exists today: a second entry for one outgoing message, at a second moment, would
/// report a message as having been sent twice to whoever reads the trail for an odd send.
/// </remarks>
public sealed record OpenedOutgoingEmail
{
    private OpenedOutgoingEmail(OutgoingEmailRecord record, bool wasRecordedNow)
    {
        this.Record = record;
        this.WasRecordedNow = wasRecordedNow;
    }

    /// <summary>Gets the durable record for the request, whichever call wrote it.</summary>
    public OutgoingEmailRecord Record { get; }

    /// <summary>Gets whether this call is the one that wrote the record, rather than finding it already written.</summary>
    public bool WasRecordedNow { get; }

    /// <summary>Reports that this call wrote the record down.</summary>
    /// <param name="record">The record this call created.</param>
    /// <returns>The opening of a request nothing had recorded before.</returns>
    public static OpenedOutgoingEmail RecordedNow(OutgoingEmailRecord record) => new(record, wasRecordedNow: true);

    /// <summary>Reports that an identical earlier request had already written the record.</summary>
    /// <param name="record">The record the earlier request left.</param>
    /// <returns>The opening of a request that needed no second row.</returns>
    public static OpenedOutgoingEmail AlreadyRecorded(OutgoingEmailRecord record) => new(record, wasRecordedNow: false);
}
