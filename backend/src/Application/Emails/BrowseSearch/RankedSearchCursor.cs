// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Text;
using System.Globalization;
using System.Text;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Paging;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.BrowseSearch;

/// <summary>Marks where one page of a ranked search ended, so the next page continues from it.</summary>
/// <remarks>
/// <para>
/// The cursor pairs the place the last returned result held — its score and its timeline position, which together are
/// the total order a ranked list publishes — with the fingerprint of the list it was issued for. The next page reads
/// the results ordered strictly after that place, so the boundary is a key rather than a count and a client never asks
/// for "the results after the first forty".
/// </para>
/// <para>
/// What a ranked boundary cannot promise is what a timeline's promises. A timeline orders by an instant nothing
/// recomputes, so a cursor into one can neither skip a row nor repeat one; relevance is recomputed per query, so a
/// message indexed between two pages can move across a boundary a client is holding and be seen twice or not at all.
/// That is the honest cost of paging a ranking, and it is bounded rather than open: the order is total, so a
/// continuation always advances, and <see cref="RankedSearchList.MaximumRankedDepth" /> is where every walk ends.
/// </para>
/// <para>
/// It carries no secret and needs no signature, for the reason every cursor here does not: every value in it is one the
/// caller already received. Encoding is about opacity — a client that cannot read a cursor does not build one, and a
/// built cursor is how a caller would ask for a boundary this system never computed.
/// </para>
/// <para>
/// The encoded form is this reading's own rather than <see cref="KeysetCursorPayload" />'s, as
/// <see cref="Contacts.ContactCursor" />'s is. That payload carries an instant, an identity, and a fingerprint, and a
/// ranked boundary needs a fourth field those readings have no use for; widening the shared format so one reading could
/// carry a score would retire every cursor of the other six to add a field none of them writes.
/// </para>
/// </remarks>
public readonly record struct RankedSearchCursor
{
    /// <summary>The field a result no instant orders is written with.</summary>
    /// <remarks>A ranked list holds such results for the reason a timeline does: a message no header could date still matches a query.</remarks>
    private const string AbsentPosition = "-";

    /// <summary>The encoded form's version, leading the payload so a later change to the fields refuses these cursors instead of misreading them.</summary>
    private const string FormatVersion = "1";

    /// <summary>Separates the encoded fields, chosen because it appears in none of them.</summary>
    private const char FieldSeparator = '.';

    private RankedSearchCursor(float score, EmailTimelinePosition position, string filterFingerprint)
    {
        this.Score = score;
        this.Position = position;
        this.FilterFingerprint = filterFingerprint;
    }

    /// <summary>Gets the score the last returned result held, in the units of the ranking that produced it.</summary>
    public float Score { get; }

    /// <summary>Gets the timeline position of that result, which is what settles a tie between two equal scores.</summary>
    public EmailTimelinePosition Position { get; }

    /// <summary>Gets the fingerprint of the list this cursor was issued for.</summary>
    public string FilterFingerprint { get; }

    /// <summary>Gets the boundary as the ranking itself expresses it, which is what a continuation compares against.</summary>
    public RankedEmailCandidate Boundary => new(this.Position, this.Score);

    /// <summary>Creates the cursor that continues a ranked walk after one result.</summary>
    /// <param name="candidate">The last candidate the page returned.</param>
    /// <param name="filterFingerprint">The fingerprint of the list the page was read under.</param>
    /// <returns>The cursor.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="candidate" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="candidate" /> carries no stored email identity or a score no ranking produces, or when <paramref name="filterFingerprint" /> is blank.</exception>
    public static RankedSearchCursor After(RankedEmailCandidate candidate, string filterFingerprint)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentException.ThrowIfNullOrWhiteSpace(filterFingerprint);

        if (candidate.StoredEmailId.Value == Guid.Empty)
        {
            throw new ArgumentException(
                "A candidate without a stored email identity names no page boundary.",
                nameof(candidate));
        }

        // Every ranking here scores finitely and never below zero — a full-text rank, a distance, and a sum of
        // reciprocals are all such numbers — so a value outside that says the boundary was composed rather than ranked.
        if (!float.IsFinite(candidate.Score) || float.IsNegative(candidate.Score))
        {
            throw new ArgumentException(
                "A candidate scored outside what a ranking produces names no page boundary.",
                nameof(candidate));
        }

        return new RankedSearchCursor(candidate.Score, candidate.Position, filterFingerprint);
    }

    /// <summary>Reads a cursor a caller presented.</summary>
    /// <param name="text">The encoded cursor, as a previous page returned it.</param>
    /// <param name="cursor">The decoded cursor when the text is one this version issued; otherwise the struct default.</param>
    /// <returns><see langword="true" /> when the text decoded into a usable cursor; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// Every field is validated before a cursor is produced, so a caller cannot reach a query with a boundary that
    /// decoded only partially. Whether a decoded cursor belongs to the current request is the separate question its
    /// <see cref="FilterFingerprint" /> answers, and one this method deliberately does not ask.
    /// </remarks>
    public static bool TryDecode(string? text, out RankedSearchCursor cursor)
    {
        cursor = default;

        if (text is null || text.Length is 0 or > KeysetCursorPayload.MaximumEncodedLength)
        {
            return false;
        }

        // Validity is checked separately because the decoder's Try form reports only that a destination was too small
        // and throws on text that is not base64url at all, which is the shape a caller most easily presents.
        if (!Base64Url.IsValid(text))
        {
            return false;
        }

        Span<byte> decoded = stackalloc byte[Base64Url.GetMaxDecodedLength(KeysetCursorPayload.MaximumEncodedLength)];
        if (!Base64Url.TryDecodeFromChars(text, decoded, out var decodedLength))
        {
            return false;
        }

        var fields = Encoding.UTF8.GetString(decoded[..decodedLength]).Split(FieldSeparator);

        if (fields is not [FormatVersion, var scoreField, var positionField, var identityField, var fingerprintField]
            || !TryReadScore(scoreField, out var score)
            || !TryReadPosition(positionField, out var position)
            || !Guid.TryParseExact(identityField, "N", out var identity)
            || identity == Guid.Empty
            || fingerprintField.Length is 0)
        {
            return false;
        }

        cursor = new RankedSearchCursor(
            score,
            new EmailTimelinePosition(position, StoredEmailId.Create(identity)),
            fingerprintField);

        return true;
    }

    /// <summary>Writes the cursor as the opaque string a caller presents to continue the walk.</summary>
    /// <returns>The encoded cursor.</returns>
    /// <remarks>
    /// The score is written as the bits of the number rather than as a rendering of it, because the boundary is
    /// compared against a score this deployment computes again on the next page: a decimal rendering that lost its last
    /// place would put the boundary between two results that had been equal, and repeat one of them. The instant is
    /// written as its UTC tick count, for the reason every cursor here writes one that way.
    /// </remarks>
    public string Encode()
    {
        var payload = string.Join(
            FieldSeparator,
            FormatVersion,
            BitConverter.SingleToUInt32Bits(this.Score).ToString(CultureInfo.InvariantCulture),
            this.Position.ReceivedAt is { } receivedAt
                ? receivedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)
                : AbsentPosition,
            this.Position.StoredEmailId.Value.ToString("N", CultureInfo.InvariantCulture),
            this.FilterFingerprint);

        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload));
    }

    /// <summary>Reads the score back out of the bits it was written as.</summary>
    /// <remarks><see cref="NumberStyles.None" /> refuses a sign, so no negative bit pattern reaches the conversion, and the finite check refuses the patterns that name no number.</remarks>
    private static bool TryReadScore(string field, out float score)
    {
        score = 0f;

        if (!uint.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out var bits))
        {
            return false;
        }

        score = BitConverter.UInt32BitsToSingle(bits);

        return float.IsFinite(score) && !float.IsNegative(score);
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
