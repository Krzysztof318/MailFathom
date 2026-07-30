// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Emails;
using Xunit;

namespace MailMcp.Domain.UnitTests;

/// <summary>Pins the total order every mailbox timeline is paged over, including where undated mail lands.</summary>
public sealed class EmailTimelinePositionTests
{
    /// <summary>The leading octet has its high bit set, which is where an ordering that reads it as signed would differ.</summary>
    private static readonly Guid IdentifierLeadingWithTheHighBitSet = new("80000000-0000-0000-0000-000000000000");

    private static readonly Guid IdentifierLeadingWithZero = new("00000000-0000-0000-0000-000000000001");

    [Fact]
    public void NewestFirst_MessagesCarryingReceivedTimestamps_OrdersTheNewestFirst()
    {
        // Arrange
        var positions = Enumerable.Range(0, 4)
            .Select(dayOffset => new EmailTimelinePosition(
                new DateTimeOffset(2026, 7, 20, 8, 0, 0, TimeSpan.Zero).AddDays(dayOffset),
                StoredEmailId.Create(Guid.CreateVersion7())))
            .ToArray();

        // Act
        var ordered = positions.Order(EmailTimelinePosition.NewestFirst).ToArray();

        // Assert
        Assert.Equal(positions.Reverse(), ordered);
    }

    [Fact]
    public void NewestFirst_MessageWithoutAReceivedTimestamp_SortsAfterEveryDatedMessage()
    {
        // Arrange
        var undated = new EmailTimelinePosition(null, StoredEmailId.Create(Guid.CreateVersion7()));
        var oldest = new EmailTimelinePosition(
            new DateTimeOffset(1998, 1, 1, 0, 0, 0, TimeSpan.Zero),
            StoredEmailId.Create(Guid.CreateVersion7()));
        var newest = new EmailTimelinePosition(
            new DateTimeOffset(2026, 7, 24, 8, 0, 0, TimeSpan.Zero),
            StoredEmailId.Create(Guid.CreateVersion7()));

        // Act
        var ordered = new[] { undated, oldest, newest }.Order(EmailTimelinePosition.NewestFirst).ToArray();

        // Assert
        Assert.Equal([newest, oldest, undated], ordered);
    }

    [Fact]
    public void NewestFirst_MessagesShareAReceivedTimestamp_BreaksTheTieOnTheIdentifier()
    {
        // Arrange
        var sharedTimestamp = new DateTimeOffset(2026, 7, 24, 8, 0, 0, TimeSpan.Zero);
        var lower = new EmailTimelinePosition(sharedTimestamp, StoredEmailId.Create(IdentifierLeadingWithZero));
        var higher = new EmailTimelinePosition(sharedTimestamp, StoredEmailId.Create(IdentifierLeadingWithTheHighBitSet));

        // Act
        var ordered = new[] { lower, higher }.Order(EmailTimelinePosition.NewestFirst).ToArray();

        // Assert
        Assert.Equal([higher, lower], ordered);
    }

    /// <summary>
    /// The tiebreaker must read the leading octet the way a <c>uuid</c> column does, unsigned, because a page boundary
    /// computed here is resumed from by a query planned against that column.
    /// </summary>
    [Fact]
    public void NewestFirst_UndatedMessagesWhoseIdentifiersDifferInTheLeadingOctet_ReadThatOctetUnsigned()
    {
        // Arrange
        var lower = new EmailTimelinePosition(null, StoredEmailId.Create(IdentifierLeadingWithZero));
        var higher = new EmailTimelinePosition(null, StoredEmailId.Create(IdentifierLeadingWithTheHighBitSet));

        // Act
        var ordered = new[] { lower, higher }.Order(EmailTimelinePosition.NewestFirst).ToArray();

        // Assert
        Assert.Equal([higher, lower], ordered);
    }

    /// <summary>The other direction is the same order reversed, which is what makes one cursor mean one boundary in both.</summary>
    [Fact]
    public void OldestFirst_MessagesCarryingReceivedTimestamps_OrdersTheOldestFirst()
    {
        // Arrange
        var positions = Enumerable.Range(0, 4)
            .Select(dayOffset => new EmailTimelinePosition(
                new DateTimeOffset(2026, 7, 20, 8, 0, 0, TimeSpan.Zero).AddDays(dayOffset),
                StoredEmailId.Create(Guid.CreateVersion7())))
            .ToArray();

        // Act
        var ordered = positions.Order(EmailTimelinePosition.OldestFirst).ToArray();

        // Assert
        Assert.Equal(positions, ordered);
    }

