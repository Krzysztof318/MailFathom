// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence;

/// <summary>
/// Covers the split every keyset reader ends on. Both answers it gives are ones a page is wrong about silently: a page
/// carrying the row that was read only to be counted serves a row twice, and a cursor handed out at the end of the set
/// walks the caller into an empty page it has to ask for to discover.
/// </summary>
public sealed class KeysetPageSplitTests
{
    /// <summary>The extra row is what says a page follows, and it never reaches the page itself.</summary>
    [Fact]
    public void Of_AReadThatReachedPastThePage_KeepsThePageAndSaysAnotherFollows()
    {
        // Act
        var (page, hasMore) = KeysetPageSplit.Of([1, 2, 3, 4], pageSize: 3);

        // Assert
        Assert.Equal([1, 2, 3], page);
        Assert.True(hasMore);
    }

    /// <summary>A read that exactly filled the page reached no further, so there is nothing to resume from.</summary>
    [Fact]
    public void Of_AReadThatExactlyFilledThePage_SaysNothingFollows()
    {
        // Act
        var (page, hasMore) = KeysetPageSplit.Of([1, 2, 3], pageSize: 3);

        // Assert
        Assert.Equal([1, 2, 3], page);
        Assert.False(hasMore);
    }

    /// <summary>The end of the set, where a cursor would send the caller after rows that do not exist.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Of_AReadThatFellShortOfThePage_SaysNothingFollows(int readCount)
    {
        // Arrange
        var readRows = Enumerable.Range(1, readCount).ToArray();

        // Act
        var (page, hasMore) = KeysetPageSplit.Of(readRows, pageSize: 3);

        // Assert
        Assert.Equal(readRows, page);
        Assert.False(hasMore);
    }

    /// <summary>
    /// A following page implies a full one, which is what lets a caller take the boundary row from the end of the page
    /// without a second guard against it being empty.
    /// </summary>
    [Fact]
    public void Of_AFollowingPage_LeavesTheBoundaryRowAtTheEndOfThePage()
    {
        // Act
        var (page, hasMore) = KeysetPageSplit.Of([10, 20, 30], pageSize: 2);

        // Assert
        Assert.True(hasMore);
        Assert.Equal(20, page[^1]);
    }

    /// <summary>A page of no rows is a bound no reader composes, and it would make the split meaningless.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Of_APageBoundOfNothing_IsRefused(int pageSize)
    {
        // Act
        var refusal = Record.Exception(() => KeysetPageSplit.Of([1, 2], pageSize));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(refusal);
    }
}
