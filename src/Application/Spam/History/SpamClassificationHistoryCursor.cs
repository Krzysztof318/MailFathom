// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Text;
using System.Globalization;
using System.Text;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Spam.History;

/// <summary>Marks where one page of an account's classifications ended, so the next page continues from it.</summary>
/// <remarks>
/// <para>
/// The reading is ordered newest first by evaluation instant, with the occurrence identifier breaking a tie, and this
/// pairs those two values with a fingerprint of the filters the page was read under. The pair is what makes pagination
/// keyset-based rather than offset-based: a run that classifies mail between two requests neither shifts a window nor
/// causes a message to be skipped or repeated. The fingerprint is what makes the boundary meaningful, because a position
/// names a page edge only within the filtered set it was computed for.
/// </para>
/// <para>
/// It carries no secret and needs no signature: every value in it is one the caller already supplied or already
/// received. Encoding is about opacity rather than protection — a client that cannot read a cursor does not build one.
/// </para>
/// </remarks>
public readonly record struct SpamClassificationHistoryCursor
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

    private SpamClassificationHistoryCursor(
        DateTimeOffset evaluatedAt,
        StoredEmailId emailId,
        string filterFingerprint)
    {
        this.EvaluatedAt = evaluatedAt;
        this.EmailId = emailId;
        this.FilterFingerprint = filterFingerprint;
    }

    /// <summary>Gets the evaluation instant of the last classification the page returned.</summary>
    public DateTimeOffset EvaluatedAt { get; }

    /// <summary>Gets the occurrence of that classification, which breaks a tie between two evaluated in one instant.</summary>
    public StoredEmailId EmailId { get; }

    /// <summary>Gets the fingerprint of the filters this cursor was issued for.</summary>
    public string FilterFingerprint { get; }

    /// <summary>Creates the cursor that continues a walk after one position in the reading.</summary>
    /// <param name="evaluatedAt">The evaluation instant the page ended on.</param>
    /// <param name="emailId">The occurrence classified at that instant.</param>
    /// <param name="filterFingerprint">The fingerprint of the filters the page was read under.</param>
    /// <returns>The cursor.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="filterFingerprint" /> is blank.</exception>
    public static SpamClassificationHistoryCursor After(
        DateTimeOffset evaluatedAt,
        StoredEmailId emailId,
        string filterFingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filterFingerprint);

        return new SpamClassificationHistoryCursor(evaluatedAt, emailId, filterFingerprint);
    }

    /// <summary>Reads a cursor a caller presented.</summary>
    /// <param name="text">The encoded cursor, as a previous page returned it.</param>
    /// <param name="cursor">The decoded cursor when the text is one this version issued; otherwise the struct default.</param>
    /// <returns><see langword="true" /> when the text decoded into a usable cursor; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// Whether a decoded cursor belongs to the current request is a separate question its
    /// <see cref="FilterFingerprint" /> answers, and one this method deliberately does not ask.
    /// </remarks>
    public static bool TryDecode(string? text, out SpamClassificationHistoryCursor cursor)
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

        if (fields is not [FormatVersion, var evaluatedField, var identifierField, var fingerprintField]
            || !TryReadEvaluatedAt(evaluatedField, out var evaluatedAt)
            || !Guid.TryParseExact(identifierField, "N", out var identifier)
            || identifier == Guid.Empty
            || fingerprintField.Length is 0)
        {
            return false;
        }

        cursor = new SpamClassificationHistoryCursor(
            evaluatedAt,
            StoredEmailId.Create(identifier),
            fingerprintField);

        return true;
    }

    /// <summary>Writes the cursor as the opaque string a caller presents to continue the walk.</summary>
    /// <returns>The encoded cursor.</returns>
    /// <remarks>
    /// The evaluation instant is written as its UTC tick count, which is the form the order compares: two timestamps
    /// naming the same instant in different offsets encode identically.
    /// </remarks>
    public string Encode()
    {
        var payload = string.Join(
            FieldSeparator,
            FormatVersion,
            this.EvaluatedAt.UtcTicks.ToString(CultureInfo.InvariantCulture),
            this.EmailId.Value.ToString("N", CultureInfo.InvariantCulture),
            this.FilterFingerprint);

        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload));
    }

    private static bool TryReadEvaluatedAt(string field, out DateTimeOffset evaluatedAt)
    {
        evaluatedAt = default;

        // NumberStyles.None refuses a sign, so no negative tick count reaches the range check below.
        if (!long.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out var utcTicks)
            || utcTicks < DateTime.MinValue.Ticks
            || utcTicks > DateTime.MaxValue.Ticks)
        {
            return false;
        }

        evaluatedAt = new DateTimeOffset(utcTicks, TimeSpan.Zero);

        return true;
    }
}
