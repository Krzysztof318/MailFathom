// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using CsCheck;
using MailFathom.Application.Paging;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.Paging;

/// <summary>States the two rules the shared cursor codec holds for every input, rather than for chosen ones.</summary>
/// <remarks>
/// Seven cursor families encode through this one codec, so a boundary it reads back wrongly is a page served twice or
/// skipped on all of them at once. The examples beside this file pin the recorded text a client may be part-way
/// through; what a generator adds is the inputs nobody thought to write down — an instant at the representable ends, an
/// offset that makes two clocks name one moment, and text a caller built rather than received.
/// </remarks>
public sealed class KeysetCursorPayloadProperties
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

    /// <summary>Fingerprints are produced the way a reading produces one, so the alphabet is the real one.</summary>
    private static readonly Gen<string> Fingerprints = Gen.String[Gen.Char.AlphaNumeric, 0, 12]
        .Null()
        .Array[1, 4]
        .Select(PageFilterFingerprint.Of);

    private static readonly Gen<KeysetCursorPayload> Payloads = Gen.Select(
        Gen.Nullable(Instants),
        Gen.Guid.Where(identity => identity != Guid.Empty),
        Fingerprints,
        KeysetCursorPayload.At);

    /// <summary>What a caller may hand back: a cursor it received, one it damaged, and text it invented.</summary>
    private static readonly Gen<string> PresentedText = Gen.OneOf(
        Gen.String[Gen.Char[FirstPrintable, LastPrintable], 0, LongestPresentedText],
        Payloads.Select(payload => payload.Encode()),
        Gen.Select(Payloads, Gen.Int[0, LongestPresentedText], (payload, cut) => Shortened(payload.Encode(), cut)),
        Gen.Select(
            Payloads,
            Gen.Int[0, LongestPresentedText],
            Gen.Char[FirstPrintable, LastPrintable],
            (payload, at, replacement) => Altered(payload.Encode(), at, replacement)));

    /// <summary>Every field a cursor carries survives the encoding, whatever value the reading that issued it held.</summary>
    [Fact]
    public void Encode_AnyPayloadACursorMayCarry_ReadsBackTheSamePositionIdentityAndFingerprint()
    {
        // Act, Assert
        PropertyCheck.Holds(
            Payloads,
            payload =>
            {
                var read = KeysetCursorPayload.TryDecode(payload.Encode(), out var decoded);

                Assert.True(read);
                Assert.Equal(payload.Position, decoded.Position);
                Assert.Equal(payload.Identity, decoded.Identity);
                Assert.Equal(payload.FilterFingerprint, decoded.FilterFingerprint);
            },
            Iterations);
    }

    /// <summary>
    /// A cursor is opaque, so a caller can present anything at all: the decoder answers rather than throwing, and what
    /// it accepts is a payload whose own encoding decodes to the same thing. Without the second half, text carrying
    /// bits the encoder never wrote could be read as a boundary this system would never issue.
    /// </summary>
    [Fact]
    public void TryDecode_AnyTextACallerMayPresent_EitherRefusesItOrAcceptsAPayloadThatEncodesBackToItself()
    {
        // Act, Assert
        PropertyCheck.Holds(
            PresentedText,
            text =>
            {
                if (!KeysetCursorPayload.TryDecode(text, out var payload))
                {
                    return;
                }

                var reread = KeysetCursorPayload.TryDecode(payload.Encode(), out var again);

                Assert.True(reread);
                Assert.Equal(payload, again);
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
