// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Threading.RateLimiting;
using MailFathom.Host.Security.Mcp;
using MailFathom.Infrastructure.Security.Transport;
using Microsoft.AspNetCore.RateLimiting;

namespace MailFathom.Host.Security.Transport;

/// <summary>Bounds what a transport surface accepts, per process and per caller.</summary>
/// <remarks>
/// <para>
/// The two controls are attached to different parts of the framework, and the reason is a limitation worth stating
/// rather than a preference. A named policy resolves to exactly one limiter, so two controls cannot share one; the
/// per-caller bucket takes the policy, because it is the one that belongs to a single surface and the one whose name a
/// metric should carry, and the process-wide concurrency limit rides on the global limiter, which is the only other
/// place a limiter can be attached. Being global, it is also the one part shared by both surfaces: there is one of it in
/// the application, so it has to recognize which surface a request belongs to from the request itself, which it does on
/// the published route prefix. Everything matching no surface is left unlimited — the same test the origin check
/// applies, for the same reason: readiness and liveness must keep answering while an endpoint is refusing.
/// </para>
/// <para>
/// The global limiter is acquired first and the policy second, so a request that is out of caller capacity has briefly
/// held a concurrency slot before being refused. The slot is released on rejection and the default queue limits are
/// zero, so nothing waits and nothing is held; a deployment that configures a concurrency queue should know that a
/// request can wait in it and then still be refused for its own rate.
/// </para>
/// <para>
/// That order is also why a caller queue cannot be as large as the concurrency limit, which
/// <see cref="TransportRateLimits" /> refuses to construct: a request waiting for its caller's tokens keeps the
/// concurrency permit it took on the way in, and would otherwise let one caller out of capacity park every permit the
/// surface has until its next replenishment.
/// </para>
/// <para>
/// Which identity a request is counted under depends on what the pipeline has established by the time the limiter runs,
/// and the two surfaces differ there. The MCP endpoint authenticates ahead of the limiter, so its callers are counted
/// per configured key or per client-certificate profile. The administrative endpoint carries no authentication
/// middleware of its own — its credential is judged by the authorization middleware, which runs behind the limiter so
/// that a request about to be refused for its credential has still spent capacity — so every administrative caller
/// shares that surface's anonymous partition. That is the stronger bound for the threat the limit exists against, an
/// attacker guessing keys, and it is why the administrative burst is the surface's rather than one caller's.
/// </para>
/// </remarks>
internal static class TransportRateLimiting
{
    /// <summary>The partition standing for everything no surface's process-wide limit applies to.</summary>
    private const string UnlimitedPartitionKey = "unlimited";

    /// <summary>The shortest retry a refusal asks for, so a client that is told to retry never reads it as "immediately".</summary>
    private static readonly TimeSpan ShortestAdvertisedRetry = TimeSpan.FromSeconds(1);

    /// <summary>Adds the limiters every bounded surface runs under and the refusal they share.</summary>
    /// <param name="services">The container to add to.</param>
    /// <param name="boundedSurfaces">The surfaces being bounded and the limits each one runs under.</param>
    /// <returns>The container, so composition reads as one sequence.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> or <paramref name="boundedSurfaces" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="boundedSurfaces" /> is empty.</exception>
    /// <remarks>
    /// Called once with every bounded surface rather than once per surface, because the global limiter is a single
    /// property of one options object: a second call would replace the first surface's process-wide limit rather than
    /// adding to it, and the loss would be silent.
    /// </remarks>
    internal static IServiceCollection AddTransportRateLimiting(
        this IServiceCollection services,
        IReadOnlyList<BoundedTransportSurface> boundedSurfaces)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(boundedSurfaces);

        if (boundedSurfaces.Count == 0)
        {
            throw new ArgumentException("At least one surface is required to register a limiter.", nameof(boundedSurfaces));
        }

