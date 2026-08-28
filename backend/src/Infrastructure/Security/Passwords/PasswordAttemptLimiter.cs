// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;

namespace MailFathom.Infrastructure.Security.Passwords;

/// <summary>Bounds how often one source and one username may have a password checked.</summary>
/// <remarks>
/// <para>
/// The transport's own limiter counts requests per authenticated caller, and a request guessing a password has no
/// authenticated caller — every one of them lands in the surface's shared anonymous bucket, which is a bound on the
/// endpoint rather than on the guessing. This is the bound on the guessing, and it is two axes because the two attacks
/// are different: one host trying many passwords is caught per source, and many hosts trying one account's passwords
/// are caught per username.
/// </para>
/// <para>
/// A username never reaches a partition key. Both keys are HMAC-SHA-256 digests under a key this process generates at
/// construction and never publishes, so what is held in memory, what a dump of this object would show, and what any
/// future metric could tag are all a value nothing outside this process can reverse — while two attempts against one
/// username still land in one partition, which is the whole of what the limiter needs. The source is digested for the
/// same reason, a remote address being personal data in its own right.
/// </para>
/// <para>
/// <strong>Capacity is taken before a password is verified and given back where the password was right.</strong> Both
/// halves of that are load-bearing. Taking it first is what makes the bound hold under concurrency: a caller opening
/// five hundred connections at once would otherwise have every one of them read a non-empty allowance and go on to a
/// deliberately expensive derivation, because nothing between a read and a later write excludes the other four hundred
/// and ninety-nine. Giving it back on success is what keeps the bound about guessing rather than about an owner's
/// request rate: HTTP Basic re-presents the credential on every request and this deployment keeps no session, so an
/// allowance every verification spent would refuse the eleventh call of a working session with the answer a wrong
/// password gets.
/// </para>
/// <para>
/// That pairing is why each axis is a <see cref="ConcurrencyLimiter" /> holding one permit per attempt rather than a
/// token bucket. A token bucket cannot express the second half — a spent token comes back on a schedule and nothing
/// returns one — while releasing a lease is exactly what giving the permit back means. A wrong password therefore does
/// not release its permits: <see cref="PasswordAttemptReservation.Spend" /> holds them for
/// <see cref="SpentAttemptWindow" /> and releases them then, which is what makes the allowance a rate rather than a
/// ceiling the process never recovers from. The window is a whole minute per failure rather than a continuous trickle,
/// and a caller that has spent its allowance waits it out; that is a cost only a caller presenting wrong passwords
/// pays, since a right one is refunded at once.
/// </para>
/// <para>
/// Nothing queues: a request that is out of capacity is refused immediately rather than held, because a queue in front
/// of a deliberately expensive verification is a way to make this process hold connections on an attacker's behalf. The
/// partitions are reclaimed once they go idle by the same machinery the endpoint limiters run on, and a partition
/// holding a spent attempt's permits is not idle.
/// </para>
/// <para>
/// <strong>An attempt whose source this deployment cannot tell apart is bounded by username alone.</strong> Behind a
/// reverse proxy every request arrives from the proxy's own address, so a per-source partition there would be one
/// partition for the whole world — and a single guesser emptying it would close password sign-in for every owner at
/// once. <see cref="PasswordAttempt.Source" /> is therefore null in that arrangement, and the source axis is skipped
/// rather than applied to a value that distinguishes nobody.
/// </para>
/// </remarks>
public sealed class PasswordAttemptLimiter : IDisposable
{
    /// <summary>How long a wrong password holds the permits it took.</summary>
    /// <remarks>One minute, because the allowance the surface configures is stated per minute: holding one permit for that long is what makes a permit limit of ten mean ten wrong passwords a minute.</remarks>
    internal static readonly TimeSpan SpentAttemptWindow = TimeSpan.FromMinutes(1);

    private const int DigestLength = 32;

    private readonly TimeProvider timeProvider;
    private readonly byte[] partitionKey = RandomNumberGenerator.GetBytes(DigestLength);
    private readonly PartitionedRateLimiter<PasswordAttempt> perSource;
    private readonly PartitionedRateLimiter<PasswordAttempt> perUsername;

    /// <summary>Initializes the two axes every attempt is counted on.</summary>
    /// <param name="timeProvider">What schedules the release of a wrong password's permits.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider" /> is <see langword="null" />.</exception>
    public PasswordAttemptLimiter(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.timeProvider = timeProvider;

        this.perSource = PartitionedRateLimiter.Create<PasswordAttempt, string>(attempt =>
            RateLimitPartition.GetConcurrencyLimiter(
                this.PartitionFor(attempt.SurfaceName, "source", attempt.Source ?? string.Empty),
                _ => AxisOptions(attempt.AttemptsPerMinute)));

        this.perUsername = PartitionedRateLimiter.Create<PasswordAttempt, string>(attempt =>
            RateLimitPartition.GetConcurrencyLimiter(
                this.PartitionFor(attempt.SurfaceName, "username", attempt.Username),
                _ => AxisOptions(attempt.AttemptsPerMinute)));
    }

