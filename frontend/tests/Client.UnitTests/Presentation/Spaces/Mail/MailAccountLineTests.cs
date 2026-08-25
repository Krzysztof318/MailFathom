// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Accounts;
using MailFathom.Client.Presentation.Spaces.Mail;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Presentation.Spaces.Mail;

/// <summary>One mailbox as a row: the gap it is described by, and the two sentences that answer different halves of it.</summary>
public sealed class MailAccountLineTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    /// <summary>An account nothing has ever synchronized has no gap to state, which is its own band rather than the widest one.</summary>
    [Fact]
    public void GapAt_AnAccountNothingHasEverSynchronized_HasNoGapToState()
    {
        // Act, Assert
        Assert.Equal(FreshnessGap.Never, MailAccountLine.GapAt(lastSynchronizedAt: null, Now));
    }

    /// <summary>The bands are the ones somebody decides on, and each boundary falls on the wider side of itself.</summary>
    [Theory]
    [InlineData(0, FreshnessGap.WithinTheHour)]
    [InlineData(59, FreshnessGap.WithinTheHour)]
    [InlineData(60, FreshnessGap.Today)]
    [InlineData(23 * 60, FreshnessGap.Today)]
    [InlineData(24 * 60, FreshnessGap.WithinTheWeek)]
    [InlineData(6 * 24 * 60, FreshnessGap.WithinTheWeek)]
    [InlineData(7 * 24 * 60, FreshnessGap.LongerAgo)]
    [InlineData(40 * 24 * 60, FreshnessGap.LongerAgo)]
    public void GapAt_AnAccountSynchronizedSomeMinutesAgo_FallsInTheBandThatSaysSo(int minutesAgo, FreshnessGap expected)
    {
        // Act
        var gap = MailAccountLine.GapAt(Now.AddMinutes(-minutesAgo), Now);

        // Assert
        Assert.Equal(expected, gap);
    }

    /// <summary>
    /// Two clocks that disagree by a few seconds are ordinary — a person's device and a deployment somewhere else —
    /// so a timestamp slightly ahead reads as the narrowest band rather than as a gap running backwards.
    /// </summary>
    [Fact]
    public void GapAt_ATimestampAheadOfThisDevicesClock_ReadsAsTheNarrowestBand()
    {
        // Act
        var gap = MailAccountLine.GapAt(Now.AddSeconds(30), Now);

        // Assert
        Assert.Equal(FreshnessGap.WithinTheHour, gap);
    }

    /// <summary>
    /// The row says the mailbox's own name and how current it is, and says both in the language being read in rather
    /// than in the words a wire contract happens to use.
    /// </summary>
    [Fact]
    public void Of_AnAccountBeingRefreshed_SaysWhatItIsAndHowCurrentItIs()
    {
        // Arrange
        var account = new DeploymentMailAccount(
            "work",
            "Work mail",
            "Synchronized",
            Now.AddMinutes(-10));

        // Act
        var line = MailAccountLine.Of(account, Now, Words());

        // Assert
        Assert.Equal("work", line.Id);
        Assert.Equal("Work mail", line.DisplayName);
        Assert.Equal("being refreshed", line.Standing);
        Assert.Equal("updated within the last hour", line.Freshness);
        Assert.False(line.IsFailing);
    }

    /// <summary>
    /// An account the deployment cannot reach is marked as that, separately from how old its copy is: the two carry
    /// the same gap on a mailbox that has been failing since it was last written to.
    /// </summary>
    [Fact]
    public void Of_AnAccountTheDeploymentCannotReach_IsMarkedApartFromItsGap()
    {
        // Arrange
        var failing = new DeploymentMailAccount("work", "Work mail", "Failing", Now.AddDays(-2));
        var behind = new DeploymentMailAccount("home", "Home mail", "Synchronized", Now.AddDays(-2));

        // Act
        var failingLine = MailAccountLine.Of(failing, Now, Words());
        var behindLine = MailAccountLine.Of(behind, Now, Words());

        // Assert
        Assert.True(failingLine.IsFailing);
        Assert.False(behindLine.IsFailing);
        Assert.Equal(failingLine.Freshness, behindLine.Freshness);
        Assert.NotEqual(failingLine.Standing, behindLine.Standing);
    }

    /// <summary>A standing this build does not know claims nothing, and says so rather than reading as a mailbox being kept current.</summary>
    [Fact]
    public void Of_AStandingThisClientDoesNotKnow_ClaimsNothingAboutTheCopy()
    {
        // Arrange
        var account = new DeploymentMailAccount("work", "Work mail", "Paused", Now.AddMinutes(-10));

        // Act
        var line = MailAccountLine.Of(account, Now, Words());

        // Assert
        Assert.Equal("state not recognized", line.Standing);
        Assert.False(line.IsFailing);
    }

    /// <summary>Every standing and every band is named under its own key, which is the one place a typo would reach a reader.</summary>
    [Fact]
    public void ResourceKeyFor_EveryStandingAndEveryBand_IsNamedUnderItsOwnKey()
    {
        // Act, Assert
        Assert.Equal(
            "MailPage.Account.Standing.Failing",
            MailAccountLine.StandingResourceKeyFor(MailAccountStanding.Failing));
        Assert.Equal(
            "MailPage.Account.Freshness.LongerAgo",
            MailAccountLine.FreshnessResourceKeyFor(FreshnessGap.LongerAgo));
    }

    /// <summary>A row that could be built out of nothing would be a row describing no mailbox.</summary>
    [Fact]
    public void Of_AMissingArgument_IsRefused()
    {
        // Arrange
        var account = new DeploymentMailAccount("work", "Work mail", "Synchronized", Now);

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => MailAccountLine.Of(null!, Now, Words()));
        Assert.Throws<ArgumentNullException>(() => MailAccountLine.Of(account, Now, null!));
    }

    private static StubStringLocalizer Words()
    {
        var table = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var standing in Enum.GetValues<MailAccountStanding>())
        {
            table[MailAccountLine.StandingResourceKeyFor(standing)] = standing switch
            {
                MailAccountStanding.Unrecognized => "state not recognized",
                MailAccountStanding.NeverSynchronized => "not synchronized yet",
                MailAccountStanding.Synchronized => "being refreshed",
                _ => "not reachable",
            };
        }

        foreach (var gap in Enum.GetValues<FreshnessGap>())
        {
            table[MailAccountLine.FreshnessResourceKeyFor(gap)] = gap switch
            {
                FreshnessGap.Never => "no mail taken in yet",
                FreshnessGap.WithinTheHour => "updated within the last hour",
                FreshnessGap.Today => "updated within the last day",
                FreshnessGap.WithinTheWeek => "updated within the last week",
                _ => "nothing taken in for over a week",
            };
        }

        return new StubStringLocalizer(table);
    }
}
