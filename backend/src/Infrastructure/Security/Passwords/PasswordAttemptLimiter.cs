// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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
/// <strong>Two different things are bounded here, and conflating them refuses an owner.</strong> One is how many wrong
/// passwords a minute an axis admits, which is what <c>AttemptsPerMinute</c> states. The other is how many derivations
/// may be in flight for one axis at once, which is what stops a caller opening five hundred connections from making
/// this process perform five hundred concurrent PBKDF2 derivations. They are separate limiters because a permit held
/// across a derivation is also a cap on simultaneous requests, and HTTP Basic re-presents the credential on every
/// request with no session — so a single limit serving both would refuse an owner's eleventh parallel call with the
/// answer a wrong password gets, however right their password was.
/// </para>
/// <para>
/// So a verification permit is taken before the derivation and returned whatever the answer was, while a failure permit
/// is taken <em>only</em> by <see cref="PasswordAttemptReservation.Spend" /> and held for
/// <see cref="SpentAttemptWindow" />, which is what turns <c>AttemptsPerMinute</c> into an allowance per minute rather
/// than a ceiling the process never recovers from. <see cref="Reserve" /> reads the failure axis before it takes a
/// verification permit, so an axis that has spent its allowance is refused before anything expensive happens.
/// </para>
/// <para>
/// Reading the failure axis rather than taking from it leaves a window between the reading and the spending, and
/// <see cref="ConcurrentVerificationsPerPartition" /> is what bounds it: at most that many derivations for one axis are
/// ever in flight, so a burst arriving together can overshoot the stated allowance by at most one such window's worth
/// before the axis closes, rather than by as many connections as an attacker cares to open. That is the honest bound —
/// <c>AttemptsPerMinute</c> plus at most <see cref="ConcurrentVerificationsPerPartition" /> derivations in the first
/// minute of a burst, and <c>AttemptsPerMinute</c> a minute after it.
/// </para>
/// <para>
/// <strong>A per-partition ceiling is not a bound on this process, which is why there is a third one.</strong> Every
/// distinct username is a fresh partition with a fresh ceiling and a fresh allowance, and behind a declared proxy the
/// username is the only axis there is — so a caller presenting five hundred well-formed usernames at once would meet
/// no per-partition limit at all and have this process derive five hundred times concurrently, each derivation
/// deliberately expensive and each occupying a thread. <see cref="ConcurrentVerificationsPerSurface" /> is the bound
/// that does not move with what a caller names: one partition per transport surface, consulted before either of the
/// others, so what a caller can spend of this process is decided by the surface it reached rather than by how many
/// names it thought of.
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
/// <para>
/// <strong>The per-username failure axis is shared with the owner it protects, deliberately.</strong> Somebody who
/// knows a username can spend its allowance on wrong passwords and have that owner's correct password refused until the
/// window elapses. Nothing can avoid it while the answer is unknowable before the derivation, which is what the axis
/// exists to bound; what the design chooses is the cost of it being one minute rather than a lockout an operator has to
/// lift. The source axis is what catches the caller doing it, wherever this deployment can tell one caller from another.
/// </para>
/// </remarks>
public sealed class PasswordAttemptLimiter : IDisposable
{
    /// <summary>How long a wrong password holds the failure permits it took.</summary>
    /// <remarks>One minute, because the allowance the surface configures is stated per minute: holding one permit for that long is what makes a permit limit of ten mean ten wrong passwords a minute.</remarks>
    internal static readonly TimeSpan SpentAttemptWindow = TimeSpan.FromMinutes(1);

    /// <summary>How many password verifications may be in flight for one axis at once.</summary>
    /// <remarks>
    /// Sized for the traffic a surface actually carries rather than for the guessing allowance: a browser opens several
    /// connections to one origin and an agent may issue calls in parallel, and every one of them re-presents the
    /// credential. It is deliberately unrelated to <c>AttemptsPerMinute</c>, which an operator lowers to make guessing
    /// expensive and which must not become a cap on an owner's own parallelism.
    /// </remarks>
    internal const int ConcurrentVerificationsPerPartition = 32;

    /// <summary>How many password verifications one transport surface may have in flight at once, whatever they name.</summary>
    /// <remarks>
    /// Four times the per-partition ceiling, so several owners' partitions can be busy together and the smaller bound
    /// still means something, while the process is never asked for an unbounded number of derivations by a caller that
    /// varies the username. It is per surface rather than per process so a flood at one endpoint does not close
    /// password sign-in at the other, which is the same isolation the endpoint limiters keep.
    /// </remarks>
    internal const int ConcurrentVerificationsPerSurface = 128;

    private const int DigestLength = 32;

