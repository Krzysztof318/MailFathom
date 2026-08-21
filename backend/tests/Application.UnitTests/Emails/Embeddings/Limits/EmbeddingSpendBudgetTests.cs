// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings.Limits;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Embeddings.Limits;

/// <summary>Covers the window a ceiling is counted over, which every process has to place identically.</summary>
public sealed class EmbeddingSpendBudgetTests
{
    [Fact]
    public void Create_ACeilingOfZero_BoundsNothing()
    {
        // Act
        var budget = EmbeddingSpendBudget.Create(maxInputCharactersPerPeriod: 0, TimeSpan.FromHours(1));

        // Assert
        Assert.True(budget.IsUnbounded);
    }

    [Fact]
    public void Create_APositiveCeiling_Bounds()
    {
        // Act
        var budget = EmbeddingSpendBudget.Create(maxInputCharactersPerPeriod: 1_000, TimeSpan.FromHours(6));

        // Assert
        Assert.False(budget.IsUnbounded);
        Assert.Equal(1_000, budget.MaxInputCharactersPerPeriod);
        Assert.Equal(TimeSpan.FromHours(6), budget.Period);
    }

    [Theory]
    [InlineData(-1, 3600)]
    [InlineData(1_000, 0)]
    [InlineData(1_000, -1)]
    public void Create_ABudgetThatCouldNotBoundASpend_IsRefused(long ceiling, int periodSeconds)
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EmbeddingSpendBudget.Create(ceiling, TimeSpan.FromSeconds(periodSeconds)));
    }

    /// <summary>
    /// Every process and every restart has to agree on where a period begins without anything being stored to say so,
    /// which is what anchoring the window at the epoch buys: two instants inside one window place on the same start.
    /// </summary>
    [Fact]
    public void PeriodStartAt_TwoInstantsInsideOneWindow_PlaceOnTheSameStart()
    {
        // Arrange
        var budget = EmbeddingSpendBudget.Create(maxInputCharactersPerPeriod: 1_000, TimeSpan.FromDays(1));
        var morning = new DateTimeOffset(2026, 8, 8, 6, 30, 0, TimeSpan.Zero);
        var evening = new DateTimeOffset(2026, 8, 8, 23, 59, 59, TimeSpan.Zero);

        // Act, Assert
        Assert.Equal(new DateTimeOffset(2026, 8, 8, 0, 0, 0, TimeSpan.Zero), budget.PeriodStartAt(morning));
        Assert.Equal(budget.PeriodStartAt(morning), budget.PeriodStartAt(evening));
    }

    /// <summary>An instant read from another offset is the same instant, so it belongs to the same period.</summary>
    [Fact]
    public void PeriodStartAt_AnInstantWrittenInAnotherOffset_PlacesOnTheSameStart()
    {
        // Arrange
        var budget = EmbeddingSpendBudget.Create(maxInputCharactersPerPeriod: 1_000, TimeSpan.FromDays(1));
        var utc = new DateTimeOffset(2026, 8, 8, 6, 30, 0, TimeSpan.Zero);
        var elsewhere = utc.ToOffset(TimeSpan.FromHours(5));

        // Act, Assert
        Assert.Equal(budget.PeriodStartAt(utc), budget.PeriodStartAt(elsewhere));
    }

    [Fact]
    public void PeriodEndAt_AnInstantInsideAWindow_IsWhenTheNextPeriodBegins()
    {
        // Arrange
        var budget = EmbeddingSpendBudget.Create(maxInputCharactersPerPeriod: 1_000, TimeSpan.FromHours(6));
        var instant = new DateTimeOffset(2026, 8, 8, 7, 15, 0, TimeSpan.Zero);

        // Act, Assert
        Assert.Equal(new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero), budget.PeriodEndAt(instant));
        Assert.Equal(budget.PeriodStartAt(instant) + budget.Period, budget.PeriodEndAt(instant));
    }
}
