// Copyright © 2026 Krzysztof Kasprowicz

using System.Security.Claims;
using System.Threading.RateLimiting;
using MailMcp.Host.Security;
using MailMcp.Infrastructure.Security;
using MailMcp.Mcp;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace MailMcp.Host.UnitTests;

/// <summary>Covers the limits the MCP endpoint admits traffic under.</summary>
/// <remarks>
/// Nothing here observes a clock. The configured replenishment period is a minute throughout, so every test finishes
/// inside one period and no capacity returns part way through an assertion; what a replenishment restores is asserted
/// against the limiter description the endpoint is built from rather than by waiting for one, because
/// <c>TokenBucketRateLimiter.TryReplenish</c> is itself gated on elapsed wall-clock time and a test that drove it would
/// be a test of the clock. Restoring capacity on that schedule is the framework's own contract; what belongs here is
/// that the schedule reaching it is the one an operator configured, and that automatic replenishment is on so nothing
/// in this process has to pump it.
/// </remarks>
public sealed class McpRateLimitingTests
{
    [Fact]
    public void ProcessConcurrencyOptions_CarriesTheConfiguredConcurrency()
    {
        // Arrange
        var limits = Limits(maxConcurrentRequests: 9, concurrencyQueueLimit: 4);

        // Act
        var limiterOptions = McpRateLimiting.ProcessConcurrencyOptions(limits);

        // Assert
        Assert.Equal(9, limiterOptions.PermitLimit);
        Assert.Equal(4, limiterOptions.QueueLimit);
        Assert.Equal(QueueProcessingOrder.OldestFirst, limiterOptions.QueueProcessingOrder);
    }

    [Fact]
    public void ClientBucketOptions_CarriesTheConfiguredBurstAndSchedule()
    {
        // Arrange
        var limits = Limits(
            tokenCapacity: 30,
            tokensPerReplenishmentPeriod: 5,
            replenishmentPeriod: TimeSpan.FromSeconds(20),
            requestQueueLimit: 2);

        // Act
        var limiterOptions = McpRateLimiting.ClientBucketOptions(limits);

        // Assert
        Assert.Equal(30, limiterOptions.TokenLimit);
        Assert.Equal(5, limiterOptions.TokensPerPeriod);
        Assert.Equal(TimeSpan.FromSeconds(20), limiterOptions.ReplenishmentPeriod);
        Assert.Equal(2, limiterOptions.QueueLimit);
        Assert.Equal(QueueProcessingOrder.OldestFirst, limiterOptions.QueueProcessingOrder);
    }

    [Fact]
    public void ClientBucketOptions_RestoresCapacityWithoutBeingPumped()
    {
        // Arrange
        var limits = Limits();

        // Act
        var limiterOptions = McpRateLimiting.ClientBucketOptions(limits);

        // Assert
        // Automatic replenishment is what makes a client's capacity return on the framework's own timer. With it off,
        // something in this process would have to call TryReplenish, and nothing does, so a spent bucket would stay
        // spent for the life of the process.
        Assert.True(limiterOptions.AutoReplenishment);
    }

    [Fact]
    public void PartitionForProcess_ForARequestOutsideTheEndpoint_AppliesNoLimit()
    {
        // Arrange
        var limits = Limits(maxConcurrentRequests: 1);
        using var limiter = ProcessLimiter(limits);

        // Act
        var leases = AcquireAll(limiter, () => RequestTo("/health"), attempts: 5);

        // Assert
        // Readiness and liveness have to keep answering while the endpoint is refusing, so the process-wide limiter
        // excludes every route that is not the MCP endpoint rather than counting them against the same permits.
        Assert.All(leases, lease => Assert.True(lease.IsAcquired));

        DisposeAll(leases);
    }

    [Fact]
    public void PartitionForProcess_ForMcpRequests_AdmitsExactlyTheConfiguredConcurrency()
    {
        // Arrange
        var limits = Limits(maxConcurrentRequests: 3);
        using var limiter = ProcessLimiter(limits);

        // Act
        var leases = AcquireAll(limiter, () => McpRequest(), attempts: 4);

        // Assert
        Assert.Equal([true, true, true, false], leases.Select(lease => lease.IsAcquired));

        DisposeAll(leases);
    }

    [Fact]
    public void PartitionForProcess_ForSeveralClients_CountsThemAgainstOneLimit()
    {
        // Arrange
        var limits = Limits(maxConcurrentRequests: 2);
        using var limiter = ProcessLimiter(limits);

        // Act
        var first = limiter.AttemptAcquire(McpRequest("desktop-agent"));
        var second = limiter.AttemptAcquire(McpRequest("nightly-indexer"));
        var third = limiter.AttemptAcquire(McpRequest("third-client"));

        // Assert
        // The concurrency limit bounds the process, which has one set of connections and threads however many clients
        // there are; splitting it per client would let the total grow with the client list.
        Assert.True(first.IsAcquired);
        Assert.True(second.IsAcquired);
        Assert.False(third.IsAcquired);

        DisposeAll([first, second, third]);
    }