    private readonly TimeProvider timeProvider;
    private readonly byte[] partitionKey = RandomNumberGenerator.GetBytes(DigestLength);
    private readonly PartitionedRateLimiter<PasswordAttempt> perSurfaceVerifications;
    private readonly PartitionedRateLimiter<PasswordAttempt> perSourceVerifications;
    private readonly PartitionedRateLimiter<PasswordAttempt> perUsernameVerifications;
    private readonly PartitionedRateLimiter<PasswordAttempt> perSourceFailures;
    private readonly PartitionedRateLimiter<PasswordAttempt> perUsernameFailures;

    /// <summary>Initializes the two axes every attempt is counted on, each bounding derivations in flight and wrong passwords a minute separately.</summary>
    /// <param name="timeProvider">What schedules the release of a wrong password's permits.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider" /> is <see langword="null" />.</exception>
    public PasswordAttemptLimiter(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.timeProvider = timeProvider;

        // Every attempt on one surface digests the same empty value, so this axis holds exactly one partition per
        // surface — which the partition name already names in clear — and is the bound no caller can widen.
        this.perSurfaceVerifications = this.Axis("surface", static _ => string.Empty, static _ => ConcurrentVerificationsPerSurface);
        this.perSourceVerifications = this.Axis("source", Source, static _ => ConcurrentVerificationsPerPartition);
        this.perUsernameVerifications = this.Axis("username", Username, static _ => ConcurrentVerificationsPerPartition);
        this.perSourceFailures = this.Axis("source-failures", Source, static attempt => attempt.AttemptsPerMinute);
        this.perUsernameFailures = this.Axis("username-failures", Username, static attempt => attempt.AttemptsPerMinute);
    }

    /// <summary>Takes one attempt's verification capacity on every axis that applies, before the password is verified.</summary>
    /// <param name="attempt">What is being attempted, and the bound the surface it arrived on runs under.</param>
    /// <returns>The reservation, which reports whether it was granted and decides afterwards whether the attempt cost the allowance anything.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="attempt" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// An axis whose failure allowance is spent refuses the attempt before any permit is taken, and either axis refusing
    /// releases whatever the other had already granted, so an attempt that never reached a derivation costs nothing. The
    /// caller disposes what it gets back on every path: disposing returns the verification capacity, and
    /// <see cref="PasswordAttemptReservation.Spend" /> is what a wrong password calls before that.
    /// </remarks>
    public PasswordAttemptReservation Reserve(PasswordAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        if (!this.HasFailureAllowance(attempt))
        {
            return PasswordAttemptReservation.Refused;
        }

        var surfaceLease = this.perSurfaceVerifications.AttemptAcquire(attempt);

        if (!surfaceLease.IsAcquired)
        {
            surfaceLease.Dispose();

            return PasswordAttemptReservation.Refused;
        }

        var sourceLease = attempt.Source is null ? null : this.perSourceVerifications.AttemptAcquire(attempt);

        if (sourceLease is { IsAcquired: false })
        {
            sourceLease.Dispose();
            surfaceLease.Dispose();

            return PasswordAttemptReservation.Refused;
        }

        var usernameLease = this.perUsernameVerifications.AttemptAcquire(attempt);

        if (!usernameLease.IsAcquired)
        {
            usernameLease.Dispose();
            sourceLease?.Dispose();
            surfaceLease.Dispose();

            return PasswordAttemptReservation.Refused;
        }

        return new PasswordAttemptReservation(this, attempt, surfaceLease, sourceLease, usernameLease);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.perSurfaceVerifications.Dispose();
        this.perSourceVerifications.Dispose();
        this.perUsernameVerifications.Dispose();
        this.perSourceFailures.Dispose();
        this.perUsernameFailures.Dispose();
        CryptographicOperations.ZeroMemory(this.partitionKey);
    }

    /// <summary>Counts one wrong password against every axis that applies, for the window the allowance is stated over.</summary>
    /// <remarks>An axis already at its limit acquires nothing, which needs no handling: its allowance is spent either way, and the next attempt reads it as such.</remarks>
    internal void HoldAFailureAgainst(PasswordAttempt attempt)
    {
        var sourceLease = attempt.Source is null ? null : this.perSourceFailures.AttemptAcquire(attempt);
        var usernameLease = this.perUsernameFailures.AttemptAcquire(attempt);

        this.ReleaseAfterTheWindow(sourceLease, usernameLease);
    }

    /// <summary>Reports whether every axis that applies still admits a wrong password, without taking anything.</summary>
    /// <remarks>A permit count of zero is how a concurrency limiter is asked its state rather than drawn on, so a caller whose password turns out to be right has read nothing away from itself.</remarks>
    private bool HasFailureAllowance(PasswordAttempt attempt)
    {
        if (attempt.Source is not null)
        {
            using var sourceAllowance = this.perSourceFailures.AttemptAcquire(attempt, permitCount: 0);

            if (!sourceAllowance.IsAcquired)
            {
                return false;
            }
        }

        using var usernameAllowance = this.perUsernameFailures.AttemptAcquire(attempt, permitCount: 0);

        return usernameAllowance.IsAcquired;
    }