    /// <summary>Takes one attempt's capacity on every axis that applies, before the password is verified.</summary>
    /// <param name="attempt">What is being attempted, and the bound the surface it arrived on runs under.</param>
    /// <returns>The reservation, which reports whether it was granted and decides afterwards whether the capacity is returned or spent.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="attempt" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Either axis refusing refuses the attempt, and a refusal releases whatever the other axis had already granted, so
    /// an attempt that never reached a derivation costs nothing. The caller disposes what it gets back on every path:
    /// disposing returns the capacity, and <see cref="PasswordAttemptReservation.Spend" /> is what a wrong password
    /// calls instead.
    /// </remarks>
    public PasswordAttemptReservation Reserve(PasswordAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        var sourceLease = attempt.Source is null ? null : this.perSource.AttemptAcquire(attempt);

        if (sourceLease is { IsAcquired: false })
        {
            sourceLease.Dispose();

            return PasswordAttemptReservation.Refused;
        }

        var usernameLease = this.perUsername.AttemptAcquire(attempt);

        if (!usernameLease.IsAcquired)
        {
            usernameLease.Dispose();
            sourceLease?.Dispose();

            return PasswordAttemptReservation.Refused;
        }

        return new PasswordAttemptReservation(this.timeProvider, sourceLease, usernameLease);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.perSource.Dispose();
        this.perUsername.Dispose();
        CryptographicOperations.ZeroMemory(this.partitionKey);
    }

    /// <summary>What one axis of one surface admits at once.</summary>
    /// <remarks>The permit limit is the surface's configured allowance, and nothing queues, so a caller past the allowance is refused rather than held in front of a derivation it may not be entitled to.</remarks>
    private static ConcurrencyLimiterOptions AxisOptions(int attemptsPerMinute) => new()
    {
        PermitLimit = attemptsPerMinute,
        QueueLimit = 0,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
    };

    /// <summary>Names one partition, without the value it is a partition for being recoverable from the name.</summary>
    /// <remarks>The surface and the axis are written in clear because neither is personal and both are needed for two surfaces' partitions not to merge; only the value itself is digested.</remarks>
    private string PartitionFor(string surfaceName, string axis, string value)
    {
        var encodedValue = GC.AllocateArray<byte>(Encoding.UTF8.GetByteCount(value), pinned: true);

        try
        {
            Encoding.UTF8.GetBytes(value, encodedValue);

            var digest = new byte[DigestLength];
            HMACSHA256.HashData(this.partitionKey, encodedValue, digest);

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{surfaceName}:{axis}:{Convert.ToHexStringLower(digest)}");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encodedValue);
        }
    }
}

/// <summary>One attempt's capacity, taken before the password was verified and returned or spent afterwards.</summary>
/// <remarks>
/// Disposing returns the capacity, which is what a verification that succeeded, threw, or was cancelled leaves behind —
/// none of those is a guess. <see cref="Spend" /> is the one path that costs anything, and after it the reservation owns
/// nothing: the release is scheduled and disposing is a no-op, so <c>using</c> is safe beside it.
/// </remarks>
public sealed class PasswordAttemptReservation : IDisposable
{
    private readonly TimeProvider? timeProvider;
    private readonly RateLimitLease? sourceLease;
    private readonly RateLimitLease? usernameLease;
    private bool spent;

    private PasswordAttemptReservation()
    {
    }

    internal PasswordAttemptReservation(
        TimeProvider timeProvider,
        RateLimitLease? sourceLease,
        RateLimitLease usernameLease)
    {
        this.timeProvider = timeProvider;
        this.sourceLease = sourceLease;
        this.usernameLease = usernameLease;
    }

    /// <summary>Gets the reservation an axis refused, which grants nothing and owns nothing.</summary>
    internal static PasswordAttemptReservation Refused { get; } = new();

    /// <summary>Gets a value indicating whether every axis that applies had capacity for this attempt.</summary>
    public bool IsGranted => this.usernameLease is not null;

    /// <summary>Keeps the capacity this attempt took, because the password it carried was wrong.</summary>
    /// <remarks>
    /// The permits are released after <see cref="PasswordAttemptLimiter.SpentAttemptWindow" /> rather than never, which
    /// is what turns a permit limit into an allowance per minute. Calling it twice, or on a refused reservation, does
    /// nothing.
    /// </remarks>
    public void Spend()
    {
        if (!this.IsGranted || this.spent || this.timeProvider is not { } clock)
        {
            return;
        }

        this.spent = true;

        // Created stopped and started afterwards, so the callback cannot run before the local it disposes has been
        // assigned. The closure is what keeps the timer alive until it fires; nothing else references it.
        ITimer? release = null;
        release = clock.CreateTimer(
            _ =>
            {
                this.ReleasePermits();
                release?.Dispose();
            },
            state: null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);

        release.Change(PasswordAttemptLimiter.SpentAttemptWindow, Timeout.InfiniteTimeSpan);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.spent)
        {
            return;
        }

        this.ReleasePermits();
    }

    private void ReleasePermits()
    {
        // The limiter this capacity was taken from is a singleton disposed as the process ends, and a scheduled release
        // can fire after that. Releasing into a disposed limiter is the end of a process rather than a fault to report.
        try
        {
            this.usernameLease?.Dispose();
            this.sourceLease?.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}

/// <summary>One password attempt, as the limiter counts it.</summary>
/// <param name="SurfaceName">The transport surface the attempt arrived on, so two surfaces' partitions stay apart.</param>
/// <param name="Source">The remote address the attempt came from, or <see langword="null" /> where this deployment cannot tell one caller's address from another's and the username is therefore the whole bound.</param>
/// <param name="Username">The canonical username presented, which reaches only a digest.</param>
/// <param name="AttemptsPerMinute">How many wrong passwords each axis admits in a minute.</param>
/// <remarks><see cref="ToString" /> is redacted, so no diagnostic can print a submitted username or a remote address by rendering the record the limiter was asked with.</remarks>
public sealed record PasswordAttempt(string SurfaceName, string? Source, string Username, int AttemptsPerMinute)
{
    /// <inheritdoc />
    public override string ToString() => $"{nameof(PasswordAttempt)} {{ {this.SurfaceName} }}";
}
