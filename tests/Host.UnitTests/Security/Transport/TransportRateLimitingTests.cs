// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Claims;
using System.Threading.RateLimiting;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Mcp;
using MailFathom.Host.Security.Transport;
using MailFathom.Infrastructure.Security.Transport;
using MailFathom.Mcp;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.Transport;

/// <summary>Covers the limits a bounded transport surface admits traffic under.</summary>
/// <remarks>
/// Nothing here observes a clock, and nothing here is raced by one. A test that spends a caller's capacity builds its
/// bucket with the replenishment timer off, so a process that is suspended, debugged, or merely slow between two
/// assertions cannot have capacity returned underneath it and turn an expected refusal into a pass. That leaves
/// replenishment itself untested by construction, which is deliberate: <c>TokenBucketRateLimiter.TryReplenish</c> is
/// gated on elapsed wall-clock time, so driving it would be a test of the clock. Restoring capacity on schedule is the
/// framework's own contract; what belongs here is that the schedule reaching it is the one an operator configured, and
/// that automatic replenishment is on in production so nothing in this process has to pump it — both asserted against
/// the limiter description an endpoint is built from.
/// </remarks>
public sealed class TransportRateLimitingTests
{
    private const string AdminRoute = AdminEndpointOptions.RoutePrefix + "/session";

    [Fact]
    public void ProcessConcurrencyOptions_CarriesTheConfiguredConcurrency()
    {
        // Arrange
        var limits = Limits(maxConcurrentRequests: 9, concurrencyQueueLimit: 4);

        // Act
        var limiterOptions = TransportRateLimiting.ProcessConcurrencyOptions(limits);

        // Assert
        Assert.Equal(9, limiterOptions.PermitLimit);
        Assert.Equal(4, limiterOptions.QueueLimit);
        Assert.Equal(QueueProcessingOrder.OldestFirst, limiterOptions.QueueProcessingOrder);
    }

    [Fact]
    public void CallerBucketOptions_CarriesTheConfiguredBurstAndSchedule()
    {
        // Arrange
        var limits = Limits(
            tokenCapacity: 30,
            tokensPerReplenishmentPeriod: 5,
            replenishmentPeriod: TimeSpan.FromSeconds(20),
            requestQueueLimit: 2);

        // Act
        var limiterOptions = TransportRateLimiting.CallerBucketOptions(limits);

        // Assert
        Assert.Equal(30, limiterOptions.TokenLimit);
        Assert.Equal(5, limiterOptions.TokensPerPeriod);
        Assert.Equal(TimeSpan.FromSeconds(20), limiterOptions.ReplenishmentPeriod);
        Assert.Equal(2, limiterOptions.QueueLimit);
        Assert.Equal(QueueProcessingOrder.OldestFirst, limiterOptions.QueueProcessingOrder);
    }

    [Fact]
    public void CallerBucketOptions_RestoresCapacityWithoutBeingPumped()
    {
        // Arrange
        var limits = Limits();

        // Act
        var limiterOptions = TransportRateLimiting.CallerBucketOptions(limits);

        // Assert
        // Automatic replenishment is what makes a caller's capacity return on the framework's own timer. With it off,
        // something in this process would have to call TryReplenish, and nothing does, so a spent bucket would stay
        // spent for the life of the process.
        Assert.True(limiterOptions.AutoReplenishment);
    }

    [Theory]
    [InlineData("/started")]
    [InlineData("/health")]
    [InlineData("/alive")]
    [InlineData("/")]
    public void PartitionForProcess_ForARequestOnNoBoundedSurface_AppliesNoLimit(string unboundedPath)
    {
        // Arrange
        var boundedSurfaces = BothSurfaces(Limits(maxConcurrentRequests: 1), Limits(maxConcurrentRequests: 1));
        using var limiter = ProcessLimiter(boundedSurfaces);

        // Act
        var leases = AcquireAll(limiter, () => RequestTo(unboundedPath), attempts: 5);

        // Assert
        // A throttled probe fails, and a failed liveness probe restarts a process that was answering correctly, so a
        // limiter on the probe listener would turn a burst of polling into an outage. The process-wide limiter excludes
        // every route belonging to no bounded surface rather than counting them against some surface's permits.
        Assert.All(leases, lease => Assert.True(lease.IsAcquired));

        DisposeAll(leases);
    }