    /// <summary>Holds a wrong password's permits for the window and gives them back at the end of it.</summary>
    /// <remarks>The timer is created stopped and started afterwards, so the callback cannot run before the local it disposes has been assigned; the closure is what keeps the timer alive until it fires, nothing else referencing it.</remarks>
    private void ReleaseAfterTheWindow(RateLimitLease? sourceLease, RateLimitLease? usernameLease)
    {
        ITimer? release = null;
        release = this.timeProvider.CreateTimer(
            _ =>
            {
                ReleasePermits(sourceLease, usernameLease);
                release?.Dispose();
            },
            state: null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);

        release.Change(SpentAttemptWindow, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Returns whatever the leases hold, tolerating a limiter this process has already torn down.</summary>
    /// <remarks>The limiter is a singleton disposed as the process ends and a scheduled release can fire after that, which is the end of a process rather than a fault to report.</remarks>
    internal static void ReleasePermits(RateLimitLease? first, RateLimitLease? second, RateLimitLease? third = null)
    {
        try
        {
            first?.Dispose();
            second?.Dispose();
            third?.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>Reads what a source axis partitions by, an attempt carrying none never reaching one.</summary>
    private static string Source(PasswordAttempt attempt) => attempt.Source ?? string.Empty;

    /// <summary>Reads what a username axis partitions by.</summary>
    private static string Username(PasswordAttempt attempt) => attempt.Username;

    /// <summary>Builds one axis, partitioned by the value it counts and limited by what that axis bounds.</summary>
    private PartitionedRateLimiter<PasswordAttempt> Axis(
        string axis,
        Func<PasswordAttempt, string> partitionedBy,
        Func<PasswordAttempt, int> permitLimit) =>
        PartitionedRateLimiter.Create<PasswordAttempt, string>(attempt =>
            RateLimitPartition.GetConcurrencyLimiter(
                this.PartitionFor(attempt.SurfaceName, axis, partitionedBy(attempt)),
                _ => new ConcurrencyLimiterOptions
                {
                    PermitLimit = permitLimit(attempt),
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                }));

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

/// <summary>One attempt's verification capacity, taken before the password was verified and returned afterwards.</summary>
/// <remarks>
/// Disposing returns the capacity, which is what every answer leaves behind — a right password, a throw, and a
/// cancellation alike. <see cref="Spend" /> is the one path that costs the allowance anything: it counts the wrong
/// password against the failure axes for a minute and returns the verification capacity immediately, so
/// <c>using</c> is safe beside it.
/// </remarks>
public sealed class PasswordAttemptReservation : IDisposable
{
    private readonly PasswordAttemptLimiter? limiter;
    private readonly PasswordAttempt? attempt;
    private readonly RateLimitLease? surfaceLease;
    private readonly RateLimitLease? sourceLease;
    private readonly RateLimitLease? usernameLease;
    private bool released;

    private PasswordAttemptReservation()
    {
    }

    internal PasswordAttemptReservation(
        PasswordAttemptLimiter limiter,
        PasswordAttempt attempt,
        RateLimitLease surfaceLease,
        RateLimitLease? sourceLease,
        RateLimitLease usernameLease)
    {
        this.limiter = limiter;
        this.attempt = attempt;
        this.surfaceLease = surfaceLease;
        this.sourceLease = sourceLease;
        this.usernameLease = usernameLease;
    }

    /// <summary>Gets the reservation an axis refused, which grants nothing and owns nothing.</summary>
    internal static PasswordAttemptReservation Refused { get; } = new();

    /// <summary>Gets a value indicating whether every axis that applies had capacity for this attempt.</summary>
    public bool IsGranted => this.usernameLease is not null;

    /// <summary>Counts this attempt against the allowance, because the password it carried was wrong.</summary>
    /// <remarks>
    /// The failure permits are held for <see cref="PasswordAttemptLimiter.SpentAttemptWindow" /> and released then,
    /// while the verification capacity is returned at once — it bounds derivations in flight rather than guesses.
    /// Calling it twice, or on a refused reservation, does nothing.
    /// </remarks>
    public void Spend()
    {
        if (!this.IsGranted || this.released || this.limiter is not { } bound)
        {
            return;
        }

        bound.HoldAFailureAgainst(this.attempt!);

        this.Release();
    }

    /// <inheritdoc />
    public void Dispose() => this.Release();

    private void Release()
    {
        if (this.released)
        {
            return;
        }

        this.released = true;

        PasswordAttemptLimiter.ReleasePermits(this.usernameLease, this.sourceLease, this.surfaceLease);
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
