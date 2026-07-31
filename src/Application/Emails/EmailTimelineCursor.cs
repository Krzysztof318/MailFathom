// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Buffers.Text;
using System.Globalization;
using System.Text;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails;

/// <summary>Marks where one page of a mailbox timeline ended, so the next page continues from it.</summary>
/// <remarks>
/// <para>
/// The cursor pairs the timeline position the last returned row occupied with a fingerprint of the filters it was
/// issued for. The position is what makes pagination keyset-based rather than offset-based: the next page asks for rows
/// beyond a known boundary, so mail arriving between two requests neither shifts a window nor causes a row to be
/// skipped or repeated. The fingerprint is what makes the boundary meaningful — a position names a page edge only within
/// the filtered set and reading direction it was computed for.
/// </para>
/// <para>
/// It carries no secret and needs no signature, because every value in it is one the caller already supplied or already
/// received: a received timestamp, a local identifier, and a hash of the filters they wrote. Encoding it is about
/// opacity rather than protection — a client that cannot read a cursor cannot build one, and building one is how a
/// caller would end up asking for a boundary this system never computed.
/// </para>
/// </remarks>
public readonly record struct EmailTimelineCursor
{
    /// <summary>The greatest number of characters an encoded cursor may carry before it is refused unread.</summary>
    /// <remarks>
    /// Comfortably above every cursor this version issues, and low enough that a caller cannot make the decoder work.
    /// The bound is applied before decoding, because a decoder is the wrong place to discover that an input is absurd.
    /// </remarks>
    public const int MaximumEncodedLength = 512;

    /// <summary>The field the encoded form uses for a message no header could date.</summary>
    private const string AbsentReceivedTimestamp = "-";

    /// <summary>
    /// The encoded form's version. It leads the payload so a later change to the fields — another ordering key, a
    /// different fingerprint — refuses the cursors this version issued instead of misreading them.
    /// </summary>
    private const string FormatVersion = "1";

    /// <summary>Separates the encoded fields, chosen because it appears in none of them.</summary>
    private const char FieldSeparator = '.';

    private EmailTimelineCursor(EmailTimelinePosition position, string filterFingerprint)
    {
        this.Position = position;
        this.FilterFingerprint = filterFingerprint;
    }

    /// <summary>Gets the timeline position the page ended on, which the next page reads beyond.</summary>
    public EmailTimelinePosition Position { get; }

    /// <summary>Gets the fingerprint of the filters and reading direction this cursor was issued for.</summary>
    public string FilterFingerprint { get; }

    /// <summary>Creates the cursor that continues a walk after one timeline position.</summary>
    /// <param name="position">The position of the last row the page returned.</param>
    /// <param name="filterFingerprint">The fingerprint of the filters the page was read under.</param>
    /// <returns>The cursor.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="position" /> carries no stored email identity, or when <paramref name="filterFingerprint" /> is blank.</exception>
    public static EmailTimelineCursor After(EmailTimelinePosition position, string filterFingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filterFingerprint);

        if (position.StoredEmailId.Value == Guid.Empty)
        {
            throw new ArgumentException(
                "A timeline position without a stored email identity names no page boundary.",
                nameof(position));
        }

        return new EmailTimelineCursor(position, filterFingerprint);
    }

    /// <summary>Reads a cursor a caller presented.</summary>
    /// <param name="text">The encoded cursor, as a previous page returned it.</param>
    /// <param name="cursor">The decoded cursor when the text is one this version issued; otherwise the struct default.</param>
    /// <returns><see langword="true" /> when the text decoded into a usable cursor; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// Every field is validated before a cursor is produced, so a caller cannot reach a query with a boundary that
    /// decoded only partially. Whether a decoded cursor belongs to the current request is a separate question its
    /// <see cref="FilterFingerprint" /> answers, and one this method deliberately does not ask.
    /// </remarks>
    public static bool TryDecode(string? text, out EmailTimelineCursor cursor)
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

        if (fields is not [FormatVersion, var receivedField, var identifierField, var fingerprintField]
            || !TryReadReceivedAt(receivedField, out var receivedAt)
            || !Guid.TryParseExact(identifierField, "N", out var identifier)
            || identifier == Guid.Empty
            || fingerprintField.Length is 0)
        {
            return false;
        }

        cursor = new EmailTimelineCursor(
            new EmailTimelinePosition(receivedAt, StoredEmailId.Create(identifier)),
            fingerprintField);

        return true;
    }

    /// <summary>Writes the cursor as the opaque string a caller presents to continue the walk.</summary>
    /// <returns>The encoded cursor.</returns>
    /// <remarks>
    /// The received timestamp is written as its UTC tick count, which is the form the timeline order compares: two
    /// timestamps that name the same instant in different offsets encode identically, so a boundary cannot depend on the
    /// offset a mail server happened to write.
    /// </remarks>
    public string Encode()
    {
        var payload = string.Join(
            FieldSeparator,
            FormatVersion,
            this.Position.ReceivedAt is { } receivedAt
                ? receivedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)
                : AbsentReceivedTimestamp,
            this.Position.StoredEmailId.Value.ToString("N", CultureInfo.InvariantCulture),
            this.FilterFingerprint);

        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload));
    }

    private static bool TryReadReceivedAt(string field, out DateTimeOffset? receivedAt)
    {
        receivedAt = null;

        if (string.Equals(field, AbsentReceivedTimestamp, StringComparison.Ordinal))
        {
            return true;
        }

        // NumberStyles.None refuses a sign, so no negative tick count reaches the range check below.
        if (!long.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out var utcTicks)
            || utcTicks < DateTime.MinValue.Ticks
            || utcTicks > DateTime.MaxValue.Ticks)
        {
            return false;
        }

        receivedAt = new DateTimeOffset(utcTicks, TimeSpan.Zero);

        return true;
    }
}
