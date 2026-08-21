// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Domain.Scheduling;

namespace MailFathom.Application.Jobs.Scheduling;

/// <summary>States when a recurring dispatch happens, in the two shapes a deployment writes and this system owns.</summary>
/// <remarks>
/// <para>
/// The syntax is MailFathom's own rather than cron, and that is a decision rather than an omission. The platform ships
/// no cron parser, so cron would be a package to review, pin, and record in the licensing register — bought for the
/// cases nobody has asked for. What a recurring rule pass actually needs is an interval and a time of day, and both are
/// stated here in words an operator reads back without a reference card.
/// </para>
/// <para>
/// Two forms, and nothing else parses:
/// </para>
/// <list type="table">
/// <item>
/// <term><c>Every &lt;interval&gt;</c></term>
/// <description>
/// A fixed period written as <c>hh:mm:ss</c> or <c>d.hh:mm:ss</c>, at least <see cref="MinimumInterval" /> and at most
/// <see cref="MaximumInterval" />. Its occasions are anchored to the Unix epoch rather than to the last dispatch, so
/// they are the same instants on every replica and do not drift by however long a pass took to notice one.
/// </description>
/// </item>
/// <item>
/// <term><c>Daily at &lt;HH:mm&gt; [&lt;time zone&gt;]</c></term>
/// <description>
/// One occasion a day at that wall-clock time. <strong>Without a zone the time is UTC</strong>; naming a zone — an IANA
/// identifier such as <c>Europe/Warsaw</c> — reads the time in it, daylight saving included.
/// </description>
/// </item>
/// </list>
/// <para>
/// A local time meets two cases a UTC one never does, and both are resolved here rather than left to whichever instant
/// arithmetic happens to produce. A time the clock skips over when it springs forward does not occur at all, and is
/// taken as the instant the gap ends, so the day's occasion still happens rather than being lost. A time the clock
/// passes through twice when it falls back occurs twice, and the first of the two is taken, so the occasion happens once
/// and at the earlier of the two readings.
/// </para>
/// </remarks>
public sealed class JobRecurrence
{
    /// <summary>The shortest interval a deployment may declare.</summary>
    /// <remarks>
    /// A minute, because the worker looks at its schedules once per poll interval and anything shorter would declare an
    /// occasion the loop cannot honour as an occasion rather than as a delay.
    /// </remarks>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(1);

    /// <summary>The longest interval a deployment may declare.</summary>
    public static readonly TimeSpan MaximumInterval = TimeSpan.FromDays(365);

    private const string EveryKeyword = "every";
    private const string DailyKeyword = "daily";
    private const string AtKeyword = "at";
    private const string CoordinatedZoneName = ZonedInstant.CoordinatedZoneId;
    private const string TimeOfDayFormat = "HH\\:mm";

    private readonly TimeSpan? interval;
    private readonly TimeOnly timeOfDay;
    private readonly TimeZoneInfo? zone;

    private JobRecurrence(TimeSpan? interval, TimeOnly timeOfDay, TimeZoneInfo? zone)
    {
        this.interval = interval;
        this.timeOfDay = timeOfDay;
        this.zone = zone;
    }

    /// <summary>Gets the declaration in the one form two recurrences are compared and hashed by.</summary>
    /// <remarks>
    /// Derived rather than the authored text, so two spellings of one schedule are one schedule: whatever a rule set's
    /// revision is derived from has to move when the meaning moves and stay still when only the writing does.
    /// </remarks>
    public string CanonicalForm => this.interval is { } every
        ? string.Create(CultureInfo.InvariantCulture, $"every:{every:c}")
        : string.Create(CultureInfo.InvariantCulture, $"daily:{this.timeOfDay.ToString(TimeOfDayFormat, CultureInfo.InvariantCulture)}:{this.ZoneName}");

    /// <summary>Gets the zone the occasions are read in, which is the coordinated one when the declaration named none.</summary>
    /// <remarks>
    /// It is published because an occasion is more than an instant to whoever reads it afterwards: a message sent on
    /// the occasion of a schedule declared in Warsaw went out at nine in Warsaw, and a record keeping only the instant
    /// would leave that unreadable the moment the offset changed.
    /// </remarks>
    public string ZoneName => this.zone?.Id ?? CoordinatedZoneName;

