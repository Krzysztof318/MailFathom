// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Presentation;
using Xunit;

namespace MailFathom.Application.UnitTests.Discovery.Presentation;

/// <summary>Covers what a run says about one account it read.</summary>
public sealed class AccountCoverageTests
{
    private static readonly DateTimeOffset ObservedAt = PresentationPlanExample.ObservedAt;

    /// <summary>The two ends bound one range, so half of one describes nothing a reader can draw.</summary>
    [Fact]
    public void Constructor_OneEndOfTheRangeWithoutTheOther_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => new AccountCoverage(
            Text("work"),
            PresentationFreshness.CurrentAt(ObservedAt),
            ObservedAt.AddDays(-7),
            latestReceivedAt: null));
    }

    [Fact]
    public void Constructor_TheOldestMailArrivingAfterTheNewest_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => new AccountCoverage(
            Text("work"),
            PresentationFreshness.CurrentAt(ObservedAt),
            ObservedAt,
            ObservedAt.AddDays(-7)));
    }

    /// <summary>An account that was read and yielded nothing is not an account that was skipped.</summary>
    [Fact]
    public void Constructor_AnAccountTheRunDrewNothingFrom_ReportsItsFreshnessWithoutDates()
    {
        // Act
        var coverage = new AccountCoverage(
            Text("archive"),
            PresentationFreshness.StaleSince(ObservedAt),
            earliestReceivedAt: null,
            latestReceivedAt: null);

        // Assert
        Assert.Equal("archive", coverage.Account.Value);
        Assert.Equal(PresentationStaleness.Stale, coverage.Freshness.Staleness);
        Assert.Null(coverage.EarliestReceivedAt);
        Assert.Null(coverage.LatestReceivedAt);
    }

    [Fact]
    public void Constructor_AnAccountTheRunDrewOn_ReportsBothEndsOfWhatItDrewOn()
    {
        // Act
        var coverage = new AccountCoverage(
            Text("work"),
            PresentationFreshness.CurrentAt(ObservedAt),
            ObservedAt.AddDays(-30),
            ObservedAt);

        // Assert
        Assert.Equal(ObservedAt.AddDays(-30), coverage.EarliestReceivedAt);
        Assert.Equal(ObservedAt, coverage.LatestReceivedAt);
    }

    private static PresentationText Text(string text) => PresentationPlanExample.Text(text);
}
