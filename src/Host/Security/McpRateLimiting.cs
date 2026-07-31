// Copyright © 2026 Krzysztof Kasprowicz

using System.Globalization;
using System.Threading.RateLimiting;
using MailMcp.Infrastructure.Security;
using MailMcp.Mcp;
using Microsoft.AspNetCore.RateLimiting;

namespace MailMcp.Host.Security;

/// <summary>Bounds what the MCP endpoint accepts, per process and per client.</summary>
/// <remarks>
/// <para>
/// The two controls are attached to different parts of the framework, and the reason is a limitation worth stating
/// rather than a preference. A named policy resolves to exactly one limiter, so two controls cannot share one; the
/// per-client bucket takes the policy, because it is the one that belongs to this endpoint and the one whose name a
/// metric should carry, and the process-wide concurrency limit rides on the global limiter, which is the only other
/// place a limiter can be attached. Being global, it then has to exclude everything that is not this endpoint itself,
/// which it does on the published route — the same test the origin check applies, for the same reason: readiness and
/// liveness must keep answering while the endpoint is refusing.
/// </para>
/// <para>
/// The global limiter is acquired first and the policy second, so a request that is out of client capacity has briefly
/// held a concurrency slot before being refused. The slot is released on rejection and the default queue limits are
/// zero, so nothing waits and nothing is held; a deployment that configures a concurrency queue should know that a
/// request can wait in it and then still be refused for its own rate.
/// </para>
/// </remarks>
internal static class McpRateLimiting
{
    /// <summary>The rate-limiting policy the MCP endpoint requires, named so the endpoint asks for this one and a metric can report it.</summary>
    internal const string PolicyName = "MailMcpEndpoint";

    /// <summary>The one partition every MCP request shares for the process-wide concurrency limit.</summary>
    private const string ProcessPartitionKey = "mcp";

    /// <summary>The partition standing for everything the process-wide limit does not apply to.</summary>
    private const string UnlimitedPartitionKey = "unlimited";

    /// <summary>The shortest retry a refusal asks for, so a client that is told to retry never reads it as "immediately".</summary>
    private static readonly TimeSpan ShortestAdvertisedRetry = TimeSpan.FromSeconds(1);

    /// <summary>Adds the limiters the endpoint runs under and the refusal they share.</summary>
    /// <param name="services">The container to add to.</param>
    /// <param name="limits">The limits composition read.</param>
    /// <returns>The container, so composition reads as one sequence.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> or <paramref name="limits" /> is <see langword="null" />.</exception>
    internal static IServiceCollection AddMcpRateLimiting(this IServiceCollection services, McpRateLimits limits)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(limits);