    [Fact]
    public void PartitionForProcess_ForMcpRequests_AdmitsExactlyTheConfiguredConcurrency()
    {
        // Arrange
        var boundedSurfaces = McpOnly(Limits(maxConcurrentRequests: 3));
        using var limiter = ProcessLimiter(boundedSurfaces);

        // Act
        var leases = AcquireAll(limiter, () => McpRequest(), attempts: 4);

        // Assert
        Assert.Equal([true, true, true, false], leases.Select(lease => lease.IsAcquired));

        DisposeAll(leases);
    }

    [Fact]
    public void PartitionForProcess_ForAdministrativeRequests_AdmitsExactlyTheConfiguredConcurrency()
    {
        // Arrange
        var boundedSurfaces = AdminOnly(Limits(maxConcurrentRequests: 2));
        using var limiter = ProcessLimiter(boundedSurfaces);

        // Act
        var leases = AcquireAll(limiter, () => AdminRequest(), attempts: 3);

        // Assert
        Assert.Equal([true, true, false], leases.Select(lease => lease.IsAcquired));

        DisposeAll(leases);
    }

    /// <summary>
    /// One endpoint saturating the process must not refuse the other's callers. Reading a mailbox and administering the
    /// service that reads it are separate authorities configured separately, and a shared permit count would let a
    /// runaway agent lock an operator out of the surface they would fix it from.
    /// </summary>
    [Fact]
    public void PartitionForProcess_WithOneSurfaceSaturated_LeavesTheOthersConcurrencyIntact()
    {
        // Arrange
        var boundedSurfaces = BothSurfaces(
            mcpLimits: Limits(maxConcurrentRequests: 1),
            adminLimits: Limits(maxConcurrentRequests: 1));
        using var limiter = ProcessLimiter(boundedSurfaces);

        // Act
        var mcpAdmitted = limiter.AttemptAcquire(McpRequest());
        var mcpRefused = limiter.AttemptAcquire(McpRequest());
        var administrative = limiter.AttemptAcquire(AdminRequest());

        // Assert
        Assert.True(mcpAdmitted.IsAcquired);
        Assert.False(mcpRefused.IsAcquired);
        Assert.True(administrative.IsAcquired);

        DisposeAll([mcpAdmitted, mcpRefused, administrative]);
    }

    /// <summary>An endpoint an operator left unbounded takes no capacity from the one they bounded, and is not bounded by it either.</summary>
    [Fact]
    public void PartitionForProcess_ForASurfaceThatIsNotBounded_AppliesNoLimit()
    {
        // Arrange
        var boundedSurfaces = McpOnly(Limits(maxConcurrentRequests: 1));
        using var limiter = ProcessLimiter(boundedSurfaces);

        // Act
        var leases = AcquireAll(limiter, () => AdminRequest(), attempts: 4);

        // Assert
        Assert.All(leases, lease => Assert.True(lease.IsAcquired));

        DisposeAll(leases);
    }

    [Fact]
    public void PartitionForProcess_ForSeveralClients_CountsThemAgainstOneLimit()
    {
        // Arrange
        var boundedSurfaces = McpOnly(Limits(maxConcurrentRequests: 2));
        using var limiter = ProcessLimiter(boundedSurfaces);

        // Act
        var first = limiter.AttemptAcquire(McpRequest("desktop-agent"));
        var second = limiter.AttemptAcquire(McpRequest("nightly-indexer"));
        var third = limiter.AttemptAcquire(McpRequest("third-client"));

        // Assert
        // The concurrency limit bounds what the process is serving on one surface, which has one set of connections and
        // threads however many clients there are; splitting it per client would let the total grow with the client list.
        Assert.True(first.IsAcquired);
        Assert.True(second.IsAcquired);
        Assert.False(third.IsAcquired);

        DisposeAll([first, second, third]);
    }

