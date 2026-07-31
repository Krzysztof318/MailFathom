// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Diagnostics.CodeAnalysis;
using MailMcp.Host.Configuration;
using MailMcp.Mcp;
using Microsoft.Extensions.Options;

namespace MailMcp.Host.Hosting;

/// <summary>States at startup what bounds the MCP endpoint is serving under, or that it has none.</summary>
/// <remarks>
/// <para>
/// The limits are the one part of this section that applies whether or not an operator wrote it down, so they are also
/// the one part nobody would otherwise see. Reporting them makes a deployment running on defaults verifiable without
/// reading the source, and makes a deployment running on a number somebody mistyped visible at the moment it starts
/// rather than the first time a client is refused.
/// </para>
/// <para>
/// Turning the limits off is reported as a warning, because from that point the endpoint will serve whatever it is
/// asked for until something runs out. It is a defensible posture behind a proxy that already shapes the traffic, and
/// an accident everywhere else, and only the operator can tell those apart.
/// </para>
/// <para>
/// It reports what an operator configured and nothing about who is calling: no client name, no address, no origin. It
/// runs as a hosted service so it appears among the other startup diagnostics, and it is registered whether or not the
/// endpoint is enabled, because it is the report that decides whether it has anything to say.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class McpRateLimitingStartupReport : IHostedService
{
    private readonly McpEndpointOptions endpointSettings;
    private readonly ILogger<McpRateLimitingStartupReport> logger;

    /// <summary>Initializes a new startup report.</summary>
    /// <param name="endpointSettings">The endpoint settings startup was composed from.</param>
    /// <param name="logger">The startup logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpointSettings" /> is <see langword="null" />.</exception>
    public McpRateLimitingStartupReport(
        IOptions<McpEndpointOptions> endpointSettings,
        ILogger<McpRateLimitingStartupReport> logger)
    {
        ArgumentNullException.ThrowIfNull(endpointSettings);

        this.endpointSettings = endpointSettings.Value;
        this.logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!this.endpointSettings.Enabled)
        {
            return Task.CompletedTask;
        }

        var rateLimitingSettings = this.endpointSettings.RateLimiting;

        if (!rateLimitingSettings.Enabled)
        {
            this.LogEndpointServedWithoutRateLimits(McpEndpointRoute.Path);

            return Task.CompletedTask;
        }

        this.LogEndpointRateLimits(
            McpEndpointRoute.Path,
            rateLimitingSettings.MaxConcurrentRequests,
            rateLimitingSettings.ConcurrencyQueueLimit,
            rateLimitingSettings.TokenCapacity,
            rateLimitingSettings.TokensPerReplenishmentPeriod,
            rateLimitingSettings.ReplenishmentPeriod,
            rateLimitingSettings.RequestQueueLimit);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The MCP endpoint is enabled on {McpEndpointPath} with rate limiting turned off, so one client can hold "
            + "every database connection, response stream, and thread the process has until something runs out. This is "
            + "the right setting only where something in front of this process already bounds the traffic reaching it. "
            + "Remove McpEndpoint:RateLimiting:Enabled to run under the product defaults.")]
    private partial void LogEndpointServedWithoutRateLimits(string mcpEndpointPath);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The MCP endpoint on {McpEndpointPath} serves at most {MaxConcurrentRequests} requests at once across "
            + "every client, queueing {ConcurrencyQueueLimit} beyond that, and allows each client a burst of "
            + "{TokenCapacity} requests restored at {TokensPerReplenishmentPeriod} every {ReplenishmentPeriod}, queueing "
            + "{RequestQueueLimit} of its requests beyond that. The limits are counted in this process alone, so a "
            + "deployment running several enforces them once per process rather than once in total.")]
    private partial void LogEndpointRateLimits(
        string mcpEndpointPath,
        int maxConcurrentRequests,
        int concurrencyQueueLimit,
        int tokenCapacity,
        int tokensPerReplenishmentPeriod,
        TimeSpan replenishmentPeriod,
        int requestQueueLimit);
}
