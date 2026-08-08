// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Retrieval.AskMail.Audit;
using MailFathom.Domain.Answering.Audit;
using Xunit;

namespace MailFathom.Application.UnitTests.Retrieval.AskMail.Audit;

/// <summary>Covers the boundary one page hands to the next, and what a presented one is refused for.</summary>
public sealed class MailAnsweringAuditCursorTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Encode_ABoundary_RoundTripsThroughTheEncodedForm()
    {
        // Arrange
        var entryId = MailAnsweringAuditEntryId.Create(Guid.CreateVersion7(Noon));
        var cursor = MailAnsweringAuditCursor.After(Noon, entryId, "abcdef0123456789");

        // Act
        var decoded = MailAnsweringAuditCursor.TryDecode(cursor.Encode(), out var read);

        // Assert
        Assert.True(decoded);
        Assert.Equal((Noon, entryId, "abcdef0123456789"), (read.CompletedAt, read.EntryId, read.FilterFingerprint));
    }

    /// <summary>The instant is compared as its UTC ticks, so two offsets naming one instant continue one walk.</summary>
    [Fact]
    public void Encode_TheSameInstantInTwoOffsets_ProducesOneCursor()
    {
        // Arrange
        var entryId = MailAnsweringAuditEntryId.Create(Guid.CreateVersion7(Noon));

        // Act
        var utc = MailAnsweringAuditCursor.After(Noon, entryId, "abcdef0123456789").Encode();
        var offset = MailAnsweringAuditCursor
            .After(Noon.ToOffset(TimeSpan.FromHours(2)), entryId, "abcdef0123456789")
            .Encode();

        // Assert
        Assert.Equal(utc, offset);
    }

    /// <summary>A built cursor is how a caller would ask for a boundary this system never computed.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64url!!")]
    [InlineData("MS4xLjEuMQ")]
    public void TryDecode_TextThisVersionDidNotIssue_IsRefused(string? text)
    {
        // Act
        var decoded = MailAnsweringAuditCursor.TryDecode(text, out var cursor);

        // Assert
        Assert.False(decoded);
        Assert.Equal(default, cursor);
    }

    /// <summary>A cursor longer than one this system issues is refused before it is read at all.</summary>
    [Fact]
    public void TryDecode_TextLongerThanACursor_IsRefusedUnread()
    {
        // Act
        var decoded = MailAnsweringAuditCursor.TryDecode(
            new string('a', MailAnsweringAuditCursor.MaximumEncodedLength + 1),
            out _);

        // Assert
        Assert.False(decoded);
    }

    [Fact]
    public void After_ABlankFingerprint_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => MailAnsweringAuditCursor.After(
            Noon,
            MailAnsweringAuditEntryId.Create(Guid.CreateVersion7()),
            "   "));
    }
}
