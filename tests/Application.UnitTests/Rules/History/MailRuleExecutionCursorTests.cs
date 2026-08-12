// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.History;
using Xunit;

namespace MailFathom.Application.UnitTests.Rules.History;

/// <summary>Covers the boundary one page of the rule history hands to the next, and what a presented one is refused for.</summary>
public sealed class MailRuleExecutionCursorTests
{
    private const string Fingerprint = "abcdef0123456789";

    private static readonly DateTimeOffset Noon = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Encode_ABoundary_RoundTripsThroughTheEncodedForm()
    {
        // Arrange
        var executionId = MailRuleExecutionId.Create(Guid.CreateVersion7(Noon));
        var cursor = MailRuleExecutionCursor.After(Noon, executionId, Fingerprint);

        // Act
        var decoded = MailRuleExecutionCursor.TryDecode(cursor.Encode(), out var read);

        // Assert
        Assert.True(decoded);
        Assert.Equal((Noon, executionId, Fingerprint), (read.EvaluatedAt, read.ExecutionId, read.FilterFingerprint));
    }

    /// <summary>The instant is compared as its UTC ticks, so two offsets naming one instant continue one walk.</summary>
    [Fact]
    public void Encode_TheSameInstantInTwoOffsets_ProducesOneCursor()
    {
        // Arrange
        var executionId = MailRuleExecutionId.Create(Guid.CreateVersion7(Noon));

        // Act
        var utc = MailRuleExecutionCursor.After(Noon, executionId, Fingerprint).Encode();
        var offset = MailRuleExecutionCursor
            .After(Noon.ToOffset(TimeSpan.FromHours(2)), executionId, Fingerprint)
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
        var decoded = MailRuleExecutionCursor.TryDecode(text, out var cursor);

        // Assert
        Assert.False(decoded);
        Assert.Equal(default, cursor);
    }

    /// <summary>A cursor longer than one this system issues is refused before it is read at all.</summary>
    [Fact]
    public void TryDecode_TextLongerThanACursor_IsRefusedUnread()
    {
        // Act
        var decoded = MailRuleExecutionCursor.TryDecode(
            new string('a', MailRuleExecutionCursor.MaximumEncodedLength + 1),
            out _);

        // Assert
        Assert.False(decoded);
    }

    [Fact]
    public void After_ABlankFingerprint_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => MailRuleExecutionCursor.After(
            Noon,
            MailRuleExecutionId.New(),
            "   "));
    }
}
