// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Security.Transport;

/// <summary>How much traffic one process serves on a transport surface, and how fast one caller may ask for it.</summary>
/// <remarks>
/// <para>
/// Two controls rather than one, because they bound different resources. The concurrency limit bounds what the process
/// is doing at any instant — database connections, CPU, open response streams — and is shared by every caller of that
/// surface, since a machine has one set of them. The token bucket bounds how often a single caller may ask, and is kept
/// per caller, so a client that goes into a loop spends its own capacity rather than everyone's.
/// </para>
/// <para>
/// Nothing here names a surface. The same numbers describe the MCP endpoint and the administrative endpoint, and each
/// surface is given an instance of its own, so what one endpoint is configured to permit says nothing about the other
/// and neither endpoint's traffic reaches the other's limiters.
/// </para>
/// <para>
/// Both are in-process. Nothing here coordinates across instances, so a deployment running several processes enforces
/// these numbers once per process rather than once in total, and none of it is protection against a distributed flood.
/// The controls exist so that one misbehaving caller cannot exhaust the resources of the process it is talking to.
/// </para>
/// <para>
/// Queue limits default to zero throughout. A queued request is a request holding memory and a connection while it waits
/// for capacity that is already gone, which turns an overload into a slower, larger overload; refusing it immediately
/// tells the client to back off while the server is still healthy. A deployment that would rather absorb a short burst
/// can configure a bounded queue, and bounded is the only shape available.
/// </para>
/// <para>
/// A caller queue costs more than it looks like it does, which is why <see cref="RequestQueueLimit" /> is bounded by
/// <see cref="MaxConcurrentRequests" /> rather than only by its own range. The two limiters are acquired in order, so a
/// request waiting for its caller's tokens has already taken a concurrency permit and holds it until the next
/// replenishment — up to an hour away. Keeping the queue smaller than the permit count is what stops one caller out of
/// capacity from parking every permit the surface has and refusing everyone else through a limit that is supposed to be
/// its own.
/// </para>
/// </remarks>
public sealed class TransportRateLimits
{
    private TransportRateLimits(
        int maxConcurrentRequests,
        int concurrencyQueueLimit,
        int tokenCapacity,
        int tokensPerReplenishmentPeriod,
        TimeSpan replenishmentPeriod,
        int requestQueueLimit)
    {
        this.MaxConcurrentRequests = maxConcurrentRequests;
        this.ConcurrencyQueueLimit = concurrencyQueueLimit;
        this.TokenCapacity = tokenCapacity;
        this.TokensPerReplenishmentPeriod = tokensPerReplenishmentPeriod;
        this.ReplenishmentPeriod = replenishmentPeriod;
        this.RequestQueueLimit = requestQueueLimit;
    }

    /// <summary>Gets the limits a deployment that configures nothing runs under.</summary>
    /// <remarks>
    /// <para>
    /// The numbers are sized for the work a request actually does here: every MCP tool answers from the local mailbox
    /// copy with a bounded query, so a request is short and database-bound rather than long and compute-bound. Twenty
    /// concurrent requests keep an endpoint well inside the connection pool the synchronization workers share, and one
    /// request per second with a sixty-request burst covers an agent that lists a page and then reads what it found
    /// while still costing an unattended loop its capacity within a second.
    /// </para>
    /// <para>
    /// One set of defaults for both surfaces rather than a second set sized for administration. An administrative
    /// request does strictly less work than a tool call and arrives from a command a person is running, so numbers
    /// comfortable for an agent are more than comfortable for it; a second set would be one more thing to keep sound in
    /// exchange for a bound nobody was reaching.
    /// </para>
    /// </remarks>
    public static TransportRateLimits Default { get; } = Create(
        maxConcurrentRequests: 20,
        concurrencyQueueLimit: 0,
        tokenCapacity: 60,
        tokensPerReplenishmentPeriod: 60,
        replenishmentPeriod: TimeSpan.FromMinutes(1),
        requestQueueLimit: 0);

    /// <summary>Gets how many requests the process serves at once on this surface, across every caller.</summary>
    public int MaxConcurrentRequests { get; }

    /// <summary>Gets how many requests wait for a concurrency slot before the rest are refused.</summary>
    public int ConcurrencyQueueLimit { get; }

    /// <summary>Gets the largest burst one caller may spend at once.</summary>
    public int TokenCapacity { get; }

    /// <summary>Gets how much of that burst one caller gets back each <see cref="ReplenishmentPeriod" />.</summary>
    public int TokensPerReplenishmentPeriod { get; }

    /// <summary>Gets how often a caller's spent capacity is restored.</summary>
    public TimeSpan ReplenishmentPeriod { get; }

    /// <summary>Gets how many of one caller's requests wait for capacity before the rest are refused.</summary>
    public int RequestQueueLimit { get; }

    /// <summary>Creates the limits a transport surface runs under.</summary>
    /// <param name="maxConcurrentRequests">How many requests the process serves at once on this surface.</param>
    /// <param name="concurrencyQueueLimit">How many requests wait for a concurrency slot.</param>
    /// <param name="tokenCapacity">The largest burst one caller may spend at once.</param>
    /// <param name="tokensPerReplenishmentPeriod">How much capacity one caller gets back each period.</param>
    /// <param name="replenishmentPeriod">How often a caller's spent capacity is restored.</param>
    /// <param name="requestQueueLimit">How many of one caller's requests wait for capacity.</param>
    /// <returns>The limits.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a value would leave the surface unbounded, unable to serve anything, unable to recover the capacity it hands out, or able to let one caller hold every concurrency permit.</exception>
    /// <remarks>
    /// The guards here are the invariants the type cannot exist without, not the ranges an operator is held to. A
    /// deployment's settings are checked against those before they reach this method, so that an operator reads every
    /// mistake at once instead of the first one to throw.
    /// </remarks>
    public static TransportRateLimits Create(
        int maxConcurrentRequests,
        int concurrencyQueueLimit,
        int tokenCapacity,
        int tokensPerReplenishmentPeriod,
        TimeSpan replenishmentPeriod,
        int requestQueueLimit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrentRequests, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(concurrencyQueueLimit);
        ArgumentOutOfRangeException.ThrowIfLessThan(tokenCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(tokensPerReplenishmentPeriod, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(replenishmentPeriod, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegative(requestQueueLimit);

        // A period that hands out more than the bucket holds is not a faster limit, it is a different one: the surplus
        // is discarded on every replenishment, so the rate an operator wrote down is never the rate that applies.
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tokensPerReplenishmentPeriod, tokenCapacity);

        // A request waiting for its caller's capacity is already holding a concurrency permit, because the two limiters
        // are acquired in that order. A caller queue as large as the permit count therefore lets one caller out of
        // tokens hold every permit the surface has until its next replenishment, which is the isolation these two
        // controls exist to provide, inverted.
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(requestQueueLimit, maxConcurrentRequests);

        return new TransportRateLimits(
            maxConcurrentRequests,
            concurrencyQueueLimit,
            tokenCapacity,
            tokensPerReplenishmentPeriod,
            replenishmentPeriod,
            requestQueueLimit);
    }
}