        services.AddRateLimiter(rateLimiterOptions =>
        {
            rateLimiterOptions.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                httpContext => PartitionForProcess(httpContext, limits));

            rateLimiterOptions.AddPolicy(PolicyName, httpContext => PartitionForClient(httpContext, limits));

            rateLimiterOptions.OnRejected = RefuseAsync;
        });

        return services;
    }

    /// <summary>Names the process-wide concurrency partition an MCP request is served under.</summary>
    /// <param name="httpContext">The request being admitted.</param>
    /// <param name="limits">The limits the endpoint runs under.</param>
    /// <returns>The one shared concurrency partition for an MCP request, and an unlimited partition for anything else.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="httpContext" /> or <paramref name="limits" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// One partition for every client rather than one each, because what it bounds is the process: the connections, the
    /// threads, and the response streams a machine has one set of. Splitting it per client would let the total grow with
    /// the number of clients, which is the opposite of what a concurrency limit is for.
    /// </remarks>
    internal static RateLimitPartition<string> PartitionForProcess(HttpContext httpContext, McpRateLimits limits)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(limits);

        if (!httpContext.Request.Path.StartsWithSegments(McpEndpointRoute.Path))
        {
            return RateLimitPartition.GetNoLimiter(UnlimitedPartitionKey);
        }

        return RateLimitPartition.GetConcurrencyLimiter(ProcessPartitionKey, _ => ProcessConcurrencyOptions(limits));
    }

    /// <summary>Describes the process-wide concurrency limiter the endpoint runs under.</summary>
    /// <param name="limits">The limits the endpoint runs under.</param>
    /// <returns>The limiter description.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="limits" /> is <see langword="null" />.</exception>
    /// <remarks>Oldest first, so a queue that is configured drains in the order requests arrived rather than rewarding whichever client retried most recently.</remarks>
    internal static ConcurrencyLimiterOptions ProcessConcurrencyOptions(McpRateLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        return new ConcurrencyLimiterOptions
        {
            PermitLimit = limits.MaxConcurrentRequests,
            QueueLimit = limits.ConcurrencyQueueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        };
    }

    /// <summary>Names the per-client partition a request spends capacity from.</summary>
    /// <param name="httpContext">The request being admitted, after authentication has run.</param>
    /// <param name="limits">The limits the endpoint runs under.</param>
    /// <returns>The token bucket kept for the client this request authenticated as, or the shared anonymous one.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="httpContext" /> or <paramref name="limits" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// It reads the identity rather than the request, so it must run behind authentication; the composition root places
    /// it there. A request whose credential was refused has no authenticated identity at this point and is counted under
    /// the anonymous partition, which is what makes a flood of bad credentials cost the sender something.
    /// </remarks>
    internal static RateLimitPartition<string> PartitionForClient(HttpContext httpContext, McpRateLimits limits)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(limits);

        return RateLimitPartition.GetTokenBucketLimiter(
            McpRateLimitPartitions.KeyFor(AuthenticatedClientName(httpContext)),
            _ => ClientBucketOptions(limits));
    }

    /// <summary>Describes the token bucket kept for one client.</summary>
    /// <param name="limits">The limits the endpoint runs under.</param>
    /// <returns>The limiter description.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="limits" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Replenishment is automatic, which is what makes a client's capacity return without anything in this process
    /// having to remember to restore it. The framework's own timer owns that schedule; what is decided here is how much
    /// comes back and how often, and how much a client may hold at once.
    /// </remarks>
    internal static TokenBucketRateLimiterOptions ClientBucketOptions(McpRateLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        return new TokenBucketRateLimiterOptions
        {
            AutoReplenishment = true,
            TokenLimit = limits.TokenCapacity,
            TokensPerPeriod = limits.TokensPerReplenishmentPeriod,
            ReplenishmentPeriod = limits.ReplenishmentPeriod,
            QueueLimit = limits.RequestQueueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        };
    }

    /// <summary>Refuses a request that is over either limit.</summary>
    /// <param name="context">The rejection the limiter reported.</param>
    /// <param name="cancellationToken">Unused; the refusal writes no body and so cannot be interrupted part way.</param>
    /// <returns>A completed task.</returns>
    /// <remarks>
    /// <para>
    /// The response is a bare <c>429</c>. A body would have to say either nothing useful or something about the limits,
    /// and the second is a description of the deployment that every refused caller would receive — including one whose
    /// credential was refused a moment later, which must not learn that its name exists. What a client needs is the
    /// status code and, where the limiter can compute one, how long to wait.
    /// </para>
    /// <para>
    /// Only the token bucket can compute that; a concurrency limit has no scheduled moment at which a slot frees, so a
    /// refusal for concurrency carries no <c>Retry-After</c> rather than a guess. The advertised value is never below a
    /// second, because a client reading <c>Retry-After: 0</c> would retry into the same refusal without pausing.
    /// </para>
    /// </remarks>
    internal static ValueTask RefuseAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var response = context.HttpContext.Response;

        response.StatusCode = StatusCodes.Status429TooManyRequests;
        response.Headers.CacheControl = "no-store";

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            var advertisedRetry = retryAfter < ShortestAdvertisedRetry ? ShortestAdvertisedRetry : retryAfter;

            response.Headers.RetryAfter = ((int)Math.Ceiling(advertisedRetry.TotalSeconds))
                .ToString(CultureInfo.InvariantCulture);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Reads the name of the credential a request authenticated with.</summary>
    /// <remarks>
    /// An unauthenticated principal is what both an endpoint running without authentication and a request whose
    /// credential was refused leave behind, and the two are deliberately indistinguishable here: the partition is a
    /// question about capacity, not about why the identity is absent.
    /// </remarks>
    private static string? AuthenticatedClientName(HttpContext httpContext) =>
        httpContext.User.Identity is { IsAuthenticated: true } identity ? identity.Name : null;
}