    [Fact]
    public async Task PartitionForProcess_WithNoQueue_RefusesRatherThanWaiting()
    {
        // Arrange
        var boundedSurfaces = McpOnly(Limits(maxConcurrentRequests: 1, concurrencyQueueLimit: 0));
        using var limiter = ProcessLimiter(boundedSurfaces);
        using var held = limiter.AttemptAcquire(McpRequest());

        // Act
        var refusal = limiter
            .AcquireAsync(McpRequest(), permitCount: 1, TestContext.Current.CancellationToken)
            .AsTask();

        // Assert
        Assert.True(refusal.IsCompleted);
        using var refusedLease = await refusal;
        Assert.False(refusedLease.IsAcquired);
    }

    [Fact]
    public async Task PartitionForProcess_WithABoundedQueue_LetsThatManyWaitAndRefusesTheRest()
    {
        // Arrange
        var boundedSurfaces = McpOnly(Limits(maxConcurrentRequests: 1, concurrencyQueueLimit: 1));
        using var limiter = ProcessLimiter(boundedSurfaces);
        var held = limiter.AttemptAcquire(McpRequest());

        // Act
        var queued = limiter
            .AcquireAsync(McpRequest(), permitCount: 1, TestContext.Current.CancellationToken)
            .AsTask();
        var beyondTheQueue = limiter
            .AcquireAsync(McpRequest(), permitCount: 1, TestContext.Current.CancellationToken)
            .AsTask();

        // Assert
        Assert.False(queued.IsCompleted);
        Assert.True(beyondTheQueue.IsCompleted);
        using var refusedLease = await beyondTheQueue;
        Assert.False(refusedLease.IsAcquired);

        held.Dispose();

        using var admittedLease = await queued;
        Assert.True(admittedLease.IsAcquired);
    }

    [Fact]
    public void PartitionForCaller_ExhaustingOneClientsBurst_LeavesAnotherClientUntouched()
    {
        // Arrange
        var limits = Limits(tokenCapacity: 2, tokensPerReplenishmentPeriod: 2);
        using var limiter = CallerLimiter(TransportSurface.Mcp, limits);

        // Act
        var spent = AcquireAll(limiter, () => McpRequest("desktop-agent"), attempts: 3);
        var otherClient = limiter.AttemptAcquire(McpRequest("nightly-indexer"));

        // Assert
        Assert.Equal([true, true, false], spent.Select(lease => lease.IsAcquired));
        Assert.True(otherClient.IsAcquired);

        DisposeAll([.. spent, otherClient]);
    }

    /// <summary>
    /// The two endpoints' key lists are configured separately and neither consults the other's, so one name can be
    /// spelled under both. A client that spent the MCP endpoint's burst must not arrive at the administrative endpoint
    /// already out of capacity, nor the other way about.
    /// </summary>
    [Fact]
    public void PartitionForCaller_ExhaustingOneSurfacesBurst_LeavesTheOtherSurfaceUntouched()
    {
        // Arrange
        var limits = Limits(tokenCapacity: 1, tokensPerReplenishmentPeriod: 1);
        using var limiter = CallerLimiterForBothSurfaces(limits);

        // Act
        var spentOnMcp = AcquireAll(limiter, () => McpRequest("workstation"), attempts: 2);
        var onAdmin = limiter.AttemptAcquire(AdminRequest("workstation"));
        var spentOnAdmin = limiter.AttemptAcquire(AdminRequest("workstation"));

        // Assert
        Assert.Equal([true, false], spentOnMcp.Select(lease => lease.IsAcquired));
        Assert.True(onAdmin.IsAcquired);
        Assert.False(spentOnAdmin.IsAcquired);

        DisposeAll([.. spentOnMcp, onAdmin, spentOnAdmin]);
    }

