// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Text;
using System.Text;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Mailboxes;

/// <summary>Covers the continuation cursor: what it preserves across an encode, and what it refuses to read back.</summary>
public sealed class EmailTimelineCursorTests
{
    private const string Fingerprint = "AAECAwQFBgcICQoLDA0ODw";

    private static readonly StoredEmailId BoundaryId = StoredEmailId.Create(Guid.CreateVersion7());

    [Fact]
    public void Encode_DatedPosition_RoundTripsThePositionAndTheFingerprint()
    {
        // Arrange
        var position = new EmailTimelinePosition(new DateTimeOffset(2026, 7, 24, 8, 0, 0, TimeSpan.Zero), BoundaryId);

        // Act
        var decoded = Decode(EmailTimelineCursor.After(position, Fingerprint).Encode());

        // Assert
        Assert.Equal(position, decoded.Position);
        Assert.Equal(Fingerprint, decoded.FilterFingerprint);
    }

    [Fact]
    public void Encode_UndatedPosition_RoundTripsTheAbsentTimestamp()
    {
        // Arrange
        var position = new EmailTimelinePosition(null, BoundaryId);

        // Act
        var decoded = Decode(EmailTimelineCursor.After(position, Fingerprint).Encode());

        // Assert
        Assert.Null(decoded.Position.ReceivedAt);
        Assert.Equal(BoundaryId, decoded.Position.StoredEmailId);
    }

    /// <summary>
    /// The order compares instants, so a boundary written in another offset must decode to the same one. Encoding the
    /// offset would otherwise make two cursors for one row, and the page after each of them would differ.
    /// </summary>
    [Fact]
    public void Encode_TimestampsWritingTheSameInstantInDifferentOffsets_ProduceTheSameCursor()
    {
        // Arrange
        var inUtc = new EmailTimelinePosition(new DateTimeOffset(2026, 7, 24, 8, 0, 0, TimeSpan.Zero), BoundaryId);
        var inLocalOffset = new EmailTimelinePosition(
            new DateTimeOffset(2026, 7, 24, 10, 0, 0, TimeSpan.FromHours(2)),
            BoundaryId);

        // Act
        var fromUtc = EmailTimelineCursor.After(inUtc, Fingerprint).Encode();
        var fromLocalOffset = EmailTimelineCursor.After(inLocalOffset, Fingerprint).Encode();

        // Assert
        Assert.Equal(fromUtc, fromLocalOffset);
    }

    /// <summary>A cursor is opaque, which is also what stops a client from reading a mail timestamp out of it at a glance.</summary>
    [Fact]
    public void Encode_AnyPosition_ProducesTextThatCarriesNoReadableField()
    {
        // Arrange
        var position = new EmailTimelinePosition(new DateTimeOffset(2026, 7, 24, 8, 0, 0, TimeSpan.Zero), BoundaryId);

        // Act
        var encoded = EmailTimelineCursor.After(position, Fingerprint).Encode();

        // Assert
        Assert.DoesNotContain(BoundaryId.Value.ToString("N"), encoded, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("2026", encoded, StringComparison.Ordinal);
    }

    [Fact]
    public void After_PositionWithoutAStoredEmailIdentity_IsRejected()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() =>
            EmailTimelineCursor.After(new EmailTimelinePosition(null, default), Fingerprint));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void After_BlankFingerprint_IsRejected(string fingerprint)
    {
        // Arrange
        var position = new EmailTimelinePosition(null, BoundaryId);

        // Act, Assert
        Assert.Throws<ArgumentException>(() => EmailTimelineCursor.After(position, fingerprint));
    }

    [Fact]
    public void After_NoFingerprint_IsRejected()
    {
        // Arrange
        var position = new EmailTimelinePosition(null, BoundaryId);

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => EmailTimelineCursor.After(position, null!));
    }

    [Fact]
    public void TryDecode_TextLongerThanACursorEverIs_IsRefusedWithoutDecoding()
    {
        // Arrange
        var overlyLongText = new string('A', EmailTimelineCursor.MaximumEncodedLength + 1);

        // Act
        var decoded = EmailTimelineCursor.TryDecode(overlyLongText, out var cursor);

        // Assert
        Assert.False(decoded);
        Assert.Equal(default, cursor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void TryDecode_NoText_IsRefused(string? text)
    {
        // Act, Assert
        Assert.False(EmailTimelineCursor.TryDecode(text, out _));
    }

    /// <summary>A tick count no date can hold is refused rather than allowed to fault the position it would build.</summary>
    [Fact]
    public void TryDecode_ReceivedTicksBeyondTheCalendar_IsRefused()
    {
        // Arrange
        var beyondTheCalendar = EncodedPayload($"1.{long.MaxValue}.{BoundaryId.Value:N}.{Fingerprint}");

        // Act, Assert
        Assert.False(EmailTimelineCursor.TryDecode(beyondTheCalendar, out _));
    }

    private static EmailTimelineCursor Decode(string encoded)
    {
        Assert.True(EmailTimelineCursor.TryDecode(encoded, out var cursor));

        return cursor;
    }

    private static string EncodedPayload(string payload) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload));
}
