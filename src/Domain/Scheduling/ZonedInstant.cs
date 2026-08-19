// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Scheduling;

/// <summary>One instant, together with the zone whoever named it was thinking in.</summary>
/// <remarks>
/// <para>
/// A person names a time in a place rather than as an offset from now: nine in the morning in Warsaw is nine in the
/// morning on both sides of a daylight-saving transition, and the two are different instants. Keeping the zone beside
/// the instant is what lets the instant be read back as the time it was meant to be, and what makes a value stored
/// months before it matters still mean what it meant when it was written.
/// </para>
/// <para>
/// The instant is the value everything compares, and the zone is never used to re-derive it. Resolution happens once,
/// where the local time is named, so a zone whose rules change afterwards cannot move a decision that was already
/// taken.
/// </para>
/// <para>
/// Two local times have no single instant and both are resolved here rather than left to whichever arithmetic a caller
/// happens to perform. A time the clock skips over when it springs forward does not occur at all, and is taken as the
/// instant the gap ends, so the occasion still happens rather than being lost. A time the clock passes through twice
/// when it falls back occurs twice, and the first of the two is taken, so the occasion happens once and at the earlier
/// of the two readings.
/// </para>
/// </remarks>
public sealed record ZonedInstant
{
    /// <summary>The name the coordinated zone is recorded under when a local time named none.</summary>
    public const string CoordinatedZoneId = "UTC";

    /// <summary>The greatest length a zone identifier may have, which bounds the column one is stored in.</summary>
    /// <remarks>An IANA identifier is a short path such as <c>America/Argentina/ComodRivadavia</c>; the bound is well above the longest one and keeps a stored value readable.</remarks>
    public const int MaximumZoneIdLength = 64;

    /// <summary>How far a resolution walks forward out of a gap the local clock skipped over.</summary>
    /// <remarks>
    /// A daylight-saving gap is an hour in every zone anybody has, so a day is far more than the walk ever needs and is
    /// short enough that a zone with an implausible rule ends the walk rather than running it out.
    /// </remarks>
    private static readonly TimeSpan LongestSkippedSpan = TimeSpan.FromDays(1);

    private ZonedInstant(DateTimeOffset instant, string zoneId)
    {
        this.Instant = instant;
        this.ZoneId = zoneId;
    }

    /// <summary>Gets the instant itself, which is what every comparison is made against.</summary>
    public DateTimeOffset Instant { get; }

    /// <summary>Gets the zone the instant was named in, as the identifier a system knows it by.</summary>
    /// <remarks>It is <see cref="CoordinatedZoneId" /> when the time was named in no zone of its own, which is the ordinary case for a value that arrived as an instant already.</remarks>
    public string ZoneId { get; }

    /// <summary>Names an instant that was already one, in the coordinated zone.</summary>
    /// <param name="instant">The instant.</param>
    /// <returns>The instant, recorded as named in no zone of its own.</returns>
    public static ZonedInstant At(DateTimeOffset instant) => new(instant.ToUniversalTime(), CoordinatedZoneId);

    /// <summary>Resolves a wall-clock time in a zone to the instant it names.</summary>
    /// <param name="localTime">The time as it reads on a clock in that zone, whose own kind and offset are ignored.</param>
    /// <param name="zone">The zone the time is read in.</param>
    /// <returns>The instant, together with the zone it was named in.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="zone" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the zone's identifier is longer than <see cref="MaximumZoneIdLength" />.</exception>
    public static ZonedInstant Resolve(DateTime localTime, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        if (zone.Id.Length > MaximumZoneIdLength)
        {
            throw new ArgumentException(
                $"A time zone identifier may be at most {MaximumZoneIdLength} characters long.",
                nameof(zone));
        }

        var declared = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
        var resolved = declared;

        while (zone.IsInvalidTime(resolved) && resolved - declared < LongestSkippedSpan)
        {
            resolved = resolved.AddMinutes(1);
        }

        if (resolved != declared)
        {
            // The walk steps by a minute from wherever the declared time sat inside the gap, so it lands a minute's
            // worth of seconds past the end rather than on it. Every transition the zone database names falls on a
            // whole minute, so dropping what is left below the minute is the end itself — and the guard is what keeps
            // a zone that ever transitioned mid-minute from being answered with a time it skipped.
            var beyondTheMinute = TimeSpan.FromTicks(resolved.TimeOfDay.Ticks % TimeSpan.TicksPerMinute);

            if (beyondTheMinute > TimeSpan.Zero && !zone.IsInvalidTime(resolved - beyondTheMinute))
            {
                resolved -= beyondTheMinute;
            }
        }

        var offset = zone.IsAmbiguousTime(resolved)
            ? zone.GetAmbiguousTimeOffsets(resolved).Max()
            : zone.GetUtcOffset(resolved);

        return new ZonedInstant(new DateTimeOffset(resolved, offset), zone.Id);
    }

    /// <summary>Restores a value from the instant and the zone identifier a record holds.</summary>
    /// <param name="instant">The stored instant.</param>
    /// <param name="zoneId">The stored zone identifier.</param>
    /// <returns>The instant and the zone it was named in.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="zoneId" /> is blank or longer than <see cref="MaximumZoneIdLength" />.</exception>
    /// <remarks>
    /// The identifier is not resolved against the zones this system knows, deliberately. A stored value has already been
    /// resolved to an instant, so a zone a later host no longer carries would make the value unreadable without making
    /// it wrong.
    /// </remarks>
    public static ZonedInstant Restore(DateTimeOffset instant, string zoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneId);

        var trimmedZoneId = zoneId.Trim();

        if (trimmedZoneId.Length > MaximumZoneIdLength)
        {
            throw new ArgumentException(
                $"A time zone identifier may be at most {MaximumZoneIdLength} characters long.",
                nameof(zoneId));
        }

        return new ZonedInstant(instant, trimmedZoneId);
    }

    /// <inheritdoc />
    public override string ToString() => $"{this.Instant:O} ({this.ZoneId})";
}
