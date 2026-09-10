// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the shared answering ledger, whose two upserts every spend-ceiling test is measured against.</summary>
/// <remarks>
/// A fault here reports somebody else's arrangement: a store that admitted every run would let a tracker test pass
/// with no ceiling binding at all, and one that shared a row between periods would hide a roll-over that never
/// happened.
/// </remarks>
public sealed class InMemoryMailAnsweringSpendPeriodStoreTests
{
    private static readonly DateTimeOffset Period = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TryAdmitRunAsync_APeriodWithAnAllowanceLeft_AdmitsAndReportsTheRunItBecame()
    {
        // Arrange
        var periods = new InMemoryMailAnsweringSpendPeriodStore();

        // Act
        var first = await periods.TryAdmitRunAsync(Period, 2, 1000, TestContext.Current.CancellationToken);
        var second = await periods.TryAdmitRunAsync(Period, 2, 1000, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, first);
        Assert.Equal(2, second);
        Assert.Equal(1, periods.PeriodCount);
    }

    /// <summary>A refusal answers with no run at all, which is how a caller tells it apart from an admission.</summary>
    [Fact]
    public async Task TryAdmitRunAsync_APeriodThatHasAdmittedItsRuns_AnswersWithNoRun()
    {
        // Arrange
        var periods = new InMemoryMailAnsweringSpendPeriodStore();
        await periods.TryAdmitRunAsync(Period, 1, 1000, TestContext.Current.CancellationToken);

        // Act
        var refused = await periods.TryAdmitRunAsync(Period, 1, 1000, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, refused);
        Assert.Equal(1, periods.Spent(Period).Runs);
    }

    [Fact]
    public async Task TryAdmitRunAsync_APeriodThatHasConsumedItsTokens_AnswersWithNoRun()
    {
        // Arrange
        var periods = new InMemoryMailAnsweringSpendPeriodStore();
        await periods.RecordSpendAsync(Period, 1000, TestContext.Current.CancellationToken);

        // Act
        var refused = await periods.TryAdmitRunAsync(Period, 100, 1000, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, refused);
    }

    [Fact]
    public async Task RecordSpendAsync_SeveralCallsInOnePeriod_AddsUpToWhatThePeriodHasConsumed()
    {
        // Arrange
        var periods = new InMemoryMailAnsweringSpendPeriodStore();

        // Act
        await periods.RecordSpendAsync(Period, 70, TestContext.Current.CancellationToken);
        var consumed = await periods.RecordSpendAsync(Period, 30, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(100L, consumed);
        Assert.Equal(100L, periods.Spent(Period).Tokens);
    }

    /// <summary>A later window is a different key, so nothing has to be reset for an allowance to return.</summary>
    [Fact]
    public async Task TryAdmitRunAsync_TheNextPeriod_StartsUnspent()
    {
        // Arrange
        var periods = new InMemoryMailAnsweringSpendPeriodStore();
        await periods.TryAdmitRunAsync(Period, 1, 1000, TestContext.Current.CancellationToken);
        await periods.RecordSpendAsync(Period, 900, TestContext.Current.CancellationToken);

        // Act
        var next = Period + TimeSpan.FromHours(1);
        var admitted = await periods.TryAdmitRunAsync(next, 1, 1000, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, admitted);
        Assert.Equal(2, periods.PeriodCount);
        Assert.Equal((1, 0L), periods.Spent(next));
        Assert.Equal((1, 900L), periods.Spent(Period));
    }

    [Fact]
    public async Task TryAdmitRunAsync_ACeilingThatCouldAdmitNothing_IsRefused()
    {
        // Arrange
        var periods = new InMemoryMailAnsweringSpendPeriodStore();

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => periods.TryAdmitRunAsync(Period, 0, 1000, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => periods.TryAdmitRunAsync(Period, 1, 0, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => periods.RecordSpendAsync(Period, -1, TestContext.Current.CancellationToken));
    }
}
