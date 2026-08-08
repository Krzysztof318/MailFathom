// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Text;
using System.Globalization;
using System.Text;
using MailFathom.Domain.Mutations.Audit;

namespace MailFathom.Application.Mail.Mutations.Audit;

/// <summary>Marks where one page of an audit trail ended, so the next page continues from it.</summary>
/// <remarks>
/// <para>
/// The trail is ordered newest first by completion instant, with the entry identifier breaking a tie, and this pairs
/// those two values with a fingerprint of the filters the page was read under. The pair is what makes pagination
/// keyset-based rather than offset-based: the next page asks for entries beyond a known boundary, so a mutation
/// finishing between two requests neither shifts a window nor causes an entry to be skipped or repeated. The fingerprint
/// is what makes the boundary meaningful, because a position names a page edge only within the filtered set it was
/// computed for.
/// </para>
/// <para>
/// It carries no secret and needs no signature: every value in it is one the caller already supplied or already
/// received. Encoding is about opacity rather than protection — a client that cannot read a cursor does not build one,
/// and a built cursor is how a caller would ask for a boundary this system never computed.
/// </para>
/// </remarks>
public readonly record struct MailboxMutationAuditCursor
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

    private MailboxMutationAuditCursor(
        DateTimeOffset completedAt,
        MailboxMutationAuditEntryId entryId,
        string filterFingerprint)
    {
        this.CompletedAt = completedAt;
        this.EntryId = entryId;
        this.FilterFingerprint = filterFingerprint;
    }

    /// <summary>Gets the completion instant of the last entry the page returned.</summary>
    public DateTimeOffset CompletedAt { get; }

    /// <summary>Gets the identity of that entry, which breaks a tie between two that finished together.</summary>
    public MailboxMutationAuditEntryId EntryId { get; }

    /// <summary>Gets the fingerprint of the filters this cursor was issued for.</summary>
    public string FilterFingerprint { get; }

    /// <summary>Creates the cursor that continues a walk after one entry.</summary>
    /// <param name="entry">The last entry the page returned.</param>
    /// <param name="filterFingerprint">The fingerprint of the filters the page was read under.</param>
    /// <returns>The cursor.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entry" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="filterFingerprint" /> is blank.</exception>
    public static MailboxMutationAuditCursor After(MailboxMutationAuditEntry entry, string filterFingerprint)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(filterFingerprint);

        return new MailboxMutationAuditCursor(entry.CompletedAt, entry.Id, filterFingerprint);
    }

    /// <summary>Reads a cursor a caller presented.</summary>
    /// <param name="text">The encoded cursor, as a previous page returned it.</param>
    /// <param name="cursor">The decoded cursor when the text is one this version issued; otherwise the struct default.</param>
    /// <returns><see langword="true" /> when the text decoded into a usable cursor; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// Whether a decoded cursor belongs to the current request is a separate question its
    /// <see cref="FilterFingerprint" /> answers, and one this method deliberately does not ask.
    /// </remarks>
    public static bool TryDecode(string? text, out MailboxMutationAuditCursor cursor)
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

        if (fields is not [FormatVersion, var completedField, var identifierField, var fingerprintField]
            || !TryReadCompletedAt(completedField, out var completedAt)
            || !Guid.TryParseExact(identifierField, "N", out var identifier)
            || identifier == Guid.Empty
            || fingerprintField.Length is 0)
        {
            return false;
        }

        cursor = new MailboxMutationAuditCursor(
            completedAt,
            MailboxMutationAuditEntryId.Create(identifier),
            fingerprintField);

        return true;
    }

    /// <summary>Writes the cursor as the opaque string a caller presents to continue the walk.</summary>
    /// <returns>The encoded cursor.</returns>
    /// <remarks>
    /// The completion instant is written as its UTC tick count, which is the form the order compares: two timestamps
    /// naming the same instant in different offsets encode identically.
    /// </remarks>
    public string Encode()
    {
        var payload = string.Join(
            FieldSeparator,
            FormatVersion,
            this.CompletedAt.UtcTicks.ToString(CultureInfo.InvariantCulture),
            this.EntryId.Value.ToString("N", CultureInfo.InvariantCulture),
            this.FilterFingerprint);

        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload));
    }

    private static bool TryReadCompletedAt(string field, out DateTimeOffset completedAt)
    {
        completedAt = default;

        // NumberStyles.None refuses a sign, so no negative tick count reaches the range check below.
        if (!long.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out var utcTicks)
            || utcTicks < DateTime.MinValue.Ticks
            || utcTicks > DateTime.MaxValue.Ticks)
        {
            return false;
        }

        completedAt = new DateTimeOffset(utcTicks, TimeSpan.Zero);

        return true;
    }
}
