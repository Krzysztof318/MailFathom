// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Storage;

/// <summary>Names which of the four things a raw MIME payload is, so a placement can say what it placed.</summary>
/// <remarks>
/// <para>
/// The four are the port's own write methods seen from the other side: each has its own owning row, its own write
/// semantics, and — under the object backend — its own group of keys. A placement happens before the owning row exists,
/// so this is the only thing the store knows about a payload at that moment, and it is what lets one placement method
/// serve all four rather than four near-identical ones differing by a single segment.
/// </para>
/// <para>
/// It reaches an object key as a segment, which is a readability and grouping property rather than a durable identity:
/// a row carries the whole key the adapter produced and no reader ever derives one, so renaming a member changes what
/// future keys look like and leaves every stored payload readable.
/// </para>
/// </remarks>
public enum EmailContentKind
{
    /// <summary>A synchronized message, stored idempotently against the local row that mirrors an occurrence.</summary>
    IncomingMessage = 0,

    /// <summary>The message one outgoing record will be transmitted as, written once so a retry transmits the same bytes.</summary>
    OutgoingMessage = 1,

    /// <summary>The draft every occurrence of one recurring send is composed from, written once beside the declaration.</summary>
    RecurringSendDraft = 2,

    /// <summary>One revision of a mail draft, which is the one payload kind a later write replaces.</summary>
    MailDraft = 3,
}