    [Fact]
    public async Task PartitionForProcess_WithNoQueue_RefusesRatherThanWaiting()
    {
        // Arrange
        var limits = Limits(maxConcurrentRequests: 1, concurrencyQueueLimit: 0);
        using var limiter = ProcessLimiter(limits);
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
        var limits = Limits(maxConcurrentRequests: 1, concurrencyQueueLimit: 1);
        using var limiter = ProcessLimiter(limits);
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
    public void PartitionForClient_ExhaustingOneClientsBurst_LeavesAnotherClientUntouched()
    {
        // Arrange
        var limits = Limits(tokenCapacity: 2, tokensPerReplenishmentPeriod: 2);
        using var limiter = ClientLimiter(limits);

        // Act
        var spent = AcquireAll(limiter, () => McpRequest("desktop-agent"), attempts: 3);
        var otherClient = limiter.AttemptAcquire(McpRequest("nightly-indexer"));

        // Assert
        Assert.Equal([true, true, false], spent.Select(lease => lease.IsAcquired));
        Assert.True(otherClient.IsAcquired);

        DisposeAll([.. spent, otherClient]);
    }

    [Fact]
    public void PartitionForClient_WithoutAnAuthenticatedIdentity_SharesOneAnonymousPartition()
    {
        // Arrange
        var limits = Limits(tokenCapacity: 2, tokensPerReplenishmentPeriod: 2);
        using var limiter = ClientLimiter(limits);

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

    [Fact]
    public void PartitionForClient_ForAnAuthenticatedClient_KeepsItOutOfTheAnonymousPartition()
    {
        // Arrange
        var limits = Limits(tokenCapacity: 1, tokensPerReplenishmentPeriod: 1);
        using var limiter = ClientLimiter(limits);

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
    public void PartitionForClient_WithACertificateProfileAndNoCredential_KeepsEachClientApplicationApart()
    {
        // Arrange
        var limits = Limits(tokenCapacity: 1, tokensPerReplenishmentPeriod: 1);
        using var limiter = ClientLimiter(limits);

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
    public void PartitionForClient_WithBothIdentities_SpendsTheAuthenticatedClientsCapacityOnly()
    {
        // Arrange
        var limits = Limits(tokenCapacity: 1, tokensPerReplenishmentPeriod: 1);
        using var limiter = ClientLimiter(limits);

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
        var limits = Limits(maxConcurrentRequests: 1);
        using var limiter = ProcessLimiter(limits);
        using var held = limiter.AttemptAcquire(McpRequest());
        using var refusedLease = limiter.AttemptAcquire(McpRequest());
        var httpContext = McpRequest();

        // Act
        await McpRateLimiting.RefuseAsync(
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
        using var limiter = ClientLimiter(limits);
        using var spent = limiter.AttemptAcquire(McpRequest("desktop-agent"));
        using var refusedLease = limiter.AttemptAcquire(McpRequest("desktop-agent"));
        var httpContext = McpRequest();

        // Act
        await McpRateLimiting.RefuseAsync(
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
        var limits = Limits(maxConcurrentRequests: 1);
        using var limiter = ProcessLimiter(limits);
        using var held = limiter.AttemptAcquire(McpRequest());
        using var refusedLease = limiter.AttemptAcquire(McpRequest());
        var httpContext = McpRequest();

        // Act
        await McpRateLimiting.RefuseAsync(
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
        using var limiter = ClientLimiter(limits);
        using var spentByFirst = limiter.AttemptAcquire(McpRequest("desktop-agent"));
        using var refusedFirst = limiter.AttemptAcquire(McpRequest("desktop-agent"));
        using var spentBySecond = limiter.AttemptAcquire(McpRequest("nightly-indexer"));
        using var refusedSecond = limiter.AttemptAcquire(McpRequest("nightly-indexer"));

        var firstResponse = McpRequest("desktop-agent");
        var secondResponse = McpRequest("nightly-indexer");

        // Act
        await McpRateLimiting.RefuseAsync(
            new OnRejectedContext { HttpContext = firstResponse, Lease = refusedFirst },
            CancellationToken.None);
        await McpRateLimiting.RefuseAsync(
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

    [Fact]
    public async Task PartitionForProcess_UnderConcurrentLoad_AdmitsTheLimitAndRefusesTheRest()
    {
        // Arrange
        const int PermitLimit = 4;
        const int Attempts = 40;

        var limits = Limits(maxConcurrentRequests: PermitLimit);
        using var limiter = ProcessLimiter(limits);

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

    private static McpRateLimits Limits(
        int maxConcurrentRequests = 8,
        int concurrencyQueueLimit = 0,
        int tokenCapacity = 100,
        int tokensPerReplenishmentPeriod = 100,
        TimeSpan? replenishmentPeriod = null,
        int requestQueueLimit = 0) =>
        McpRateLimits.Create(
            maxConcurrentRequests,
            concurrencyQueueLimit,
            tokenCapacity,
            tokensPerReplenishmentPeriod,
            replenishmentPeriod ?? TimeSpan.FromMinutes(1),
            requestQueueLimit);

    private static PartitionedRateLimiter<HttpContext> ProcessLimiter(McpRateLimits limits) =>
        PartitionedRateLimiter.Create<HttpContext, string>(
            httpContext => McpRateLimiting.PartitionForProcess(httpContext, limits));

    private static PartitionedRateLimiter<HttpContext> ClientLimiter(McpRateLimits limits) =>
        PartitionedRateLimiter.Create<HttpContext, string>(
            httpContext => McpRateLimiting.PartitionForClient(httpContext, limits));

    private static DefaultHttpContext McpRequest(
        string? authenticatedClientName = null,
        string? certificateProfileName = null)
    {
        var httpContext = RequestTo(McpEndpointRoute.Path);

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

    private static DefaultHttpContext RequestTo(string path)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = path;

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
