// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;

namespace MailFathom.Client.Session;

/// <summary>How many times the client asks its deployment again by itself, and how long it waits between attempts.</summary>
/// <remarks>
/// <para>
/// Bounded rather than endless, which is what makes losing a deployment something a person is eventually told about
/// rather than a window that spins forever. When the attempts run out the client stops and offers the ask as a button:
/// a connection that has been gone for a minute is a different situation from one that dropped for a moment, and only
/// the person on the other side of the screen knows which they are in.
/// </para>
/// <para>
/// This is the only backoff in the client and it wraps nothing. The root instructions refuse nested retry storms, so
/// nothing composed over the session retries on top of it — a screen whose own read failed renders its error and
/// offers the same ask instead of starting a second curve inside this one.
/// </para>
/// <para>
/// The wait doubles per attempt and is drawn from a range rather than computed exactly, because several clients that
/// lost the same deployment lost it at the same instant and an exact wait would return all of them to it together.
/// The service states the same curve for its own schedulers in <c>backend/src/Application/Resilience/</c>; the two are
/// stated at each end rather than shared, for the reason nothing under <c>frontend/</c> reaches into <c>backend/</c>.
/// </para>
/// </remarks>
public sealed record DeploymentConnectionRetry
{
    /// <summary>How finely a wait is drawn from its range.</summary>
    private const int JitterResolution = 1000;

    /// <summary>Bounds the doubling so it stays inside the tick arithmetic that applies it.</summary>
    /// <remarks>The ceiling is reached long before this, so the bound costs nothing anybody can observe.</remarks>
    private const int MaxGrowthSteps = 16;

    /// <summary>Initializes the policy.</summary>
    /// <param name="attempts">How many attempts are made in all, the first one included.</param>
    /// <param name="firstWait">The wait the first retry is drawn around, from which the doubling grows.</param>
    /// <param name="longestWait">The ceiling a grown wait never exceeds.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the attempts are not positive, the first wait is negative, or the ceiling is below it.</exception>
    public DeploymentConnectionRetry(int attempts, TimeSpan firstWait, TimeSpan longestWait)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(attempts);
        ArgumentOutOfRangeException.ThrowIfLessThan(firstWait, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(longestWait, firstWait);

        this.Attempts = attempts;
        this.FirstWait = firstWait;
        this.LongestWait = longestWait;
    }

    /// <summary>Gets what the client does when nothing is stated, which is what the application registers.</summary>
    /// <remarks>
    /// Five attempts over about half a minute. Long enough to carry a client through a network that dropped while a
    /// laptop moved between access points, and short enough that somebody whose deployment is genuinely down is told so
    /// rather than left watching a progress bar.
    /// </remarks>
    public static DeploymentConnectionRetry Standard { get; } =
        new(attempts: 5, firstWait: TimeSpan.FromSeconds(2), longestWait: TimeSpan.FromSeconds(30));

    /// <summary>Gets how many attempts are made in all, the first one included.</summary>
    public int Attempts { get; }

    /// <summary>Gets the wait the first retry is drawn around.</summary>
    public TimeSpan FirstWait { get; }

    /// <summary>Gets the ceiling a grown wait never exceeds.</summary>
    public TimeSpan LongestWait { get; }

    /// <summary>Says how long the client waits before the attempt it is about to make.</summary>
    /// <param name="attempt">The attempt about to be made, counting from one; the first is made without waiting.</param>
    /// <returns>A wait of at least half the grown ceiling and at most <see cref="LongestWait" />, and zero before the first attempt.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="attempt" /> is not positive.</exception>
    public TimeSpan WaitBefore(int attempt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(attempt);

        if (attempt is 1)
        {
            return TimeSpan.Zero;
        }

        var ceilingTicks = GrowFromFirstWait(this.FirstWait.Ticks, this.LongestWait.Ticks, attempt - 2);
        var floorTicks = ceilingTicks / 2;

        return TimeSpan.FromTicks(floorTicks + DrawJitterTicks(ceilingTicks - floorTicks));
    }

    /// <summary>Doubles the first wait once per retry already made and caps the result.</summary>
    /// <remarks>The comparison shifts the ceiling down rather than the wait up, so a product that would run past the tick range is never computed.</remarks>
    private static long GrowFromFirstWait(long firstWaitTicks, long longestWaitTicks, int retriesMade)
    {
        var growthSteps = Math.Min(retriesMade, MaxGrowthSteps);

        return firstWaitTicks <= longestWaitTicks >> growthSteps
            ? firstWaitTicks << growthSteps
            : longestWaitTicks;
    }

    /// <summary>Draws the jitter added to the floor of the range.</summary>
    /// <remarks>A fraction of the range rather than a tick count, so one draw covers every width without the range having to fit the generator's own bounds.</remarks>
    private static long DrawJitterTicks(long spreadTicks) =>
        spreadTicks is 0
            ? 0
            : spreadTicks * RandomNumberGenerator.GetInt32(0, JitterResolution + 1) / JitterResolution;
}
