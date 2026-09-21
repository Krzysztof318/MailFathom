// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the clock every suite states an agent's time anchor through.</summary>
/// <remarks>
/// What each of those suites asserts is the day an anchor names, so a helper reading the stated instant in the wrong
/// zone would leave every one of them agreeing with a clock nobody asked for — and agreeing quietly, since the day it
/// produced is a plausible one. The default matters for the same reason: a helper that silently read somewhere other
/// than the coordinated zone would make a test stating no zone depend on where it was run.
/// </remarks>
public sealed class UserClocksTests
{
    private static readonly DateTimeOffset Instant = new(2026, 9, 9, 21, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Reading_AnInstantAndNoZone_ReadsItInTheCoordinatedZone()
    {
        // Arrange
        var clock = UserClocks.Reading(Instant);

        // Act
        var now = clock.Now();

        // Assert
        Assert.Equal(Instant, now);
        Assert.Equal(TimeSpan.Zero, now.Offset);
    }

    [Fact]
    public void Reading_AnInstantAndAZone_ReadsItInThatZonesOwnDay()
    {
        // Arrange
        var clock = UserClocks.Reading(Instant, "Asia/Tokyo");

        // Act
        var now = clock.Now();

        // Assert
        Assert.Equal(Instant, now);
        Assert.Equal(new DateTime(2026, 9, 10, 6, 30, 0), now.DateTime);
    }

    /// <summary>A zone identifier nothing resolves is a defect in the test that stated it, so it is refused rather than quietly read in UTC.</summary>
    [Fact]
    public void Reading_AZoneThisHostDoesNotKnow_IsRefusedNamingTheIdentifier()
    {
        // Act
        var refusal = Assert.Throws<ArgumentException>(() => UserClocks.Reading(Instant, "Europe/Warszawa"));

        // Assert
        Assert.Contains("Europe/Warszawa", refusal.Message, StringComparison.Ordinal);
    }
}