        services.AddRateLimiter(rateLimiterOptions =>
        {
            rateLimiterOptions.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                httpContext => PartitionForProcess(httpContext, boundedSurfaces));

            foreach (var boundedSurface in boundedSurfaces)
            {
                rateLimiterOptions.AddPolicy(
                    boundedSurface.Surface.RateLimitingPolicyName,
                    httpContext => PartitionForCaller(httpContext, boundedSurface));
            }

            rateLimiterOptions.OnRejected = RefuseAsync;
        });

        return services;
    }

    /// <summary>Names the process-wide concurrency partition a request is served under.</summary>
    /// <param name="httpContext">The request being admitted.</param>
    /// <param name="boundedSurfaces">The surfaces being bounded and the limits each one runs under.</param>
    /// <returns>The one shared concurrency partition of the surface the request arrived on, and an unlimited partition for anything else.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="httpContext" /> or <paramref name="boundedSurfaces" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// One partition per surface rather than one per caller, because what it bounds is the process: the connections, the
    /// threads, and the response streams a machine has one set of. Splitting it per caller would let the total grow with
    /// the number of callers, which is the opposite of what a concurrency limit is for. It is still per surface rather
    /// than one for the whole process, because two endpoints sharing a permit count would let traffic to one refuse
    /// traffic to the other.
    /// </remarks>
    internal static RateLimitPartition<string> PartitionForProcess(
        HttpContext httpContext,
        IReadOnlyList<BoundedTransportSurface> boundedSurfaces)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(boundedSurfaces);

        var boundedSurface = boundedSurfaces.FirstOrDefault(
            candidate => candidate.Surface.Serves(httpContext.Request.Path));

        return boundedSurface is null
            ? RateLimitPartition.GetNoLimiter(UnlimitedPartitionKey)
            : RateLimitPartition.GetConcurrencyLimiter(
                boundedSurface.Surface.Name,
                _ => ProcessConcurrencyOptions(boundedSurface.Limits));
    }

    /// <summary>Describes the process-wide concurrency limiter a surface runs under.</summary>
    /// <param name="limits">The limits the surface runs under.</param>
    /// <returns>The limiter description.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="limits" /> is <see langword="null" />.</exception>
    /// <remarks>Oldest first, so a queue that is configured drains in the order requests arrived rather than rewarding whichever caller retried most recently.</remarks>
    internal static ConcurrencyLimiterOptions ProcessConcurrencyOptions(TransportRateLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        return new ConcurrencyLimiterOptions
        {
            PermitLimit = limits.MaxConcurrentRequests,
            QueueLimit = limits.ConcurrencyQueueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        };
    }

    /// <summary>Names the per-caller partition a request spends capacity from.</summary>
    /// <param name="httpContext">The request being admitted, after whatever authentication the surface runs ahead of the limiter.</param>
    /// <param name="boundedSurface">The surface being bounded and the limits it runs under.</param>
    /// <returns>The token bucket kept for the caller this request authenticated as, or the surface's shared anonymous one.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="httpContext" /> or <paramref name="boundedSurface" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// It reads the identity rather than the request, so it counts per caller only where the surface establishes one
    /// ahead of it; the composition root places the MCP endpoint's authentication there and states why the
    /// administrative endpoint has none to place. A request whose credential was refused has no authenticated identity
    /// at this point and is counted under the anonymous partition, which is what makes a flood of bad credentials cost
    /// the sender something.
    /// </para>
    /// <para>
    /// The certificate identity is read for every surface and set on one. Only the MCP endpoint asks a client for a
    /// certificate, so an administrative request never carries the feature and the lookup answers nothing — which is
    /// the shape to keep, because a surface that gained trust profiles would be counted per client application here
    /// without this method learning about it.
    /// </para>
    /// </remarks>
    internal static RateLimitPartition<string> PartitionForCaller(
        HttpContext httpContext,
        BoundedTransportSurface boundedSurface)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(boundedSurface);

        return RateLimitPartition.GetTokenBucketLimiter(
            TransportRateLimitPartitions.KeyFor(
                boundedSurface.Surface.Name,
                AuthenticatedClientName(httpContext),
                httpContext.Features.Get<McpClientCertificateIdentity>()?.ProfileName),
            _ => CallerBucketOptions(boundedSurface.Limits));
    }

    /// <summary>Describes the token bucket kept for one caller.</summary>
    /// <param name="limits">The limits the surface runs under.</param>
    /// <returns>The limiter description.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="limits" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Replenishment is automatic, which is what makes a caller's capacity return without anything in this process
    /// having to remember to restore it. The framework's own timer owns that schedule; what is decided here is how much
    /// comes back and how often, and how much a caller may hold at once.
    /// </remarks>
    internal static TokenBucketRateLimiterOptions CallerBucketOptions(TransportRateLimits limits)
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
    /// <para>
    /// One refusal for both surfaces, and identical on each. Which endpoint refused a request is something its caller
    /// already knows from the address it called, and saying anything more would describe one surface's configuration to
    /// whoever provoked a refusal on it.
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
    /// An unauthenticated principal is what a surface running without authentication, a surface that authenticates
    /// behind the limiter, and a request whose credential was refused all leave behind, and they are deliberately
    /// indistinguishable here: the partition is a question about capacity, not about why the identity is absent.
    /// </remarks>
    private static string? AuthenticatedClientName(HttpContext httpContext) =>
        httpContext.User.Identity is { IsAuthenticated: true } identity ? identity.Name : null;
}