    /// <summary>The anonymous partition is the one a flood of bad credentials lands in, and it is per surface for the same reason a client's is.</summary>
    [Fact]
    public void PartitionForCaller_ExhaustingOneSurfacesAnonymousBurst_LeavesTheOtherSurfaceUntouched()
    {
        // Arrange
        var limits = Limits(tokenCapacity: 1, tokensPerReplenishmentPeriod: 1);
        using var limiter = CallerLimiterForBothSurfaces(limits);

        // Act
        var spentOnAdmin = AcquireAll(limiter, () => AdminRequest(), attempts: 2);
        var onMcp = limiter.AttemptAcquire(McpRequest());

        // Assert
        Assert.Equal([true, false], spentOnAdmin.Select(lease => lease.IsAcquired));
        Assert.True(onMcp.IsAcquired);

        DisposeAll([.. spentOnAdmin, onMcp]);
    }

    [Fact]
    public void PartitionForCaller_WithoutAnAuthenticatedIdentity_SharesOneAnonymousPartition()
    {
        // Arrange
        var limits = Limits(tokenCapacity: 2, tokensPerReplenishmentPeriod: 2);
        using var limiter = CallerLimiter(TransportSurface.Mcp, limits);

        // Act
        // A request that presented nothing and a request whose credential was refused both arrive with an
        // unauthenticated principal, and both are counted under the one anonymous partition — which is what stops an
        // attacker minting a partition per request and stops a flood of bad credentials being served for free.
        var presentedNothing = limiter.AttemptAcquire(McpRequest());
        var credentialRefused = limiter.AttemptAcquire(McpRequest(authenticatedClientName: null));
        var beyondTheSharedBurst = limiter.AttemptAcquire(McpRequest());

        // Assert
        Assert.True(presentedNothing.IsAcquired);
        Assert.True(credentialRefused.IsAcquired);
        Assert.False(beyondTheSharedBurst.IsAcquired);

        DisposeAll([presentedNothing, credentialRefused, beyondTheSharedBurst]);
    }

    /// <summary>
    /// The administrative endpoint judges its credential in the authorization middleware, which runs behind the limiter
    /// so that a wrong key still costs capacity. There is therefore no identity to partition on, and every
    /// administrative caller shares the surface's one bucket — the strongest bound available against the guessing this
    /// endpoint is exposed to, and the reason its burst is the endpoint's rather than one caller's.
    /// </summary>
    [Fact]
    public void PartitionForCaller_ForAdministrativeRequests_CountsEveryCallerUnderOneBucket()
    {
        // Arrange
        var limits = Limits(tokenCapacity: 2, tokensPerReplenishmentPeriod: 2);
        using var limiter = CallerLimiter(TransportSurface.Admin, limits);

        // Act
        var firstGuess = limiter.AttemptAcquire(AdminRequest());
        var secondGuess = limiter.AttemptAcquire(AdminRequest());
        var thirdGuess = limiter.AttemptAcquire(AdminRequest());

        // Assert
        Assert.True(firstGuess.IsAcquired);
        Assert.True(secondGuess.IsAcquired);
        Assert.False(thirdGuess.IsAcquired);

        DisposeAll([firstGuess, secondGuess, thirdGuess]);
    }

    [Fact]
    public void PartitionForCaller_ForAnAuthenticatedClient_KeepsItOutOfTheAnonymousPartition()
    {
        // Arrange
        var limits = Limits(tokenCapacity: 1, tokensPerReplenishmentPeriod: 1);
        using var limiter = CallerLimiter(TransportSurface.Mcp, limits);

        // Act
        var anonymous = limiter.AttemptAcquire(McpRequest());
        var authenticated = limiter.AttemptAcquire(McpRequest("desktop-agent"));

        // Assert
        Assert.True(anonymous.IsAcquired);
        Assert.True(authenticated.IsAcquired);

        DisposeAll([anonymous, authenticated]);
    }

