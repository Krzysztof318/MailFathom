// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Security.Transport;

/// <summary>How much traffic one process serves on a transport surface, and how much of it one user may spend.</summary>
/// <remarks>
/// <para>
/// Three controls rather than one, because they bound different resources. The process-wide concurrency limit bounds
/// what the process is doing at any instant — database connections, CPU, open response streams — and is shared by every
/// caller of that surface, since a machine has one set of them. The per-user concurrency limit bounds how much of that
/// one user may hold at once, so one user's burst leaves permits for everybody else. The token bucket bounds how often
/// one user may ask, so a client that goes into a loop spends its own user's capacity rather than everyone's.
/// </para>
/// <para>
/// The numbers are the deployment's and the allowance is each user's. Every user is given a bucket and a concurrency
/// allowance of the configured size, independently of every other user; nothing about a user, a credential, or an
/// organization carries a number of its own. Every credential one user holds spends that user's one allowance, so
/// holding a second credential buys no second bucket.
/// </para>
/// <para>
/// Nothing here names a surface. The same numbers describe every bounded endpoint, and each surface is given an
/// instance of its own, so what one endpoint is configured to permit says nothing about another and no endpoint's
/// traffic reaches another's limiters.
/// </para>
/// <para>
/// All three are in-process. Nothing here coordinates across instances, so a deployment running several processes
/// enforces these numbers once per process rather than once in total, and none of it is protection against a
/// distributed flood. The controls exist so that one misbehaving caller cannot exhaust the resources of the process it is
/// talking to.
/// </para>
/// <para>
/// Queue limits default to zero throughout. A queued request is a request holding memory and a connection while it waits
/// for capacity that is already gone, which turns an overload into a slower, larger overload; refusing it immediately
/// tells the client to back off while the server is still healthy. A deployment that would rather absorb a short burst
/// can configure a bounded queue, and bounded is the only shape available.
/// </para>
/// <para>
/// A caller queue costs more than it looks like it does, which is why <see cref="RequestQueueLimit" /> is bounded by
/// <see cref="MaxConcurrentRequests" /> rather than only by its own range. The concurrency limiters are acquired before
/// the bucket, so a request waiting for its user's tokens has already taken a concurrency permit and holds it until the
/// next replenishment — up to an hour away. Keeping the queue smaller than the permit count is what stops one user out of
/// capacity from parking every permit the surface has and refusing everyone else through a limit that is supposed to be
/// their own.
/// </para>
/// </remarks>
public sealed class TransportRateLimits
{
    private TransportRateLimits(
        int maxConcurrentRequests,
        int maxConcurrentRequestsPerUser,
        int concurrencyQueueLimit,
        int tokenCapacity,
        int tokensPerReplenishmentPeriod,
        TimeSpan replenishmentPeriod,
        int requestQueueLimit)
    {
        this.MaxConcurrentRequests = maxConcurrentRequests;
        this.MaxConcurrentRequestsPerUser = maxConcurrentRequestsPerUser;
        this.ConcurrencyQueueLimit = concurrencyQueueLimit;
        this.TokenCapacity = tokenCapacity;
        this.TokensPerReplenishmentPeriod = tokensPerReplenishmentPeriod;
        this.ReplenishmentPeriod = replenishmentPeriod;
        this.RequestQueueLimit = requestQueueLimit;
    }

    /// <summary>Gets the limits a deployment that configures nothing runs under.</summary>
    /// <remarks>
    /// <para>
    /// Sized for a deployment serving a team rather than one person. A request here is short and database-bound: every
    /// MCP tool and every client read answers from the local mailbox copy with a bounded query. Forty-eight requests at
    /// once per surface keep a replica inside the connection pool its synchronization workers share while leaving room
    /// for a roster whose users are not all busy in the same instant.
    /// </para>
    /// <para>
    /// Eight at once for one user is a browser opening its six connections to one origin plus an agent issuing a couple
    /// of calls in parallel, and it means six users saturating their own allowance together still leave the process
    /// permits to serve a seventh. A burst of a hundred and twenty restored at two a second covers one user's client
    /// opening folders and an agent listing and reading beside it — every credential of that user spends the same
    /// bucket — while still costing an unattended loop its capacity within a minute.
    /// </para>
    /// <para>
    /// One set of defaults for every surface rather than a second set sized for administration. An administrative
    /// request does strictly less work than a tool call and arrives from a command a person is running, so numbers
    /// comfortable for a user's agents are more than comfortable for it; a second set would be one more thing to keep
    /// sound in exchange for a bound nobody was reaching.
    /// </para>
    /// </remarks>
    public static TransportRateLimits Default { get; } = Create(
        maxConcurrentRequests: 48,
        maxConcurrentRequestsPerUser: 8,
        concurrencyQueueLimit: 0,
        tokenCapacity: 120,
        tokensPerReplenishmentPeriod: 120,
        replenishmentPeriod: TimeSpan.FromMinutes(1),
        requestQueueLimit: 0);

