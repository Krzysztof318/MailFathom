// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Calendar;
using MailFathom.Domain.Calendar;
using Xunit;

namespace MailFathom.Application.UnitTests.Calendar;

/// <summary>Covers the bounds every window over a calendar is held to before a store reads under it.</summary>
public sealed class CalendarEventQueryTests
{
    private static readonly DateTimeOffset From = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Until = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_NoCount_IsServedTheDefault()
    {
        // Act
        var query = CalendarEventQuery.Create(From, Until, origin: null, count: null);

        // Assert
        Assert.Equal(CalendarEventQuery.DefaultCount, query.Count);
    }

    /// <summary>A window that closes as it opens, or before it, answers about no span at all.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_AWindowThatDoesNotCloseAfterItOpens_IsRefused(int daysFromOpening)
    {
        // Act
        var refusal = Assert.Throws<ArgumentOutOfRangeException>(() =>
            CalendarEventQuery.Create(From, From.AddDays(daysFromOpening), origin: null, count: null));

        // Assert
        Assert.Equal("until", refusal.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(CalendarEventQuery.MaximumCount + 1)]
    public void Create_ACountOutsideTheBound_IsRefused(int count)
    {
        // Act
        var refusal = Assert.Throws<ArgumentOutOfRangeException>(() =>
            CalendarEventQuery.Create(From, Until, origin: null, count));

        // Assert
        Assert.Equal("count", refusal.ParamName);
    }

    [Fact]
    public void Create_UndeclaredOrigin_IsRefused()
    {
        // Act
        var refusal = Assert.Throws<ArgumentOutOfRangeException>(() =>
            CalendarEventQuery.Create(From, Until, (CalendarEventOrigin)99, count: null));

        // Assert
        Assert.Equal("origin", refusal.ParamName);
    }

    /// <summary>The two halves are drawn in different places, so narrowing to one of them is what a reader asks for.</summary>
    [Fact]
    public void Create_ANarrowedOrigin_IsCarriedToTheStore()
    {
        // Act
        var query = CalendarEventQuery.Create(From, Until, CalendarEventOrigin.Proposed, count: 10);

        // Assert
        Assert.Equal(CalendarEventOrigin.Proposed, query.Origin);
        Assert.Equal(10, query.Count);
    }
}