    /// <summary>Undated mail leads when the oldest is read first, because that placement is the reverse of the other one.</summary>
    [Fact]
    public void OldestFirst_MessageWithoutAReceivedTimestamp_SortsBeforeEveryDatedMessage()
    {
        // Arrange
        var undated = new EmailTimelinePosition(null, StoredEmailId.Create(Guid.CreateVersion7()));
        var oldest = new EmailTimelinePosition(
            new DateTimeOffset(1998, 1, 1, 0, 0, 0, TimeSpan.Zero),
            StoredEmailId.Create(Guid.CreateVersion7()));
        var newest = new EmailTimelinePosition(
            new DateTimeOffset(2026, 7, 24, 8, 0, 0, TimeSpan.Zero),
            StoredEmailId.Create(Guid.CreateVersion7()));

        // Act
        var ordered = new[] { newest, oldest, undated }.Order(EmailTimelinePosition.OldestFirst).ToArray();

        // Assert
        Assert.Equal([undated, oldest, newest], ordered);
    }

    [Fact]
    public void OldestFirst_MessagesShareAReceivedTimestamp_BreaksTheTieOnTheIdentifierAscending()
    {
        // Arrange
        var sharedTimestamp = new DateTimeOffset(2026, 7, 24, 8, 0, 0, TimeSpan.Zero);
        var lower = new EmailTimelinePosition(sharedTimestamp, StoredEmailId.Create(IdentifierLeadingWithZero));
        var higher = new EmailTimelinePosition(sharedTimestamp, StoredEmailId.Create(IdentifierLeadingWithTheHighBitSet));

        // Act
        var ordered = new[] { higher, lower }.Order(EmailTimelinePosition.OldestFirst).ToArray();

        // Assert
        Assert.Equal([lower, higher], ordered);
    }

    [Theory]
    [InlineData(EmailTimelineDirection.NewestFirst)]
    [InlineData(EmailTimelineDirection.OldestFirst)]
    public void ComparerFor_EitherDirection_ReturnsThatDirectionsComparer(EmailTimelineDirection direction)
    {
        // Arrange
        var expected = direction is EmailTimelineDirection.NewestFirst
            ? EmailTimelinePosition.NewestFirst
            : EmailTimelinePosition.OldestFirst;

        // Act
        var comparer = EmailTimelinePosition.ComparerFor(direction);

        // Assert
        Assert.Same(expected, comparer);
    }

    [Fact]
    public void ComparerFor_ValueThatNamesNeitherEnd_IsRejected()
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EmailTimelinePosition.ComparerFor((EmailTimelineDirection)7));
    }

    [Fact]
    public void NewestFirst_TwoPositionsDescribingTheSameMessage_CompareEqual()
    {
        // Arrange
        var storedEmailId = StoredEmailId.Create(Guid.CreateVersion7());
        var receivedAt = new DateTimeOffset(2026, 7, 24, 8, 0, 0, TimeSpan.Zero);

        // Act
        var comparison = EmailTimelinePosition.NewestFirst.Compare(
            new EmailTimelinePosition(receivedAt, storedEmailId),
            new EmailTimelinePosition(receivedAt, storedEmailId));

        // Assert
        Assert.Equal(0, comparison);
    }

    [Fact]
    public void NewestFirst_TimestampsWritingTheSameInstantInDifferentOffsets_CompareEqual()
    {
        // Arrange
        var storedEmailId = StoredEmailId.Create(Guid.CreateVersion7());
        var inUtc = new EmailTimelinePosition(
            new DateTimeOffset(2026, 7, 24, 8, 0, 0, TimeSpan.Zero),
            storedEmailId);
        var inLocalOffset = new EmailTimelinePosition(
            new DateTimeOffset(2026, 7, 24, 10, 0, 0, TimeSpan.FromHours(2)),
            storedEmailId);

        // Act
        var comparison = EmailTimelinePosition.NewestFirst.Compare(inUtc, inLocalOffset);

        // Assert
        Assert.Equal(0, comparison);
    }
}