    /// <summary>Reads a declared schedule, reporting what is wrong with one this system cannot use.</summary>
    /// <param name="declaration">The schedule as an operator wrote it.</param>
    /// <param name="recurrence">The parsed schedule, or <see langword="null" /> when the declaration is unusable.</param>
    /// <param name="error">What an operator has to fix, or <see langword="null" /> when the declaration is usable.</param>
    /// <returns><see langword="true" /> when the declaration names a schedule this system runs.</returns>
    /// <remarks>
    /// The failure is a message rather than an exception, because the one caller that matters is configuration
    /// validation and what it needs is every fault of a section in one reading rather than the first one raised.
    /// </remarks>
    public static bool TryParse(string? declaration, out JobRecurrence? recurrence, out string? error)
    {
        recurrence = null;

        if (string.IsNullOrWhiteSpace(declaration))
        {
            error = "a schedule is named by nothing.";

            return false;
        }

        var words = declaration.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return words switch
        {
            [var keyword, var written] when Names(keyword, EveryKeyword) =>
                TryReadInterval(written, out recurrence, out error),
            [var keyword, var preposition, var written] when Names(keyword, DailyKeyword) && Names(preposition, AtKeyword) =>
                TryReadDailyTime(written, zoneName: null, out recurrence, out error),
            [var keyword, var preposition, var written, var zoneName] when Names(keyword, DailyKeyword) && Names(preposition, AtKeyword) =>
                TryReadDailyTime(written, zoneName, out recurrence, out error),
            _ => Unreadable(out error),
        };
    }

    /// <summary>Reads the most recent occasion at or before an instant.</summary>
    /// <param name="instant">The instant to look back from.</param>
    /// <returns>The occasion, or <see langword="null" /> when the schedule has had none by then.</returns>
    public DateTimeOffset? LatestOccurrenceAtOrBefore(DateTimeOffset instant)
    {
        if (this.interval is { } every)
        {
            var elapsedTicks = (instant - DateTimeOffset.UnixEpoch).Ticks;

            return elapsedTicks < 0
                ? null
                : DateTimeOffset.UnixEpoch + new TimeSpan(elapsedTicks / every.Ticks * every.Ticks);
        }

        // Two local days are looked at rather than one, because the occasion of the day an instant falls in can resolve
        // after that instant — a schedule at 03:00 read at 01:00 is answered by the previous day's occasion.
        var localDate = this.LocalDateOf(instant);

        return this.DailyOccurrences(localDate, dayCount: 2, step: -1)
            .Where(occurrence => occurrence <= instant)
            .Select(occurrence => (DateTimeOffset?)occurrence)
            .FirstOrDefault();
    }

    /// <summary>Reads the first occasion strictly after an instant.</summary>
    /// <param name="instant">The instant to look forward from.</param>
    /// <returns>The occasion, or <see langword="null" /> when the schedule declares none after it.</returns>
    public DateTimeOffset? NextOccurrenceAfter(DateTimeOffset instant)
    {
        if (this.interval is { } every)
        {
            var elapsedTicks = (instant - DateTimeOffset.UnixEpoch).Ticks;

            return elapsedTicks < 0
                ? DateTimeOffset.UnixEpoch
                : DateTimeOffset.UnixEpoch + new TimeSpan(((elapsedTicks / every.Ticks) + 1) * every.Ticks);
        }

        return this.DailyOccurrences(this.LocalDateOf(instant).AddDays(-1), dayCount: 3, step: 1)
            .Where(occurrence => occurrence > instant)
            .Select(occurrence => (DateTimeOffset?)occurrence)
            .FirstOrDefault();
    }

    /// <summary>Counts the occasions that fall in a window, which is how many a dispatch is passing over.</summary>
    /// <param name="exclusiveStart">The instant the window opens after.</param>
    /// <param name="inclusiveEnd">The last instant the window holds.</param>
    /// <returns>How many occasions the window holds, and zero when it holds none or is empty.</returns>
    /// <remarks>
    /// Counted from the two ends rather than by walking them, so a process that was down for a year answers as cheaply
    /// as one that was down for a minute — which matters because this count is what a skipped occasion is reported by.
    /// </remarks>
    public int CountOccurrencesIn(DateTimeOffset exclusiveStart, DateTimeOffset inclusiveEnd)
    {
        if (inclusiveEnd <= exclusiveStart)
        {
            return 0;
        }

        if (this.interval is { } every)
        {
            var lastIndex = IndexOf(inclusiveEnd, every);
            var firstIndex = IndexOf(exclusiveStart, every) + 1;

            return lastIndex < firstIndex ? 0 : (int)Math.Min(lastIndex - firstIndex + 1, int.MaxValue);
        }

        if (this.NextOccurrenceAfter(exclusiveStart) is not { } first
            || first > inclusiveEnd
            || this.LatestOccurrenceAtOrBefore(inclusiveEnd) is not { } last)
        {
            return 0;
        }

        // One occasion per local day, including a day whose declared time the clock skipped over, so the span between
        // the first day inside the window and the last is the count.
        return this.LocalDateOf(last).DayNumber - this.LocalDateOf(first).DayNumber + 1;
    }

