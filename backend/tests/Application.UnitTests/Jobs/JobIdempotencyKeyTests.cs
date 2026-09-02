// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using Xunit;

namespace MailFathom.Application.UnitTests.Jobs;

public sealed class JobIdempotencyKeyTests
{
    /// <summary>The key is the identity of one execution, so two keys composed from the same trigger have to compare equal.</summary>
    [Fact]
    public void Create_TheSameComposedText_ProducesEqualKeys()
    {
        // Act
        var key = JobIdempotencyKey.Create("account-a/inbox#1/12345/4711");
        var sameKey = JobIdempotencyKey.Create("account-a/inbox#1/12345/4711");

        // Assert
        Assert.Equal(key, sameKey);
        Assert.Equal("account-a/inbox#1/12345/4711", key.Value);
    }

    [Fact]
    public void Create_SurroundingWhitespace_IsNormalizedAway()
    {
        // Act
        var key = JobIdempotencyKey.Create("  account-a/inbox#1  ");

        // Assert
        Assert.Equal("account-a/inbox#1", key.Value);
        Assert.Equal(JobIdempotencyKey.Create("account-a/inbox#1"), key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankText_IsRefused(string value)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => JobIdempotencyKey.Create(value));
    }

    [Fact]
    public void Create_NoText_IsRefused()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => JobIdempotencyKey.Create(null!));
    }

    /// <summary>The bound is the column's and the index's, so a key over it would be truncated or refused by the database instead.</summary>
    [Fact]
    public void Create_TextLongerThanTheBound_IsRefused()
    {
        // Arrange
        var overLongKey = new string('k', JobIdempotencyKey.MaximumLength + 1);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => JobIdempotencyKey.Create(overLongKey));
    }

    [Fact]
    public void Create_TextExactlyAtTheBound_IsAccepted()
    {
        // Arrange
        var longestKey = new string('k', JobIdempotencyKey.MaximumLength);

        // Act
        var key = JobIdempotencyKey.Create(longestKey);

        // Assert
        Assert.Equal(longestKey, key.Value);
    }

    /// <summary>An operator reads this text when they ask what is stuck, and a control character would make it unreadable there.</summary>
    [Theory]
    [InlineData("account-a\n/inbox")]
    [InlineData("account-a\0")]
    public void Create_TextCarryingAControlCharacter_IsRefused(string value)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => JobIdempotencyKey.Create(value));
    }
}
