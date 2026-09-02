// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Presentation;
using Xunit;

namespace MailFathom.Application.UnitTests.Discovery.Presentation;

/// <summary>Covers how a plan says whether what it rests on was current.</summary>
public sealed class PresentationFreshnessTests
{
    private static readonly DateTimeOffset ObservedAt = PresentationPlanExample.ObservedAt;

    [Fact]
    public void CurrentAt_DataReadFromACurrentCopy_CarriesWhenThatWasEstablished()
    {
        // Act
        var freshness = PresentationFreshness.CurrentAt(ObservedAt);

        // Assert
        Assert.Equal(PresentationStaleness.Current, freshness.Staleness);
        Assert.Equal(ObservedAt, freshness.ObservedAt);
    }

    [Fact]
    public void StaleSince_DataKnownToBeBehind_CarriesWhenThatWasEstablished()
    {
        // Act
        var freshness = PresentationFreshness.StaleSince(ObservedAt);

        // Assert
        Assert.Equal(PresentationStaleness.Stale, freshness.Staleness);
        Assert.Equal(ObservedAt, freshness.ObservedAt);
    }

    [Fact]
    public void Unknown_DataNothingEstablished_ClaimsNeither()
    {
        // Act, Assert
        Assert.Equal(PresentationStaleness.Unknown, PresentationFreshness.Unknown.Staleness);
        Assert.Null(PresentationFreshness.Unknown.ObservedAt);
    }

    /// <summary>A verdict with no timestamp cannot be re-judged by a screen that has been open since.</summary>
    [Theory]
    [InlineData(PresentationStaleness.Current)]
    [InlineData(PresentationStaleness.Stale)]
    public void Constructor_AStatedVerdictWithNoTimestamp_IsRefused(PresentationStaleness staleness)
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => new PresentationFreshness(staleness, observedAt: null));
    }

    /// <summary>A timestamp beside a verdict that establishes nothing would be a reading nobody took.</summary>
    [Fact]
    public void Constructor_AnUnknownVerdictWithATimestamp_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => new PresentationFreshness(PresentationStaleness.Unknown, ObservedAt));
    }
}
