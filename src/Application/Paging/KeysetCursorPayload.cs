// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace MailFathom.Application.Paging;

/// <summary>Carries what every keyset cursor here encodes: an ordering position, the row at it, and a filter fingerprint.</summary>
/// <remarks>
/// <para>
/// Each paged reading declares a cursor of its own, because the position it walks and the identity that breaks a tie
/// are its own domain types and its own words. What none of them owns is the encoding: the field layout, the version
/// that leads it, the length bound applied before a byte is decoded, and the tick form an instant is written in are one
/// decision, and a defect in any of them is a page served twice or skipped on every surface at once.
/// </para>
/// <para>
/// The format version is shared as well, which is what writing the codec once costs: a later change to the layout
/// retires the cursors of all seven families together rather than one family at a time. That is the intended trade — a
/// cursor is opaque and short-lived, and a version that drifted per family is one nobody could reason about.
/// </para>
/// <para>
/// <see cref="Contacts.ContactCursor" /> is deliberately outside this. It orders by a name's comparison form rather
/// than by an instant, carries no fingerprint, and lets its last field hold the separator: a different format that
/// happens to be base64url, not a seventh copy of this one.
/// </para>
/// <para>
/// The payload carries no secret and needs no signature, because every value in it is one the caller already supplied
/// or already received. Encoding is about opacity rather than protection — a client that cannot read a cursor does not
/// build one, and a built cursor is how a caller would ask for a boundary this system never computed.
/// </para>
/// </remarks>
public readonly record struct KeysetCursorPayload
{
    /// <summary>The greatest number of characters an encoded cursor may carry before it is refused unread.</summary>
    /// <remarks>
    /// Comfortably above every cursor this version issues, and low enough that a caller cannot make the decoder work.
    /// The bound is applied before decoding, because a decoder is the wrong place to discover that an input is absurd.
    /// </remarks>
    public const int MaximumEncodedLength = 512;

    /// <summary>The field a row no instant orders is written with, for the one reading that has such rows.</summary>
    private const string AbsentPosition = "-";

    /// <summary>
    /// The encoded form's version. It leads the payload so a later change to the fields refuses the cursors this version
    /// issued instead of misreading them.
    /// </summary>
    private const string FormatVersion = "1";

    /// <summary>Separates the encoded fields, chosen because it appears in none of them.</summary>
    private const char FieldSeparator = '.';

    private KeysetCursorPayload(DateTimeOffset? position, Guid identity, string filterFingerprint)
    {
        this.Position = position;
        this.Identity = identity;
        this.FilterFingerprint = filterFingerprint;
    }

    /// <summary>Gets the instant the page ended on, or <see langword="null" /> when no instant orders that row.</summary>
    public DateTimeOffset? Position { get; }

    /// <summary>Gets the identity of the row at that position, which breaks a tie between two sharing an instant.</summary>
    public Guid Identity { get; }

    /// <summary>Gets the fingerprint of the filters the cursor was issued for.</summary>
    public string FilterFingerprint { get; }

    /// <summary>Creates the payload one page boundary encodes to.</summary>
    /// <param name="position">The instant the page ended on, or <see langword="null" /> when no instant orders that row.</param>
    /// <param name="identity">The identity of the row at that position.</param>
    /// <param name="filterFingerprint">The fingerprint of the filters the page was read under.</param>
    /// <returns>The payload.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="filterFingerprint" /> is blank.</exception>
    public static KeysetCursorPayload At(DateTimeOffset? position, Guid identity, string filterFingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filterFingerprint);

        return new KeysetCursorPayload(position, identity, filterFingerprint);
    }

    /// <summary>Reads the payload out of the text a caller presented.</summary>
    /// <param name="text">The encoded cursor, as a previous page returned it.</param>
    /// <param name="payload">The decoded payload when the text is one this version issued; otherwise the struct default.</param>
    /// <returns><see langword="true" /> when the text decoded into a usable payload; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// Every field is validated before a payload is produced, so a caller cannot reach a query with a boundary that
    /// decoded only partially. A reading whose every row is ordered by an instant refuses a decoded
    /// <see cref="Position" /> of <see langword="null" /> itself, because that field means something only where a row
    /// can lack the instant. Whether a payload belongs to the current request is a separate question its
    /// <see cref="FilterFingerprint" /> answers, and one this method deliberately does not ask.
    /// </remarks>
    public static bool TryDecode(string? text, out KeysetCursorPayload payload)
    {
        payload = default;

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

        if (fields is not [FormatVersion, var positionField, var identityField, var fingerprintField]
            || !TryReadPosition(positionField, out var position)
            || !Guid.TryParseExact(identityField, "N", out var identity)
            || identity == Guid.Empty
            || fingerprintField.Length is 0)
        {
            return false;
        }

        payload = new KeysetCursorPayload(position, identity, fingerprintField);

        return true;
    }

    /// <summary>Writes the payload as the opaque string a caller presents to continue the walk.</summary>
    /// <returns>The encoded cursor.</returns>
    /// <remarks>
    /// The position is written as its UTC tick count, which is the form every one of these orderings compares: two
    /// timestamps naming the same instant in different offsets encode identically, so a boundary cannot depend on the
    /// offset whoever wrote the row happened to use.
    /// </remarks>
    public string Encode()
    {
        var payload = string.Join(
            FieldSeparator,
            FormatVersion,
            this.Position is { } position
                ? position.UtcTicks.ToString(CultureInfo.InvariantCulture)
                : AbsentPosition,
            this.Identity.ToString("N", CultureInfo.InvariantCulture),
            this.FilterFingerprint);

        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload));
    }

    private static bool TryReadPosition(string field, out DateTimeOffset? position)
    {
        position = null;

        if (string.Equals(field, AbsentPosition, StringComparison.Ordinal))
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

        position = new DateTimeOffset(utcTicks, TimeSpan.Zero);

        return true;
    }
}
