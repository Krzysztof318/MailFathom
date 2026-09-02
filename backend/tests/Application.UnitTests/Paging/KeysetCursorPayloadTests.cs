// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Text;
using System.Text;
using MailFathom.Application.Paging;
using Xunit;

namespace MailFathom.Application.UnitTests.Paging;

/// <summary>Covers the encoding every keyset cursor shares: what survives it, and what it refuses to read.</summary>
/// <remarks>
/// This is the codec seven cursor families reach through, so a defect here is a page served twice or skipped on all of
/// them at once. What is asserted is the round trip, the recorded text a client may be part-way through, and the
/// refusals: text this version did not issue is refused rather than read as a boundary nobody meant.
/// </remarks>
public sealed class KeysetCursorPayloadTests
{
    private const string Fingerprint = "abcdef0123456789";

    private const string RecordedDatedCursor =
        "MS42MzkyMjcyODYwMDAwMDAwMDAuMDE5ODkzZTU2YWQwN2JkMDlmMTE2YzNhMWQ1ZTRiMmYuYWJjZGVmMDEyMzQ1Njc4OQ";

    private const string RecordedUndatedCursor = "MS4tLjAxOTg5M2U1NmFkMDdiZDA5ZjExNmMzYTFkNWU0YjJmLmFiY2RlZjAxMjM0NTY3ODk";

    private static readonly DateTimeOffset Position = new(2026, 8, 19, 9, 30, 0, TimeSpan.Zero);

    private static readonly Guid Identity = new("019893e5-6ad0-7bd0-9f11-6c3a1d5e4b2f");

    /// <summary>The encoded text is what a client holds between two pages, so it is pinned rather than only round-tripped.</summary>
    [Fact]
    public void Encode_ARecordedPosition_ProducesTheTextThisFormatHasAlwaysIssued()
    {
        // Act
        var encoded = KeysetCursorPayload.At(Position, Identity, Fingerprint).Encode();

        // Assert
        Assert.Equal(RecordedDatedCursor, encoded);
    }

    /// <summary>A reading whose rows need not carry an instant writes the sentinel, and that text is pinned too.</summary>
    [Fact]
    public void Encode_NoPosition_ProducesTheRecordedSentinelText()
    {
        // Act
        var encoded = KeysetCursorPayload.At(null, Identity, Fingerprint).Encode();

        // Assert
        Assert.Equal(RecordedUndatedCursor, encoded);
    }

    [Fact]
    public void TryDecode_ARecordedCursor_ReadsBackEveryValueItCarried()
    {
        // Act
        var read = KeysetCursorPayload.TryDecode(RecordedDatedCursor, out var payload);

        // Assert
        Assert.True(read);
        Assert.Equal(Position, payload.Position);
        Assert.Equal(Identity, payload.Identity);
        Assert.Equal(Fingerprint, payload.FilterFingerprint);
    }

    [Fact]
    public void TryDecode_ARecordedCursorWithNoPosition_ReadsBackTheAbsence()
    {
        // Act
        var read = KeysetCursorPayload.TryDecode(RecordedUndatedCursor, out var payload);

        // Assert
        Assert.True(read);
        Assert.Null(payload.Position);
        Assert.Equal(Identity, payload.Identity);
    }

    /// <summary>The orderings compare instants, so the offset whoever wrote the row used must not reach the text.</summary>
    [Fact]
    public void Encode_TwoTimestampsNamingOneInstant_ProduceTheSameText()
    {
        // Arrange
        var elsewhere = Position.ToOffset(TimeSpan.FromHours(2));

        // Act
        var here = KeysetCursorPayload.At(Position, Identity, Fingerprint).Encode();
        var there = KeysetCursorPayload.At(elsewhere, Identity, Fingerprint).Encode();

        // Assert
        Assert.Equal(here, there);
    }

    /// <summary>A cursor is opaque, which is also what stops a client from reading a row's instant out of it at a glance.</summary>
    [Fact]
    public void Encode_AnyPosition_ProducesTextThatCarriesNoReadableField()
    {
        // Act
        var encoded = KeysetCursorPayload.At(Position, Identity, Fingerprint).Encode();

        // Assert
        Assert.DoesNotContain(Identity.ToString("N"), encoded, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("2026", encoded, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64url at all!!")]
    [InlineData("YWJj")]
    public void TryDecode_TextThisVersionDidNotIssue_IsRefusedRatherThanRead(string? text)
    {
        // Act
        var read = KeysetCursorPayload.TryDecode(text, out var payload);

        // Assert
        Assert.False(read);
        Assert.Equal(default, payload);
    }

    /// <summary>Text longer than a cursor ever is is refused unread rather than decoded into a buffer sized for it.</summary>
    [Fact]
    public void TryDecode_TextLongerThanAnIssuedCursor_IsRefusedUnread()
    {
        // Arrange
        var overlong = new string('A', KeysetCursorPayload.MaximumEncodedLength + 1);

        // Act
        var read = KeysetCursorPayload.TryDecode(overlong, out _);

        // Assert
        Assert.False(read);
    }

    /// <summary>The version leads the payload so a cursor an older or newer layout issued is refused, never misread.</summary>
    [Theory]
    [InlineData("2.639227286000000000.019893e56ad07bd09f116c3a1d5e4b2f.abcdef0123456789")]
    [InlineData("1.639227286000000000.019893e56ad07bd09f116c3a1d5e4b2f")]
    [InlineData("1.639227286000000000.019893e56ad07bd09f116c3a1d5e4b2f.abcdef0123456789.extra")]
    public void TryDecode_APayloadThisLayoutDidNotWrite_IsRefused(string payload)
    {
        // Act, Assert
        Assert.False(KeysetCursorPayload.TryDecode(Encoded(payload), out _));
    }

    /// <summary>Every field is validated before a payload is produced, so no boundary decodes only partially.</summary>
    [Theory]
    [InlineData("1.not-a-tick-count.019893e56ad07bd09f116c3a1d5e4b2f.abcdef0123456789")]
    [InlineData("1.-1.019893e56ad07bd09f116c3a1d5e4b2f.abcdef0123456789")]
    [InlineData("1.9223372036854775807.019893e56ad07bd09f116c3a1d5e4b2f.abcdef0123456789")]
    [InlineData("1.639227286000000000.not-a-guid.abcdef0123456789")]
    [InlineData("1.639227286000000000.00000000000000000000000000000000.abcdef0123456789")]
    [InlineData("1.639227286000000000.019893e56ad07bd09f116c3a1d5e4b2f.")]
    public void TryDecode_AFieldNoWalkCouldHaveWritten_IsRefused(string payload)
    {
        // Act, Assert
        Assert.False(KeysetCursorPayload.TryDecode(Encoded(payload), out _));
    }

    /// <summary>A payload proves which walk it belongs to, so it is refused without the fingerprint that says so.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void At_ABlankFingerprint_IsRefused(string fingerprint)
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => KeysetCursorPayload.At(Position, Identity, fingerprint));
    }

    [Fact]
    public void At_NoFingerprint_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => KeysetCursorPayload.At(Position, Identity, null!));
    }

    private static string Encoded(string payload) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload));
}