    /// <summary>Gets how many requests the process serves at once on this surface, across every user.</summary>
    public int MaxConcurrentRequests { get; }

    /// <summary>Gets how many requests one user may have served at once on this surface.</summary>
    /// <remarks>Always below <see cref="MaxConcurrentRequests" />, so one user's requests never hold every permit the process has.</remarks>
    public int MaxConcurrentRequestsPerUser { get; }

    /// <summary>Gets how many requests wait for a process-wide concurrency slot before the rest are refused.</summary>
    public int ConcurrencyQueueLimit { get; }

    /// <summary>Gets the largest burst one user may spend at once.</summary>
    public int TokenCapacity { get; }

    /// <summary>Gets how much of that burst one user gets back each <see cref="ReplenishmentPeriod" />.</summary>
    public int TokensPerReplenishmentPeriod { get; }

    /// <summary>Gets how often a user's spent capacity is restored.</summary>
    public TimeSpan ReplenishmentPeriod { get; }

    /// <summary>Gets how many of one user's requests wait for capacity before the rest are refused.</summary>
    public int RequestQueueLimit { get; }

    /// <summary>Creates the limits a transport surface runs under.</summary>
    /// <param name="maxConcurrentRequests">How many requests the process serves at once on this surface.</param>
    /// <param name="maxConcurrentRequestsPerUser">How many requests one user may have served at once on this surface.</param>
    /// <param name="concurrencyQueueLimit">How many requests wait for a process-wide concurrency slot.</param>
    /// <param name="tokenCapacity">The largest burst one user may spend at once.</param>
    /// <param name="tokensPerReplenishmentPeriod">How much capacity one user gets back each period.</param>
    /// <param name="replenishmentPeriod">How often a user's spent capacity is restored.</param>
    /// <param name="requestQueueLimit">How many of one user's requests wait for capacity.</param>
    /// <returns>The limits.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a value would leave the surface unbounded, unable to serve anything, unable to recover the capacity it hands out, or able to let one user hold every concurrency permit.</exception>
    /// <remarks>
    /// The guards here are the invariants the type cannot exist without, not the ranges an operator is held to. A
    /// deployment's settings are checked against those before they reach this method, so that an operator reads every
    /// mistake at once instead of the first one to throw.
    /// </remarks>
    public static TransportRateLimits Create(
        int maxConcurrentRequests,
        int maxConcurrentRequestsPerUser,
        int concurrencyQueueLimit,
        int tokenCapacity,
        int tokensPerReplenishmentPeriod,
        TimeSpan replenishmentPeriod,
        int requestQueueLimit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrentRequests, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrentRequestsPerUser, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(concurrencyQueueLimit);
        ArgumentOutOfRangeException.ThrowIfLessThan(tokenCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(tokensPerReplenishmentPeriod, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(replenishmentPeriod, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegative(requestQueueLimit);

        // A user allowed as many requests at once as the whole process serves is a user whose burst refuses everybody
        // else, which is the one outcome the per-user limit exists to rule out.
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(maxConcurrentRequestsPerUser, maxConcurrentRequests);

        // A period that hands out more than the bucket holds is not a faster limit, it is a different one: the surplus
        // is discarded on every replenishment, so the rate an operator wrote down is never the rate that applies.
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tokensPerReplenishmentPeriod, tokenCapacity);

        // A request waiting for its user's capacity is already holding a concurrency permit, because the limiters are
        // acquired in that order. A caller queue as large as the permit count therefore lets one user out of tokens
        // hold every permit the surface has until its next replenishment, which is the isolation these controls exist
        // to provide, inverted.
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(requestQueueLimit, maxConcurrentRequests);

        return new TransportRateLimits(
            maxConcurrentRequests,
            maxConcurrentRequestsPerUser,
            concurrencyQueueLimit,
            tokenCapacity,
            tokensPerReplenishmentPeriod,
            replenishmentPeriod,
            requestQueueLimit);
    }
}
