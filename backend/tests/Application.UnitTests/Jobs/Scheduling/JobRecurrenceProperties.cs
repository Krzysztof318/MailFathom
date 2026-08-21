// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using CsCheck;
using MailFathom.Application.Jobs.Scheduling;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.Jobs.Scheduling;

/// <summary>States what a daily schedule answers around every clock change, rather than around two written-down ones.</summary>
/// <remarks>
/// A time the clock skips over does not occur and a time it passes through twice occurs twice, and both are resolved
/// rather than left to whatever the arithmetic produced. The examples beside this file pin those two readings for one
/// zone on two known dates; what a generator adds is every other declared time on those days and every other clock
/// change the zones below make — including one that shifts by half an hour and two that move in opposite seasons.
/// </remarks>
public sealed class JobRecurrenceProperties
{
    /// <summary>How many inputs each property here draws.</summary>
    private const int Iterations = 400;

    /// <summary>How far either side of a clock change an instant is drawn from.</summary>
    private const int WindowHours = 30;

    /// <summary>How many occasions the walked window holds, chosen to span the clock change and the days around it.</summary>
    private const int WalkedOccasions = 7;

    /// <summary>How many times a zone that observes daylight saving changes its offset in one year.</summary>
    private const int ChangesPerYear = 2;

    /// <summary>
    /// Zones that change their offset, chosen for what each one changes differently: two hemispheres so the change
    /// falls in opposite seasons, and a zone that shifts by half an hour rather than a whole one.
    /// </summary>
    private static readonly string[] ZoneIds =
        ["Europe/Warsaw", "America/New_York", "Australia/Sydney", "Australia/Lord_Howe"];

    private static readonly DateTimeOffset FirstInstant = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset LastInstant = new(2029, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly TimeZoneInfo[] Zones = [.. ZoneIds.Select(TimeZoneInfo.FindSystemTimeZoneById)];

    /// <summary>Every hour at which one of the zones above reads a different offset than it did an hour earlier.</summary>
    private static readonly (TimeZoneInfo Zone, DateTimeOffset At)[] ClockChanges =
    [
        .. Zones.SelectMany(zone => HourlyInstants()
            .Where(instant => zone.GetUtcOffset(instant) != zone.GetUtcOffset(instant.AddHours(-1)))
            .Select(instant => (Zone: zone, At: instant))),
    ];

    /// <summary>A schedule in a zone that changes its offset, and an instant within a day or so of a change it makes.</summary>
    private static readonly Gen<(JobRecurrence Recurrence, DateTimeOffset Instant)> Cases = Gen.Select(
        Gen.Int[0, ClockChanges.Length - 1],
        Gen.Int[0, 23],
        Gen.Int[0, 59],
        Gen.Long[-WindowHours * TimeSpan.TicksPerHour, WindowHours * TimeSpan.TicksPerHour],
        (change, hour, minute, ticks) => (
            Daily(hour, minute, ClockChanges[change].Zone.Id),
            ClockChanges[change].At.AddTicks(ticks)));

    /// <summary>
    /// The corpus every property below draws from is discovered rather than written down, so it is asserted to be real:
    /// a machine whose time-zone data named no change would otherwise leave each property sampling schedules that never
    /// move and reporting green for them.
    /// </summary>
    [Fact]
    public void ClockChanges_TheZonesThesePropertiesUse_ChangeOffsetTwiceInEveryYearCovered()
    {
        // Act
        var changesPerZone = ClockChanges
            .GroupBy(change => change.Zone.Id)
            .ToDictionary(zone => zone.Key, zone => zone.Count(), StringComparer.Ordinal);

        // Assert
        Assert.Equal(ZoneIds.Length, changesPerZone.Count);
        Assert.All(
            Zones,
            zone => Assert.Equal(
                ChangesPerYear * (LastInstant.Year - FirstInstant.Year),
                changesPerZone[zone.Id]));
    }

    /// <summary>
    /// The next occasion is the earliest one after the instant, which is the whole of what a dispatch relies on: an
    /// answer at or before it would fire twice, and one past a nearer occasion would skip a day.
    /// </summary>
    [Fact]
    public void NextOccurrenceAfter_AnyInstantAroundAClockChange_AnswersTheEarliestOccasionStrictlyAfterIt()
    {
        // Act, Assert
        PropertyCheck.Holds(
            Cases,
            input =>
            {
                var next = input.Recurrence.NextOccurrenceAfter(input.Instant);

                Assert.NotNull(next);
                Assert.True(next > input.Instant, $"{next:O} is not after {input.Instant:O}");

                var nearer = input.Recurrence.LatestOccurrenceAtOrBefore(next.Value.AddTicks(-1));

                Assert.True(
                    nearer is null || nearer <= input.Instant,
                    $"{nearer:O} sits between {input.Instant:O} and {next:O}");
            },
            Iterations);
    }

    /// <summary>
    /// The two readings are one schedule seen from either side, so an occasion the forward reading names is one the
    /// backward reading names too. A day whose declared time the clock skipped resolves the same way under both.
    /// </summary>
    [Fact]
    public void LatestOccurrenceAtOrBefore_AnOccasionAroundAClockChange_AnswersThatOccasionItself()
    {
        // Act, Assert
        PropertyCheck.Holds(
            Cases,
            input =>
            {
                var next = input.Recurrence.NextOccurrenceAfter(input.Instant);

                Assert.NotNull(next);
                Assert.Equal(next, input.Recurrence.LatestOccurrenceAtOrBefore(next.Value));
            },
            Iterations);
    }

    /// <summary>
    /// The count is computed from the two ends rather than by walking, so that a process down for a year answers as
    /// cheaply as one down for a minute. That is worth having only while the cheap answer and the walk agree, and a
    /// clock change is where a local day gains or loses an hour without gaining or losing an occasion.
    /// </summary>
    [Fact]
    public void CountOccurrencesIn_AWindowSpanningAClockChange_CountsWhatWalkingTheOccasionsOneByOneReaches()
    {
        // Act, Assert
        PropertyCheck.Holds(
            Cases,
            input =>
            {
                var last = Enumerable
                    .Range(0, WalkedOccasions)
                    .Aggregate(
                        input.Instant,
                        (from, _) => input.Recurrence.NextOccurrenceAfter(from)
                            ?? throw new InvalidOperationException($"No occasion follows {from:O}."));

                Assert.Equal(WalkedOccasions, input.Recurrence.CountOccurrencesIn(input.Instant, last));
            },
            Iterations);
    }

    private static JobRecurrence Daily(int hour, int minute, string zoneId)
    {
        var declaration = string.Create(CultureInfo.InvariantCulture, $"Daily at {hour:00}:{minute:00} {zoneId}");

        Assert.True(JobRecurrence.TryParse(declaration, out var recurrence, out var error), error);

        return recurrence!;
    }

    private static IEnumerable<DateTimeOffset> HourlyInstants() => Enumerable
        .Range(0, (int)(LastInstant - FirstInstant).TotalHours)
        .Select(hour => FirstInstant.AddHours(hour));
}
