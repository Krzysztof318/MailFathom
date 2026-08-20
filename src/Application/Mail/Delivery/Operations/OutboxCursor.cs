// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Paging;
using MailFathom.Domain.Delivery;

namespace MailFathom.Application.Mail.Delivery.Operations;

/// <summary>Marks where one page of an outbox ended, so the next page continues from it.</summary>
/// <remarks>
/// <para>
/// The reading is ordered newest first by the instant the send was written down, with the send's own identifier
/// breaking a tie, and this pairs those two values with a fingerprint of the filters the page was read under. Keyset
/// rather than offset because the set moves while it is being read: a worker settling a send, or an operator cancelling
/// one from a second terminal, would otherwise shift a window and cause a send to be skipped or listed twice — and on
/// this surface a skipped send is a message nobody notices is stuck.
/// </para>
/// <para>
/// It carries no secret and needs no signature: every value in it is one the caller already supplied or already
/// received. Encoding is about opacity rather than protection — a client that cannot read a cursor does not build one.
/// The encoded form itself is <see cref="KeysetCursorPayload" />'s, which every keyset cursor here shares.
/// </para>
/// </remarks>
public readonly record struct OutboxCursor
{
    private OutboxCursor(DateTimeOffset recordedAt, OutgoingEmailId outgoingEmailId, string filterFingerprint)
    {
        this.RecordedAt = recordedAt;
        this.OutgoingEmailId = outgoingEmailId;
        this.FilterFingerprint = filterFingerprint;
    }

    /// <summary>Gets the instant the last send the page returned was written down at.</summary>
    public DateTimeOffset RecordedAt { get; }

    /// <summary>Gets that send, which breaks a tie between two written down in one instant.</summary>
    public OutgoingEmailId OutgoingEmailId { get; }

    /// <summary>Gets the fingerprint of the filters this cursor was issued for.</summary>
    public string FilterFingerprint { get; }

    /// <summary>Creates the cursor that continues a walk after one position in the reading.</summary>
    /// <param name="recordedAt">The instant the page ended on.</param>
    /// <param name="outgoingEmailId">The send written down at that instant.</param>
    /// <param name="filterFingerprint">The fingerprint of the filters the page was read under.</param>
    /// <returns>The cursor.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="filterFingerprint" /> is blank.</exception>
    public static OutboxCursor After(
        DateTimeOffset recordedAt,
        OutgoingEmailId outgoingEmailId,
        string filterFingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filterFingerprint);

        return new OutboxCursor(recordedAt, outgoingEmailId, filterFingerprint);
    }

    /// <summary>Reads a cursor a caller presented.</summary>
    /// <param name="text">The encoded cursor, as a previous page returned it.</param>
    /// <param name="cursor">The decoded cursor when the text is one this version issued; otherwise <see langword="null" />.</param>
    /// <returns><see langword="true" /> when the text decoded into a usable cursor; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// Every send this reading returns was written down at a known instant, so a payload carrying none names no
    /// boundary here and is refused. Whether a decoded cursor belongs to the current request is a separate question its
    /// <see cref="FilterFingerprint" /> answers, and one this method deliberately does not ask.
    /// </remarks>
    public static bool TryDecode(string? text, out OutboxCursor? cursor)
    {
        cursor = null;

        if (!KeysetCursorPayload.TryDecode(text, out var payload) || payload.Position is not { } recordedAt)
        {
            return false;
        }

        cursor = new OutboxCursor(
            recordedAt,
            OutgoingEmailId.Create(payload.Identity),
            payload.FilterFingerprint);

        return true;
    }

    /// <summary>Writes the cursor as the opaque string a caller presents to continue the walk.</summary>
    /// <returns>The encoded cursor.</returns>
    public string Encode() =>
        KeysetCursorPayload.At(this.RecordedAt, this.OutgoingEmailId.Value, this.FilterFingerprint).Encode();
}
