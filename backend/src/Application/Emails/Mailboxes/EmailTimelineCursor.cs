// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Paging;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Mailboxes;

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
/// <para>
/// The encoded form itself is <see cref="KeysetCursorPayload" />'s, which every keyset cursor here shares. This is the
/// one reading whose rows need not carry an instant — a message no header could date is still on the timeline — so it
/// is also the one that accepts a decoded position of <see langword="null" /> rather than refusing it.
/// </para>
/// </remarks>
public readonly record struct EmailTimelineCursor
{
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

        if (!KeysetCursorPayload.TryDecode(text, out var payload))
        {
            return false;
        }

        cursor = new EmailTimelineCursor(
            new EmailTimelinePosition(payload.Position, StoredEmailId.Create(payload.Identity)),
            payload.FilterFingerprint);

        return true;
    }

    /// <summary>Writes the cursor as the opaque string a caller presents to continue the walk.</summary>
    /// <returns>The encoded cursor.</returns>
    public string Encode() => KeysetCursorPayload
        .At(this.Position.ReceivedAt, this.Position.StoredEmailId.Value, this.FilterFingerprint)
        .Encode();
}
