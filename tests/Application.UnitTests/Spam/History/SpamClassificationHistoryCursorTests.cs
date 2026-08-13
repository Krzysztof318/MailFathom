// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Text;
using System.Text;
using MailFathom.Application.Spam.History;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Application.UnitTests.Spam.History;

/// <summary>Covers the round trip, and everything a presented cursor is refused for before it is read.</summary>
public sealed class SpamClassificationHistoryCursorTests
{
    private const string Fingerprint = "0123456789abcdef";

    private static readonly StoredEmailId Email =
        StoredEmailId.Create(Guid.Parse("0199a0c0-0000-7000-8000-0000000090a0"));

    private static readonly DateTimeOffset EvaluatedAt = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryDecode_ACursorThisTypeIssued_ReadsBackWhatItCarried()
    {
        // Arrange
        var issued = SpamClassificationHistoryCursor.After(EvaluatedAt, Email, Fingerprint);

        // Act
        var decoded = SpamClassificationHistoryCursor.TryDecode(issued.Encode(), out var cursor);

        // Assert
        Assert.True(decoded);
        Assert.Equal(EvaluatedAt, cursor.EvaluatedAt);
        Assert.Equal(Email, cursor.EmailId);
        Assert.Equal(Fingerprint, cursor.FilterFingerprint);
    }

    /// <summary>The instant is compared as ticks, so the same moment written at another offset encodes identically.</summary>
    [Fact]
    public void Encode_TheSameInstantAtAnotherOffset_ProducesTheSameCursor()
    {
        // Arrange
        var here = SpamClassificationHistoryCursor.After(EvaluatedAt, Email, Fingerprint);
        var elsewhere = SpamClassificationHistoryCursor.After(
            new DateTimeOffset(2026, 8, 12, 11, 0, 0, TimeSpan.FromHours(2)),
            Email,
            Fingerprint);

        // Act, Assert
        Assert.Equal(here.Encode(), elsewhere.Encode());
    }

    [Fact]
    public void After_ABlankFingerprint_IsRefused()
    {
        // Arrange, Act, Assert
        var failure = Assert.Throws<ArgumentException>(
            () => SpamClassificationHistoryCursor.After(EvaluatedAt, Email, "   "));

        Assert.Equal("filterFingerprint", failure.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64url!!")]
    public void TryDecode_TextNoPageIssued_IsRefused(string? text)
    {
        // Arrange, Act
        var decoded = SpamClassificationHistoryCursor.TryDecode(text, out var cursor);

        // Assert
        Assert.False(decoded);
        Assert.Equal(default, cursor);
    }

    [Fact]
    public void TryDecode_TextLongerThanTheBound_IsRefusedUnread()
    {
        // Arrange
        var overlong = new string('a', SpamClassificationHistoryCursor.MaximumEncodedLength + 1);

        // Act, Assert
        Assert.False(SpamClassificationHistoryCursor.TryDecode(overlong, out _));
    }

    [Theory]
    [InlineData("2.638000000000000000.0199a0c000007000800000000000abcd.0123456789abcdef")]
    [InlineData("1.not-a-number.0199a0c000007000800000000000abcd.0123456789abcdef")]
    [InlineData("1.-638000000000000000.0199a0c000007000800000000000abcd.0123456789abcdef")]
    [InlineData("1.638000000000000000.not-a-guid.0123456789abcdef")]
    [InlineData("1.638000000000000000.00000000000000000000000000000000.0123456789abcdef")]
    [InlineData("1.638000000000000000.0199a0c000007000800000000000abcd.")]
    [InlineData("1.638000000000000000.0199a0c000007000800000000000abcd")]
    public void TryDecode_APayloadThisVersionDidNotIssue_IsRefused(string payload)
    {
        // Arrange
        var encoded = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload));

        // Act, Assert
        Assert.False(SpamClassificationHistoryCursor.TryDecode(encoded, out _));
    }
}
