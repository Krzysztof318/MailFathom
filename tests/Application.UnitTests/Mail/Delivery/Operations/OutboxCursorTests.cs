// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Domain.Delivery;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Operations;

/// <summary>Covers what an outbox cursor carries across an encoding, and what it refuses to be read from.</summary>
/// <remarks>
/// A cursor is the boundary a page continues from, so anything it loses or misreads is a send served twice or skipped
/// entirely. What is asserted is the round trip and the refusals: text this version did not issue is refused rather than
/// read as a position nobody meant.
/// </remarks>
public sealed class OutboxCursorTests
{
    private static readonly DateTimeOffset RecordedAt = new(2026, 8, 19, 9, 30, 0, TimeSpan.Zero);

    private static readonly OutgoingEmailId Send = OutgoingEmailId.Create(Guid.CreateVersion7(RecordedAt));

    [Fact]
    public void TryDecode_ACursorThisVersionEncoded_ReadsBackEveryValueItCarried()
    {
        // Arrange
        var encoded = OutboxCursor.After(RecordedAt, Send, "abcdef0123456789").Encode();

        // Act
        var read = OutboxCursor.TryDecode(encoded, out var cursor);

        // Assert
        Assert.True(read);
        Assert.Equal(RecordedAt, cursor.RecordedAt);
        Assert.Equal(Send, cursor.OutgoingEmailId);
        Assert.Equal("abcdef0123456789", cursor.FilterFingerprint);
    }

    /// <summary>The instant is compared as an instant, so an offset the caller happened to write it in changes nothing.</summary>
    [Fact]
    public void Encode_TwoTimestampsNamingOneInstant_ProduceTheSameCursor()
    {
        // Arrange
        var elsewhere = RecordedAt.ToOffset(TimeSpan.FromHours(2));

        // Act
        var here = OutboxCursor.After(RecordedAt, Send, "fingerprint").Encode();
        var there = OutboxCursor.After(elsewhere, Send, "fingerprint").Encode();

        // Assert
        Assert.Equal(here, there);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64url at all!!")]
    [InlineData("YWJj")]
    public void TryDecode_TextThisVersionDidNotIssue_IsRefusedRatherThanRead(string? text)
    {
        // Act
        var read = OutboxCursor.TryDecode(text, out var cursor);

        // Assert
        Assert.False(read);
        Assert.Equal(default, cursor);
    }

    /// <summary>A cursor longer than the reading ever issues is refused unread rather than decoded into a buffer sized for it.</summary>
    [Fact]
    public void TryDecode_TextLongerThanAnIssuedCursor_IsRefusedUnread()
    {
        // Arrange
        var overlong = new string('A', OutboxCursor.MaximumEncodedLength + 1);

        // Act
        var read = OutboxCursor.TryDecode(overlong, out _);

        // Assert
        Assert.False(read);
    }

    /// <summary>A cursor proves which walk it belongs to, so it is refused without the fingerprint that says so.</summary>
    [Fact]
    public void After_ABlankFingerprint_IsRefused()
    {
        // Act and assert
        Assert.Throws<ArgumentException>(() => OutboxCursor.After(RecordedAt, Send, "  "));
    }
}