    /// <summary>
    /// Under <c>None</c> a client certificate is the only identity there is, so a deployment that trusts several client
    /// applications keeps a bucket per application instead of pooling them all into the anonymous one.
    /// </summary>
    [Fact]
    public void PartitionForCaller_WithACertificateProfileAndNoCredential_KeepsEachClientApplicationApart()
    {
        // Arrange
        var limits = Limits(tokenCapacity: 1, tokensPerReplenishmentPeriod: 1);
        using var limiter = CallerLimiter(TransportSurface.Mcp, limits);

        // Act
        var chatgpt = limiter.AttemptAcquire(McpRequest(certificateProfileName: "chatgpt-connector"));
        var workstation = limiter.AttemptAcquire(McpRequest(certificateProfileName: "workstation-connector"));
        var chatgptAgain = limiter.AttemptAcquire(McpRequest(certificateProfileName: "chatgpt-connector"));
        var unidentified = limiter.AttemptAcquire(McpRequest());

        // Assert
        Assert.True(chatgpt.IsAcquired);
        Assert.True(workstation.IsAcquired);
        Assert.False(chatgptAgain.IsAcquired);
        Assert.True(unidentified.IsAcquired);

        DisposeAll([chatgpt, workstation, chatgptAgain, unidentified]);
    }

    /// <summary>
    /// A key names one client of this deployment and a profile names a client application several keys may sit behind,
    /// so the key decides. Were the two combined, one credential would earn a fresh bucket for every certificate it
    /// could present under — capacity bought by holding one more certificate.
    /// </summary>
    [Fact]
    public void PartitionForCaller_WithBothIdentities_SpendsTheAuthenticatedClientsCapacityOnly()
    {
        // Arrange
        var limits = Limits(tokenCapacity: 1, tokensPerReplenishmentPeriod: 1);
        using var limiter = CallerLimiter(TransportSurface.Mcp, limits);

        // Act
        var underOneProfile = limiter.AttemptAcquire(
            McpRequest("desktop-agent", certificateProfileName: "chatgpt-connector"));
        var underAnotherProfile = limiter.AttemptAcquire(
            McpRequest("desktop-agent", certificateProfileName: "workstation-connector"));

        // Assert
        Assert.True(underOneProfile.IsAcquired);
        Assert.False(underAnotherProfile.IsAcquired);

        DisposeAll([underOneProfile, underAnotherProfile]);
    }

    [Fact]
    public async Task RefuseAsync_ForAnyRejection_AnswersTooManyRequestsWithoutABody()
    {
        // Arrange
        var boundedSurfaces = McpOnly(Limits(maxConcurrentRequests: 1));
        using var limiter = ProcessLimiter(boundedSurfaces);
        using var held = limiter.AttemptAcquire(McpRequest());
        using var refusedLease = limiter.AttemptAcquire(McpRequest());
        var httpContext = McpRequest();

        // Act
        await TransportRateLimiting.RefuseAsync(
            new OnRejectedContext { HttpContext = httpContext, Lease = refusedLease },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Status429TooManyRequests, httpContext.Response.StatusCode);
        Assert.Equal("no-store", httpContext.Response.Headers.CacheControl);
        Assert.Equal(0, httpContext.Response.Body.Length);
    }

    [Fact]
    public async Task RefuseAsync_WhenTheLimiterKnowsWhenCapacityReturns_AdvertisesTheRetry()
    {
        // Arrange
        var limits = Limits(
            tokenCapacity: 1,
            tokensPerReplenishmentPeriod: 1,
            replenishmentPeriod: TimeSpan.FromSeconds(30));
        using var limiter = CallerLimiter(TransportSurface.Mcp, limits);
        using var spent = limiter.AttemptAcquire(McpRequest("desktop-agent"));
        using var refusedLease = limiter.AttemptAcquire(McpRequest("desktop-agent"));
        var httpContext = McpRequest();

        // Act
        await TransportRateLimiting.RefuseAsync(
            new OnRejectedContext { HttpContext = httpContext, Lease = refusedLease },
            CancellationToken.None);

        // Assert
        Assert.False(refusedLease.IsAcquired);
        Assert.Equal("30", httpContext.Response.Headers.RetryAfter);
    }

