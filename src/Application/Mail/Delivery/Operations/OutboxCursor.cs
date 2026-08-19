// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Text;
using System.Globalization;
using System.Text;
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
/// </para>
/// </remarks>
public readonly record struct OutboxCursor
{
    /// <summary>The greatest number of characters an encoded cursor may carry before it is refused unread.</summary>
    public const int MaximumEncodedLength = 512;

    /// <summary>
    /// The encoded form's version. It leads the payload so a later change to the fields refuses the cursors this version
    /// issued instead of misreading them.
    /// </summary>
    private const string FormatVersion = "1";

    /// <summary>Separates the encoded fields, chosen because it appears in none of them.</summary>
    private const char FieldSeparator = '.';

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
    /// <param name="cursor">The decoded cursor when the text is one this version issued; otherwise the struct default.</param>
    /// <returns><see langword="true" /> when the text decoded into a usable cursor; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// Whether a decoded cursor belongs to the current request is a separate question its
    /// <see cref="FilterFingerprint" /> answers, and one this method deliberately does not ask.
    /// </remarks>
    public static bool TryDecode(string? text, out OutboxCursor cursor)
    {
        cursor = default;

        if (text is null || text.Length is 0 or > MaximumEncodedLength)
        {
            return false;
        }

        // Validity is checked separately because the decoder's Try form reports only that a destination was too small
        // and throws on text that is not base64url at all, which is the shape a caller most easily presents.
        if (!Base64Url.IsValid(text))
        {
            return false;
        }

        Span<byte> decoded = stackalloc byte[Base64Url.GetMaxDecodedLength(MaximumEncodedLength)];
        if (!Base64Url.TryDecodeFromChars(text, decoded, out var decodedLength))
        {
            return false;
        }

        var fields = Encoding.UTF8.GetString(decoded[..decodedLength]).Split(FieldSeparator);

        if (fields is not [FormatVersion, var recordedField, var identifierField, var fingerprintField]
            || !TryReadRecordedAt(recordedField, out var recordedAt)
            || !Guid.TryParseExact(identifierField, "N", out var identifier)
            || identifier == Guid.Empty
            || fingerprintField.Length is 0)
        {
            return false;
        }

        cursor = new OutboxCursor(recordedAt, OutgoingEmailId.Create(identifier), fingerprintField);

        return true;
    }

    /// <summary>Writes the cursor as the opaque string a caller presents to continue the walk.</summary>
    /// <returns>The encoded cursor.</returns>
    /// <remarks>
    /// The instant is written as its UTC tick count, which is the form the order compares: two timestamps naming the
    /// same instant in different offsets encode identically.
    /// </remarks>
    public string Encode()
    {
        var payload = string.Join(
            FieldSeparator,
            FormatVersion,
            this.RecordedAt.UtcTicks.ToString(CultureInfo.InvariantCulture),
            this.OutgoingEmailId.Value.ToString("N", CultureInfo.InvariantCulture),
            this.FilterFingerprint);

        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload));
    }

    private static bool TryReadRecordedAt(string field, out DateTimeOffset recordedAt)
    {
        recordedAt = default;

        // NumberStyles.None refuses a sign, so no negative tick count reaches the range check below.
        if (!long.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out var utcTicks)
            || utcTicks < DateTime.MinValue.Ticks
            || utcTicks > DateTime.MaxValue.Ticks)
        {
            return false;
        }

        recordedAt = new DateTimeOffset(utcTicks, TimeSpan.Zero);

        return true;
    }
}