    /// <inheritdoc />
    public override string ToString() => this.interval is { } every
        ? string.Create(CultureInfo.InvariantCulture, $"every {every:c}")
        : string.Create(
            CultureInfo.InvariantCulture,
            $"daily at {this.timeOfDay.ToString(TimeOfDayFormat, CultureInfo.InvariantCulture)} {this.ZoneName}");

    private static bool Names(string word, string keyword) => StringComparer.OrdinalIgnoreCase.Equals(word, keyword);

    private static long IndexOf(DateTimeOffset instant, TimeSpan every)
    {
        var elapsedTicks = (instant - DateTimeOffset.UnixEpoch).Ticks;

        return elapsedTicks < 0 ? -1 : elapsedTicks / every.Ticks;
    }

    private static bool Unreadable(out string? error)
    {
        error = string.Create(
            CultureInfo.InvariantCulture,
            $"a schedule is written as 'Every <interval from {MinimumInterval:c} to {MaximumInterval:c}>' or as 'Daily at <HH:mm>' with an optional IANA time zone such as 'Europe/Warsaw'; without a zone the time is UTC.");

        return false;
    }

    private static bool TryReadInterval(string written, out JobRecurrence? recurrence, out string? error)
    {
        recurrence = null;
        error = null;

        if (!TimeSpan.TryParseExact(written, ["c"], CultureInfo.InvariantCulture, out var every))
        {
            return Unreadable(out error);
        }

        if (every < MinimumInterval || every > MaximumInterval)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"an interval of {every:c} is outside the {MinimumInterval:c} to {MaximumInterval:c} a schedule may declare.");

            return false;
        }

        recurrence = new JobRecurrence(every, default, zone: null);

        return true;
    }

    private static bool TryReadDailyTime(
        string written,
        string? zoneName,
        out JobRecurrence? recurrence,
        out string? error)
    {
        recurrence = null;
        error = null;

        var parsed = TimeOnly.TryParseExact(
            written,
            TimeOfDayFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var timeOfDay);

        if (!parsed)
        {
            return Unreadable(out error);
        }

        if (zoneName is null || Names(zoneName, CoordinatedZoneName))
        {
            recurrence = new JobRecurrence(interval: null, timeOfDay, zone: null);

            return true;
        }

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(zoneName, out var zone))
        {
            error =
                $"'{zoneName}' names no time zone this system knows. Write an IANA identifier such as 'Europe/Warsaw', or leave the zone out to declare the time in UTC.";

            return false;
        }

        recurrence = new JobRecurrence(interval: null, timeOfDay, zone);

        return true;
    }

    private DateOnly LocalDateOf(DateTimeOffset instant) => DateOnly.FromDateTime(
        this.zone is null ? instant.UtcDateTime : TimeZoneInfo.ConvertTime(instant, this.zone).DateTime);

    /// <summary>Walks the daily occasions from one local date, resolving each to the instant it names.</summary>
    /// <param name="from">The local date the walk starts at.</param>
    /// <param name="dayCount">How many days the walk covers.</param>
    /// <param name="step">Which way it walks, <c>1</c> forwards and <c>-1</c> backwards.</param>
    private IEnumerable<DateTimeOffset> DailyOccurrences(DateOnly from, int dayCount, int step) => Enumerable
        .Range(0, dayCount)
        .Select(offset => this.ResolveLocal(from.AddDays(offset * step)));

    /// <summary>Resolves one local date's declared time to the instant it names.</summary>
    /// <remarks>
    /// The two daylight-saving cases are <see cref="ZonedInstant" />'s rather than this type's, because a schedule and
    /// a message held until a named time meet the same two and have to answer them identically: a time the clock
    /// skipped is the instant the gap ends, and a time it passed through twice is the earlier of the two readings.
    /// </remarks>
    private DateTimeOffset ResolveLocal(DateOnly date) => this.zone is null
        ? new DateTimeOffset(date.ToDateTime(this.timeOfDay), TimeSpan.Zero)
        : ZonedInstant.Resolve(date.ToDateTime(this.timeOfDay), this.zone).Instant;
}