    [Fact]
    public async Task RefuseAsync_WhenTheLimiterCannotSayWhenCapacityReturns_OmitsTheRetry()
    {
        // Arrange
        var boundedSurfaces = McpOnly(Limits(maxConcurrentRequests: 1));
        using var limiter = ProcessLimiter(boundedSurfaces);
        using var held = limiter.AttemptAcquire(McpRequest());
        using var refusedLease = limiter.AttemptAcquire(McpRequest());
        var httpContext = McpRequest();

        // Act
        await TransportRateLimiting.RefuseAsync(
            new OnRejectedContext { HttpContext = httpContext, Lease = refusedLease },
            CancellationToken.None);

        // Assert
        // A concurrency limit has no scheduled moment at which a slot frees, so a refusal for concurrency carries no
        // guess about when to come back.
        Assert.False(refusedLease.IsAcquired);
        Assert.True(StringValues.IsNullOrEmpty(httpContext.Response.Headers.RetryAfter));
    }

    [Fact]
    public async Task RefuseAsync_ForDifferentClients_AnswersIdentically()
    {
        // Arrange
        var limits = Limits(tokenCapacity: 1, tokensPerReplenishmentPeriod: 1);
        using var limiter = CallerLimiter(TransportSurface.Mcp, limits);
        using var spentByFirst = limiter.AttemptAcquire(McpRequest("desktop-agent"));
        using var refusedFirst = limiter.AttemptAcquire(McpRequest("desktop-agent"));
        using var spentBySecond = limiter.AttemptAcquire(McpRequest("nightly-indexer"));
        using var refusedSecond = limiter.AttemptAcquire(McpRequest("nightly-indexer"));

        var firstResponse = McpRequest("desktop-agent");
        var secondResponse = McpRequest("nightly-indexer");

        // Act
        await TransportRateLimiting.RefuseAsync(
            new OnRejectedContext { HttpContext = firstResponse, Lease = refusedFirst },
            CancellationToken.None);
        await TransportRateLimiting.RefuseAsync(
            new OnRejectedContext { HttpContext = secondResponse, Lease = refusedSecond },
            CancellationToken.None);

        // Assert
        // A refusal describes the deployment to whoever provoked it, so it must not describe one client's limits to
        // another, nor differ in a way that says whether a named credential exists.
        Assert.Equal(firstResponse.Response.StatusCode, secondResponse.Response.StatusCode);
        Assert.Equal(
            firstResponse.Response.Headers.OrderBy(header => header.Key, StringComparer.Ordinal),
            secondResponse.Response.Headers.OrderBy(header => header.Key, StringComparer.Ordinal));
    }

    /// <summary>A refusal must not tell an administrative caller anything an MCP caller is not told, or the other way about.</summary>
    [Fact]
    public async Task RefuseAsync_ForDifferentSurfaces_AnswersIdentically()
    {
        // Arrange
        var limits = Limits(tokenCapacity: 1, tokensPerReplenishmentPeriod: 1);
        using var limiter = CallerLimiterForBothSurfaces(limits);
        using var spentOnMcp = limiter.AttemptAcquire(McpRequest());
        using var refusedOnMcp = limiter.AttemptAcquire(McpRequest());
        using var spentOnAdmin = limiter.AttemptAcquire(AdminRequest());
        using var refusedOnAdmin = limiter.AttemptAcquire(AdminRequest());

        var mcpResponse = McpRequest();
        var adminResponse = AdminRequest();

        // Act
        await TransportRateLimiting.RefuseAsync(
            new OnRejectedContext { HttpContext = mcpResponse, Lease = refusedOnMcp },
            CancellationToken.None);
        await TransportRateLimiting.RefuseAsync(
            new OnRejectedContext { HttpContext = adminResponse, Lease = refusedOnAdmin },
            CancellationToken.None);

        // Assert
        Assert.Equal(mcpResponse.Response.StatusCode, adminResponse.Response.StatusCode);
        Assert.Equal(
            mcpResponse.Response.Headers.OrderBy(header => header.Key, StringComparer.Ordinal),
            adminResponse.Response.Headers.OrderBy(header => header.Key, StringComparer.Ordinal));
    }

