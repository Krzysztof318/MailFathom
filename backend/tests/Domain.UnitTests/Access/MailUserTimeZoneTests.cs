// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Scheduling;
using Xunit;

namespace MailFathom.Domain.UnitTests.Access;

/// <summary>Covers which zone identifiers are read, which are refused, and what a record stating none falls to.</summary>
/// <remarks>
/// The zones named here are real ones from the IANA database rather than fabricated rules, because what is being
/// asserted is that a host resolves what a browser reports: <c>Intl.DateTimeFormat().resolvedOptions().timeZone</c>
/// answers an IANA identifier, and a value a client proposes has to be one this side can read back.
/// </remarks>
public sealed class MailUserTimeZoneTests
{
    /// <summary>An IANA identifier is what a browser reports and what a record holds, so it is what a read accepts.</summary>
    [Theory]
    [InlineData("Europe/Warsaw")]
    [InlineData("America/New_York")]
    [InlineData("Asia/Tokyo")]
    [InlineData("UTC")]
    public void TryRead_AZoneThisHostKnows_ReadsItUnderTheIdentifierItWasStatedBy(string zoneId)
    {
        // Act
        var read = MailUserTimeZone.TryRead(zoneId, out var zone);

        // Assert
        Assert.True(read);
        Assert.Equal(zoneId, zone?.Id);
    }

    /// <summary>A zone nobody has is refused rather than quietly read in UTC, or somebody is answered in a day they did not ask for.</summary>
    [Theory]
    [InlineData("Europe/Warszawa")]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData("+02:00")]
    public void TryRead_AZoneThisHostDoesNotKnow_IsRefused(string zoneId)
    {
        // Act
        var read = MailUserTimeZone.TryRead(zoneId, out var zone);

        // Assert
        Assert.False(read);
        Assert.Null(zone);
    }

    /// <summary>The bound is the one the stored zone identifier already carries, so the two cannot disagree about it.</summary>
    [Fact]
    public void TryRead_AnIdentifierLongerThanAZoneIdentifierMayBe_IsRefused()
    {
        // Arrange
        var overLong = new string('a', ZonedInstant.MaximumZoneIdLength + 1);

        // Act
        var read = MailUserTimeZone.TryRead(overLong, out var zone);

        // Assert
        Assert.False(read);
        Assert.Null(zone);
    }

    /// <summary>Nothing stated is a record that asked for nothing, and it is refused here so the caller decides what that means.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryRead_NoIdentifierAtAll_IsRefused(string? zoneId)
    {
        // Act
        var read = MailUserTimeZone.TryRead(zoneId, out var zone);

        // Assert
        Assert.False(read);
        Assert.Null(zone);
    }

    /// <summary>The coordinated zone is what an unstated record falls to, and it is the same answer on every replica.</summary>
    [Fact]
    public void Coordinated_TheZoneAnUnstatedRecordFallsTo_IsCoordinatedUniversalTime()
    {
        // Act
        var zone = MailUserTimeZone.Coordinated;

        // Assert
        Assert.Equal("UTC", zone.Id);
        Assert.Equal(TimeSpan.Zero, zone.Reading(new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero)).Offset);
    }

    /// <summary>Reading an instant in a zone is what turns a deployment's clock into the day somebody is standing on.</summary>
    /// <remarks>
    /// Half past nine in the evening in London on the ninth is half past eleven in Warsaw on the same evening, and half
    /// past six the next morning in Tokyo — which is the pair of days a question naming <em>yesterday</em> would
    /// otherwise be answered with the wrong one of.
    /// </remarks>
    [Theory]
    [InlineData("Europe/Warsaw", 2026, 9, 9, 23, 30, 2)]
    [InlineData("Asia/Tokyo", 2026, 9, 10, 6, 30, 9)]
    [InlineData("UTC", 2026, 9, 9, 21, 30, 0)]
    public void Reading_AnInstant_AnswersTheWallClockThatZoneReadsItOn(
        string zoneId,
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int offsetHours)
    {
        // Arrange
        Assert.True(MailUserTimeZone.TryRead(zoneId, out var zone));

        // Act
        var reading = zone.Reading(new DateTimeOffset(2026, 9, 9, 21, 30, 0, TimeSpan.Zero));

        // Assert
        Assert.Equal(
            new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.FromHours(offsetHours)),
            reading);
    }

    /// <summary>An identifier written with surrounding space is the same zone, because a record edited by hand carries what somebody typed.</summary>
    [Fact]
    public void TryRead_AnIdentifierWithSurroundingSpace_ReadsTheZoneItNames()
    {
        // Act
        var read = MailUserTimeZone.TryRead("  Europe/Warsaw  ", out var zone);

        // Assert
        Assert.True(read);
        Assert.Equal("Europe/Warsaw", zone?.Id);
    }
}
