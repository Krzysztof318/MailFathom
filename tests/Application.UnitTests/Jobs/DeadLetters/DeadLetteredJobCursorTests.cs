// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.DeadLetters;
using Xunit;

namespace MailFathom.Application.UnitTests.Jobs.DeadLetters;

/// <summary>Covers the boundary a continued reading of the dead letters resumes from.</summary>
/// <remarks>
/// The set moves while it is read — a worker dead-letters another job, an operator retries one from a second terminal —
/// so what these assert is that the boundary survives a round trip intact and that anything else is refused rather than
/// read as a position in a reading it does not belong to.
/// </remarks>
public sealed class DeadLetteredJobCursorTests
{
    private static readonly DateTimeOffset StoppedAt = new(2026, 8, 13, 9, 30, 0, TimeSpan.Zero);

    private static readonly JobId Job = JobId.Create(new Guid("2f1c1d6c-6f0b-4a5e-9f3d-0f9b2a5c7e11"));

    /// <summary>A cursor is only useful if the next request reads back exactly the position the last page ended on.</summary>
    [Fact]
    public void TryDecode_ACursorThisVersionIssued_ReadsBackTheSamePosition()
    {
        // Arrange
        var encoded = DeadLetteredJobCursor.After(StoppedAt, Job, "fingerprint").Encode();

        // Act
        var decoded = DeadLetteredJobCursor.TryDecode(encoded, out var cursor);

        // Assert
        Assert.True(decoded);
        Assert.Equal(StoppedAt, cursor.DeadLetteredAt);
        Assert.Equal(Job, cursor.JobId);
        Assert.Equal("fingerprint", cursor.FilterFingerprint);
    }

    /// <summary>
    /// The instant is written as its UTC tick count, so two timestamps naming the same moment in different offsets are
    /// one boundary rather than two that compare differently against the order the reading is taken in.
    /// </summary>
    [Fact]
    public void Encode_TheSameInstantInAnotherOffset_ProducesTheSameCursor()
    {
        // Arrange
        var elsewhere = StoppedAt.ToOffset(TimeSpan.FromHours(2));

        // Act
        var encoded = DeadLetteredJobCursor.After(elsewhere, Job, "fingerprint").Encode();

        // Assert
        Assert.Equal(DeadLetteredJobCursor.After(StoppedAt, Job, "fingerprint").Encode(), encoded);
    }

    /// <summary>Anything a client built for itself is refused, because a client that cannot read a cursor does not write one.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a cursor at all")]
    [InlineData("MS4yLjM")]
    public void TryDecode_TextThisVersionDidNotIssue_IsRefused(string? text)
    {
        // Arrange, Act
        var decoded = DeadLetteredJobCursor.TryDecode(text, out _);

        // Assert
        Assert.False(decoded);
    }

    /// <summary>A cursor longer than the bound is refused unread, so no caller can make the decoder work for it.</summary>
    [Fact]
    public void TryDecode_TextLongerThanTheBound_IsRefusedWithoutBeingDecoded()
    {
        // Arrange
        var overlong = new string('a', DeadLetteredJobCursor.MaximumEncodedLength + 1);

        // Act
        var decoded = DeadLetteredJobCursor.TryDecode(overlong, out _);

        // Assert
        Assert.False(decoded);
    }
}
