// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailMcp.Application.Synchronization;
using Xunit;

namespace MailMcp.Application.UnitTests;

public sealed class ReconciliationWindowBudgetTests
{
    /// <summary>The reserve exists to stop newly stored mail from taking every window, so half of one is kept back.</summary>
    [Fact]
    public void NeverObservedShareOf_EnoughPreviouslyObservedToFillTheReserve_KeepsHalfTheWindowBack()
    {
        // Act
        var share = ReconciliationWindowBudget.NeverObservedShareOf(500, previouslyObservedCandidateCount: 4000);

        // Assert
        Assert.Equal(250, share);
    }

    /// <summary>A mailbox being synchronized for the first time has nothing to revisit, so none of its window is idle.</summary>
    [Fact]
    public void NeverObservedShareOf_NothingObservedBefore_GivesTheWholeWindowToUnobservedMail()
    {
        // Act
        var share = ReconciliationWindowBudget.NeverObservedShareOf(500, previouslyObservedCandidateCount: 0);

        // Assert
        Assert.Equal(500, share);
    }

    /// <summary>Only as much of the reserve as there is mail to fill it is held back, so a window is never left short.</summary>
    [Theory]
    [InlineData(10, 3, 7)]
    [InlineData(10, 5, 5)]
    [InlineData(10, 9, 5)]
    [InlineData(1, 4, 1)]
    [InlineData(2, 1, 1)]
    public void NeverObservedShareOf_FewerPreviouslyObservedThanTheReserve_HoldsBackOnlyWhatExists(
        int maxEmailCount,
        int previouslyObservedCandidateCount,
        int expectedShare)
    {
        // Act
        var share = ReconciliationWindowBudget.NeverObservedShareOf(maxEmailCount, previouslyObservedCandidateCount);

        // Assert
        Assert.Equal(expectedShare, share);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(10, -1)]
    public void NeverObservedShareOf_UnusableCounts_AreRejected(
        int maxEmailCount,
        int previouslyObservedCandidateCount)
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ReconciliationWindowBudget.NeverObservedShareOf(maxEmailCount, previouslyObservedCandidateCount));
    }
}