    [Fact]
    public async Task PartitionForProcess_UnderConcurrentLoad_AdmitsTheLimitAndRefusesTheRest()
    {
        // Arrange
        const int PermitLimit = 4;
        const int Attempts = 40;

        var boundedSurfaces = McpOnly(Limits(maxConcurrentRequests: PermitLimit));
        using var limiter = ProcessLimiter(boundedSurfaces);

        var everyAttemptSettled = new TaskCompletionSource();
        var attemptsSettled = 0;
        var refusedCount = 0;
        var concurrency = 0;
        var peakConcurrency = 0;

        async Task AttemptAsync()
        {
            using var lease = await limiter.AcquireAsync(
                McpRequest(),
                permitCount: 1,
                TestContext.Current.CancellationToken);

            if (lease.IsAcquired)
            {
                RaiseTo(ref peakConcurrency, Interlocked.Increment(ref concurrency));
            }
            else
            {
                Interlocked.Increment(ref refusedCount);
            }

            // Nothing is released until every attempt has been made, so the counts below describe what the limit
            // admitted rather than what the scheduler happened to interleave.
            if (Interlocked.Increment(ref attemptsSettled) == Attempts)
            {
                everyAttemptSettled.TrySetResult();
            }

            await everyAttemptSettled.Task;

            if (lease.IsAcquired)
            {
                Interlocked.Decrement(ref concurrency);
            }
        }

        // Act
        await Task.WhenAll(Enumerable.Range(0, Attempts).Select(_ => AttemptAsync()));

        // Assert
        Assert.Equal(PermitLimit, peakConcurrency);
        Assert.Equal(Attempts - PermitLimit, refusedCount);

        // A compliant client is not starved by the load that was refused: the permits came back as the leases were
        // released, so the next request is served rather than paying for the burst that preceded it.
        using var afterTheLoad = limiter.AttemptAcquire(McpRequest());
        Assert.True(afterTheLoad.IsAcquired);
    }

    private static TransportRateLimits Limits(
        int maxConcurrentRequests = 8,
        int concurrencyQueueLimit = 0,
        int tokenCapacity = 100,
        int tokensPerReplenishmentPeriod = 100,
        TimeSpan? replenishmentPeriod = null,
        int requestQueueLimit = 0) =>
        TransportRateLimits.Create(
            maxConcurrentRequests,
            concurrencyQueueLimit,
            tokenCapacity,
            tokensPerReplenishmentPeriod,
            replenishmentPeriod ?? TimeSpan.FromMinutes(1),
            requestQueueLimit);

    private static BoundedTransportSurface[] McpOnly(TransportRateLimits limits) =>
        [new BoundedTransportSurface(TransportSurface.Mcp, limits)];

    private static BoundedTransportSurface[] AdminOnly(TransportRateLimits limits) =>
        [new BoundedTransportSurface(TransportSurface.Admin, limits)];

    private static BoundedTransportSurface[] BothSurfaces(
        TransportRateLimits mcpLimits,
        TransportRateLimits adminLimits) =>
        [
            new BoundedTransportSurface(TransportSurface.Mcp, mcpLimits),
            new BoundedTransportSurface(TransportSurface.Admin, adminLimits),
        ];

    private static PartitionedRateLimiter<HttpContext> ProcessLimiter(
        IReadOnlyList<BoundedTransportSurface> boundedSurfaces) =>
        PartitionedRateLimiter.Create<HttpContext, string>(
            httpContext => TransportRateLimiting.PartitionForProcess(httpContext, boundedSurfaces));

