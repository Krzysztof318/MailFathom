// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using CsCheck;
using MailFathom.Application.Emails.BrowseSearch;
using MailFathom.Application.Emails.Search;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.BrowseSearch;

/// <summary>States the rules the ranked cursor holds for every boundary a search can end a page on, rather than for chosen ones.</summary>
/// <remarks>
/// <para>
/// This cursor is the one that carries a score, and a score read back even slightly wrong is a boundary between two
/// results that had been equal — so the page after it repeats one of them. A generator is what reaches the scores
/// nobody writes down: the reciprocal sums a fusion produces, the ranks a full-text match produces, and the ends of
/// both.
/// </para>
/// <para>
/// Scores are drawn non-negative and finite because that is what the type accepts, and what every ranking here
/// produces: a full-text rank, a distance, and a sum of reciprocals are all such numbers.
/// </para>
/// </remarks>
public sealed class RankedSearchCursorProperties
{
    /// <summary>How many inputs each property here draws.</summary>
    private const int Iterations = 500;

    /// <summary>The greatest offset a <see cref="DateTimeOffset" /> may carry, in minutes.</summary>
    private const int GreatestOffsetMinutes = 14 * 60;

    /// <summary>The printable range a caller's text is drawn from, and the longest such text drawn.</summary>
    private const char FirstPrintable = ' ';

    private const char LastPrintable = '~';

    private const int LongestPresentedText = 600;

    /// <summary>
    /// Instants stay one offset clear of the representable ends, because the generator moves each one into a named
    /// offset and that is the conversion the type refuses at the very edge. The ends themselves are drawn beside them.
    /// </summary>
    private static readonly Gen<DateTimeOffset> MovedInstants = Gen.Select(
        Gen.Long[
            DateTime.MinValue.Ticks + (TimeSpan.TicksPerMinute * GreatestOffsetMinutes),
            DateTime.MaxValue.Ticks - (TimeSpan.TicksPerMinute * GreatestOffsetMinutes)],
        Gen.Int[-GreatestOffsetMinutes, GreatestOffsetMinutes],
        (utcTicks, offsetMinutes) => new DateTimeOffset(utcTicks, TimeSpan.Zero)
            .ToOffset(TimeSpan.FromMinutes(offsetMinutes)));

    private static readonly Gen<DateTimeOffset> Instants = Gen.OneOf(
        MovedInstants,
        Gen.OneOfConst(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, DateTimeOffset.UnixEpoch));

    /// <summary>What a ranking scores: the reciprocal sums a fusion produces, ordinary ranks, and the ends of the range.</summary>
    private static readonly Gen<float> Scores = Gen.OneOf(
        Gen.Float[0f, 1f],
        Gen.Int[1, 400].Select(place => (1f / (ReciprocalRankFusion.RankConstant + place))
            + (1f / (ReciprocalRankFusion.RankConstant + place + 1))),
        Gen.OneOfConst(0f, float.Epsilon, float.MaxValue));

    private static readonly Gen<RankedEmailCandidate> Candidates = Gen.Select(
        Gen.Nullable(Instants),
        Gen.Guid.Where(identity => identity != Guid.Empty),
        Scores,
        (receivedAt, identity, score) => new RankedEmailCandidate(
            new EmailTimelinePosition(receivedAt, StoredEmailId.Create(identity)),
            score));

    private static readonly Gen<string> Fingerprints = Gen.String[Gen.Char.AlphaNumeric, 1, 22];

    private static readonly Gen<RankedSearchCursor> Cursors =
        Gen.Select(Candidates, Fingerprints, RankedSearchCursor.After);

    /// <summary>What a caller may hand back: a cursor it received, one it damaged, and text it invented.</summary>
    private static readonly Gen<string> PresentedText = Gen.OneOf(
        Gen.String[Gen.Char[FirstPrintable, LastPrintable], 0, LongestPresentedText],
        Cursors.Select(cursor => cursor.Encode()),
        Gen.Select(Cursors, Gen.Int[0, LongestPresentedText], (cursor, cut) => Shortened(cursor.Encode(), cut)),
        Gen.Select(
            Cursors,
            Gen.Int[0, LongestPresentedText],
            Gen.Char[FirstPrintable, LastPrintable],
            (cursor, at, replacement) => Altered(cursor.Encode(), at, replacement)));

    /// <summary>Every field a ranked boundary carries survives the encoding, the score included and to the last bit.</summary>
    [Fact]
    public void Encode_AnyBoundaryARankingMayProduce_ReadsBackTheSameScorePositionAndFingerprint()
    {
        // Act, Assert
        PropertyCheck.Holds(
            Cursors,
            cursor =>
            {
                var read = RankedSearchCursor.TryDecode(cursor.Encode(), out var decoded);

                Assert.True(read);
                Assert.Equal(cursor.Score, decoded.Score);
                Assert.Equal(cursor.Position, decoded.Position);
                Assert.Equal(cursor.FilterFingerprint, decoded.FilterFingerprint);
            },
            Iterations);
    }

    /// <summary>
    /// A cursor is opaque, so a caller can present anything at all: the decoder answers rather than throwing, and what
    /// it accepts is a boundary whose own encoding decodes to the same thing. Without the second half, text carrying
    /// bits the encoder never wrote could be read as a place this system would never have handed out.
    /// </summary>
    [Fact]
    public void TryDecode_AnyTextACallerMayPresent_EitherRefusesItOrAcceptsABoundaryThatEncodesBackToItself()
    {
        // Act, Assert
        PropertyCheck.Holds(
            PresentedText,
            text =>
            {
                if (!RankedSearchCursor.TryDecode(text, out var cursor))
                {
                    return;
                }

                var reread = RankedSearchCursor.TryDecode(cursor.Encode(), out var again);

                Assert.True(reread);
                Assert.Equal(cursor, again);
            },
            Iterations);
    }

    private static string Shortened(string encoded, int characters) =>
        encoded[..Math.Max(0, encoded.Length - characters)];

    private static string Altered(string encoded, int at, char replacement)
    {
        var index = at % encoded.Length;

        return string.Concat(encoded[..index], replacement, encoded[(index + 1)..]);
    }
}
