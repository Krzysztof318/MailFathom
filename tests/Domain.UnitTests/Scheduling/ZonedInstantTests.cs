// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Domain.Scheduling;
using Xunit;

namespace MailFathom.Domain.UnitTests.Scheduling;

/// <summary>Covers what a named local time resolves to, above all where a clock skips an hour or repeats one.</summary>
/// <remarks>
/// The zone is a real one from the IANA database rather than a fabricated rule, because what these tests are about is
/// the arithmetic .NET performs against a transition an operator's own zone actually has. Every instant asserted here
/// is written out rather than derived, so a change in the resolution would fail rather than move both sides together.
/// </remarks>
public sealed class ZonedInstantTests
{
    private static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");

    /// <summary>An ordinary local time is the instant that zone's offset makes it, and it keeps the zone it was named in.</summary>
    [Fact]
    public void Resolve_ALocalTimeOutsideAnyTransition_IsTheInstantThatZoneReadsIt_As()
    {
        // Act
        var resolved = ZonedInstant.Resolve(new DateTime(2026, 8, 19, 9, 0, 0), Warsaw);

        // Assert
        Assert.Equal(Instant("2026-08-19T07:00:00Z"), resolved.Instant);
        Assert.Equal("Europe/Warsaw", resolved.ZoneId);
    }

    /// <summary>A time the clock springs over never occurs, so the occasion is taken at the instant the gap ends.</summary>
    /// <remarks>
    /// The alternative is losing the occasion entirely, which for a message written for half past two on the last
    /// Sunday in March means a send that silently never happens once a year.
    /// </remarks>
    [Fact]
    public void Resolve_ALocalTimeTheClockSpringsOver_IsTakenAtTheInstantTheGapEnds()
    {
        // Act
        var resolved = ZonedInstant.Resolve(new DateTime(2026, 3, 29, 2, 30, 0), Warsaw);

        // Assert
        Assert.Equal(Instant("2026-03-29T01:00:00Z"), resolved.Instant);
    }

    /// <summary>A skipped time carrying seconds is taken at the gap's end too, rather than at a minute's worth past it.</summary>
    /// <remarks>
    /// The walk out of the gap steps by a minute from wherever the time sat inside it, so a time with seconds on it
    /// would otherwise land those seconds beyond the end — which is not the instant the type promises and is a message
    /// leaving at a moment nobody named.
    /// </remarks>
    [Fact]
    public void Resolve_ALocalTimeTheClockSpringsOverThatCarriesSeconds_IsStillTakenAtTheInstantTheGapEnds()
    {
        // Act
        var resolved = ZonedInstant.Resolve(new DateTime(2026, 3, 29, 2, 15, 30), Warsaw);

        // Assert
        Assert.Equal(Instant("2026-03-29T01:00:00Z"), resolved.Instant);
    }

    /// <summary>A time the clock passes through twice occurs once, at the earlier of the two readings.</summary>
    [Fact]
    public void Resolve_ALocalTimeTheClockPassesThroughTwice_IsTakenAtTheEarlierReading()
    {
        // Act
        var resolved = ZonedInstant.Resolve(new DateTime(2026, 10, 25, 2, 30, 0), Warsaw);

        // Assert
        Assert.Equal(Instant("2026-10-25T00:30:00Z"), resolved.Instant);
    }

    /// <summary>The same wall-clock time on either side of a transition names two instants an hour apart in offset terms.</summary>
    /// <remarks>
    /// This is the whole reason a due time carries its zone: nine in the morning is nine in the morning in both weeks,
    /// and a value that had kept only the offset would drift by an hour across the transition.
    /// </remarks>
    [Fact]
    public void Resolve_TheSameWallClockTimeOnBothSidesOfATransition_KeepsTheLocalReadingRatherThanTheOffset()
    {
        // Act
        var beforeTheTransition = ZonedInstant.Resolve(new DateTime(2026, 10, 24, 9, 0, 0), Warsaw);
        var afterTheTransition = ZonedInstant.Resolve(new DateTime(2026, 10, 26, 9, 0, 0), Warsaw);

        // Assert
        Assert.Equal(Instant("2026-10-24T07:00:00Z"), beforeTheTransition.Instant);
        Assert.Equal(Instant("2026-10-26T08:00:00Z"), afterTheTransition.Instant);
    }

    /// <summary>An instant that arrived as one is kept as it is, under the coordinated zone's name.</summary>
    [Fact]
    public void At_AnInstant_KeepsItUnderTheCoordinatedZone()
    {
        // Act
        var named = ZonedInstant.At(Instant("2026-08-19T07:00:00Z").ToOffset(TimeSpan.FromHours(2)));

        // Assert
        Assert.Equal(Instant("2026-08-19T07:00:00Z"), named.Instant);
        Assert.Equal(ZonedInstant.CoordinatedZoneId, named.ZoneId);
    }

    /// <summary>A stored value is restored without being resolved again, so a host that no longer knows the zone can still read it.</summary>
    [Fact]
    public void Restore_AZoneThisHostDoesNotKnow_IsStillReadBackAsTheInstantItWas()
    {
        // Act
        var restored = ZonedInstant.Restore(Instant("2026-08-19T07:00:00Z"), " Mars/Olympus ");

        // Assert
        Assert.Equal(Instant("2026-08-19T07:00:00Z"), restored.Instant);
        Assert.Equal("Mars/Olympus", restored.ZoneId);
    }

    /// <summary>An identifier longer than the column that holds it is refused rather than truncated.</summary>
    [Fact]
    public void Restore_AZoneIdentifierPastTheBound_IsRefused()
    {
        // Act
        var thrown = Assert.Throws<ArgumentException>(() => ZonedInstant.Restore(
            Instant("2026-08-19T07:00:00Z"),
            new string('z', ZonedInstant.MaximumZoneIdLength + 1)));

        // Assert
        Assert.Equal("zoneId", thrown.ParamName);
    }

    /// <summary>An identifier naming no zone at all is refused, because a stored value with none could not be read back.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Restore_AZoneIdentifierNamingNothing_IsRefused(string zoneId)
    {
        // Act
        var thrown = Assert.Throws<ArgumentException>(() => ZonedInstant.Restore(
            Instant("2026-08-19T07:00:00Z"),
            zoneId));

        // Assert
        Assert.Equal("zoneId", thrown.ParamName);
    }

    private static DateTimeOffset Instant(string value) => DateTimeOffset.Parse(
        value,
        CultureInfo.InvariantCulture,
        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
}