    /// <summary>
    /// Builds one surface's own partitions, with the replenishment timer left off so a test that spends capacity is not
    /// racing it.
    /// </summary>
    /// <remarks>
    /// The partition key is the one <see cref="TransportRateLimiting.PartitionForCaller" /> computed, because which
    /// caller a request is counted under is what these tests are about, and the bucket is the one
    /// <see cref="TransportRateLimiting.CallerBucketOptions" /> described apart from
    /// <see cref="TokenBucketRateLimiterOptions.AutoReplenishment" />. That an endpoint leaves it on is asserted against
    /// those options directly, which is the only place it can be asserted without waiting for a clock.
    /// </remarks>
    private static PartitionedRateLimiter<HttpContext> CallerLimiter(
        TransportSurface surface,
        TransportRateLimits limits)
    {
        var boundedSurface = new BoundedTransportSurface(surface, limits);

        return PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            RateLimitPartition.GetTokenBucketLimiter(
                TransportRateLimiting.PartitionForCaller(httpContext, boundedSurface).PartitionKey,
                _ => WithoutTheReplenishmentTimer(TransportRateLimiting.CallerBucketOptions(limits))));
    }

    /// <summary>
    /// Builds both surfaces' partitions into one limiter, which is stricter than production: each endpoint's policy is
    /// its own limiter with its own dictionary, so a key shared between them would collide here and cannot there. What
    /// this proves is that the keys themselves are distinct, which is what makes the isolation independent of the
    /// framework keeping two policies apart.
    /// </summary>
    private static PartitionedRateLimiter<HttpContext> CallerLimiterForBothSurfaces(TransportRateLimits limits) =>
        PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            RateLimitPartition.GetTokenBucketLimiter(
                TransportRateLimiting.PartitionForCaller(httpContext, SurfaceOf(httpContext, limits)).PartitionKey,
                _ => WithoutTheReplenishmentTimer(TransportRateLimiting.CallerBucketOptions(limits))));

    private static BoundedTransportSurface SurfaceOf(HttpContext httpContext, TransportRateLimits limits) =>
        httpContext.Request.Path.StartsWithSegments(AdminEndpointOptions.RoutePrefix)
            ? new BoundedTransportSurface(TransportSurface.Admin, limits)
            : new BoundedTransportSurface(TransportSurface.Mcp, limits);

    private static TokenBucketRateLimiterOptions WithoutTheReplenishmentTimer(TokenBucketRateLimiterOptions options) =>
        new()
        {
            AutoReplenishment = false,
            TokenLimit = options.TokenLimit,
            TokensPerPeriod = options.TokensPerPeriod,
            ReplenishmentPeriod = options.ReplenishmentPeriod,
            QueueLimit = options.QueueLimit,
            QueueProcessingOrder = options.QueueProcessingOrder,
        };

    private static DefaultHttpContext McpRequest(
        string? authenticatedClientName = null,
        string? certificateProfileName = null) =>
        RequestTo(McpEndpointRoute.Path, authenticatedClientName, certificateProfileName);

    private static DefaultHttpContext AdminRequest(string? authenticatedClientName = null) =>
        RequestTo(AdminRoute, authenticatedClientName);

    private static DefaultHttpContext RequestTo(
        string path,
        string? authenticatedClientName = null,
        string? certificateProfileName = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = path;

        if (authenticatedClientName is not null)
        {
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, authenticatedClientName)],
                authenticationType: "test"));
        }

        if (certificateProfileName is not null)
        {
            // The same feature the client-certificate middleware publishes, so the limiter reads the identity it will
            // actually be handed rather than one shaped for the test.
            httpContext.Features.Set(new McpClientCertificateIdentity(certificateProfileName));
        }

        return httpContext;
    }

    private static RateLimitLease[] AcquireAll(
        PartitionedRateLimiter<HttpContext> limiter,
        Func<HttpContext> request,
        int attempts) =>
        [.. Enumerable.Range(0, attempts).Select(_ => limiter.AttemptAcquire(request()))];

    private static void DisposeAll(IEnumerable<RateLimitLease> leases)
    {
        foreach (var lease in leases)
        {
            lease.Dispose();
        }
    }

    /// <summary>Raises a shared maximum to the observed value without losing a concurrent raise.</summary>
    private static void RaiseTo(ref int target, int candidate)
    {
        var observed = Volatile.Read(ref target);

        while (candidate > observed)
        {
            var previous = Interlocked.CompareExchange(ref target, candidate, observed);

            if (previous == observed)
            {
                return;
            }

            observed = previous;
        }
    }
}
