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
/// endpoint rather than on the guessing. This is the bound on the guessing, and it is two buckets because the two
/// attacks are different: one host trying many passwords is caught per source, and many hosts trying one account's
/// passwords are caught per username.
/// </para>
/// <para>
/// A username never reaches a partition key. Both keys are HMAC-SHA-256 digests under a key this process generates at
/// construction and never publishes, so what is held in memory, what a dump of this object would show, and what any
/// future metric could tag are all a value nothing outside this process can reverse — while two attempts against one
/// username still land in one bucket, which is the whole of what the limiter needs. The source is digested for the same
/// reason, a remote address being personal data in its own right.
/// </para>
/// <para>
/// Both buckets are token buckets that replenish on the framework's own timer, so capacity returns without anything
/// here remembering to restore it, and the partitions are reclaimed once they go idle by the same machinery the
/// endpoint limiters run on. Nothing queues: a request that is out of capacity is refused immediately rather than held,
/// because a queue in front of a deliberately expensive verification is a way to make this process hold connections on
/// an attacker's behalf.
/// </para>
/// <para>
/// The source bucket is spent before the username bucket is asked, so an attempt refused for its username has still
/// cost its source a token. That is the direction to be wrong in: it makes an attacker spreading guesses across many
/// usernames pay for each one.
/// </para>
/// </remarks>
public sealed class PasswordAttemptLimiter : IDisposable
{
    private const int DigestLength = 32;

    private readonly byte[] partitionKey = RandomNumberGenerator.GetBytes(DigestLength);
    private readonly PartitionedRateLimiter<PasswordAttempt> perSource;
    private readonly PartitionedRateLimiter<PasswordAttempt> perUsername;

    /// <summary>Initializes the two buckets every attempt is counted in.</summary>
    public PasswordAttemptLimiter()
    {
        this.perSource = PartitionedRateLimiter.Create<PasswordAttempt, string>(attempt =>
            RateLimitPartition.GetTokenBucketLimiter(
                this.PartitionFor(attempt.SurfaceName, "source", attempt.Source),
                _ => BucketOptions(attempt.AttemptsPerMinute)));

        this.perUsername = PartitionedRateLimiter.Create<PasswordAttempt, string>(attempt =>
            RateLimitPartition.GetTokenBucketLimiter(
                this.PartitionFor(attempt.SurfaceName, "username", attempt.Username),
                _ => BucketOptions(attempt.AttemptsPerMinute)));
    }

    /// <summary>Spends one attempt's capacity, or reports that there is none.</summary>
    /// <param name="attempt">What is being attempted, and the bound the surface it arrived on runs under.</param>
    /// <returns><see langword="true" /> when the attempt may proceed to a password verification.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="attempt" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The leases are released immediately, which is what a token bucket expects: a spent token comes back on the
    /// replenishment schedule rather than when the request that spent it finishes.
    /// </remarks>
    public bool TryBeginAttempt(PasswordAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        using var sourceLease = this.perSource.AttemptAcquire(attempt);

        if (!sourceLease.IsAcquired)
        {
            return false;
        }

        using var usernameLease = this.perUsername.AttemptAcquire(attempt);

        return usernameLease.IsAcquired;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.perSource.Dispose();
        this.perUsername.Dispose();
        CryptographicOperations.ZeroMemory(this.partitionKey);
    }

    private static TokenBucketRateLimiterOptions BucketOptions(int attemptsPerMinute) => new()
    {
        AutoReplenishment = true,
        TokenLimit = attemptsPerMinute,
        TokensPerPeriod = attemptsPerMinute,
        ReplenishmentPeriod = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
    };

    /// <summary>Names one bucket, without the value it is a bucket for being recoverable from the name.</summary>
    /// <remarks>The surface and the axis are written in clear because neither is personal and both are needed for two surfaces' buckets not to merge; only the value itself is digested.</remarks>
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

/// <summary>One password attempt, as the limiter counts it.</summary>
/// <param name="SurfaceName">The transport surface the attempt arrived on, so two surfaces' buckets stay apart.</param>
/// <param name="Source">The remote address the attempt came from, or a fixed word where the request carried none.</param>
/// <param name="Username">The canonical username presented, which reaches only a digest.</param>
/// <param name="AttemptsPerMinute">How many attempts each of the two buckets replenishes to.</param>
/// <remarks><see cref="ToString" /> is redacted, so no diagnostic can print a submitted username or a remote address by rendering the record the limiter was asked with.</remarks>
public sealed record PasswordAttempt(string SurfaceName, string Source, string Username, int AttemptsPerMinute)
{
    /// <inheritdoc />
    public override string ToString() => $"{nameof(PasswordAttempt)} {{ {this.SurfaceName} }}";
}
