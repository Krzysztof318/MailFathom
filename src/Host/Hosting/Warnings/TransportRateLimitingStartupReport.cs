// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Mcp;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Hosting.Warnings;

/// <summary>States at startup what bounds each enabled endpoint is serving under, or that it has none.</summary>
/// <remarks>
/// <para>
/// The limits are the one part of an endpoint's configuration that applies whether or not an operator wrote it down, so
/// they are also the one part nobody would otherwise see. Reporting them makes a deployment running on defaults
/// verifiable without reading the source, and makes a deployment running on a number somebody mistyped visible at the
/// moment it starts rather than the first time a caller is refused.
/// </para>
/// <para>
/// Both endpoints are reported by one service, and each is reported separately, because the two carry independent
/// numbers and an operator who narrowed one has to be able to read back that they narrowed the one they meant. A
/// disabled endpoint contributes nothing rather than a line saying it is off, which is what keeps the report about
/// limits that are in force.
/// </para>
/// <para>
/// Turning the limits off is reported as a warning, because from that point the endpoint will serve whatever it is
/// asked for until something runs out. It is a defensible posture behind a proxy that already shapes the traffic, and
/// an accident everywhere else, and only the operator can tell those apart.
/// </para>
/// <para>
/// It reports what an operator configured and nothing about who is calling: no client name, no address, no origin. It
/// runs as a hosted service so it appears among the other startup diagnostics, and it is registered whether or not
/// either endpoint is enabled, because it is the report that decides whether it has anything to say.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class TransportRateLimitingStartupReport : IHostedService
{
    private const string McpEndpointName = "MCP";

    private const string AdminEndpointName = "administrative";

    private readonly McpEndpointOptions mcpEndpointSettings;
    private readonly AdminEndpointOptions adminEndpointSettings;
    private readonly ILogger<TransportRateLimitingStartupReport> logger;

    /// <summary>Initializes a new startup report.</summary>
    /// <param name="mcpEndpointSettings">The MCP endpoint settings startup was composed from.</param>
    /// <param name="adminEndpointSettings">The administrative endpoint settings startup was composed from.</param>
    /// <param name="logger">The startup logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="mcpEndpointSettings" /> or <paramref name="adminEndpointSettings" /> is <see langword="null" />.</exception>
    public TransportRateLimitingStartupReport(
        IOptions<McpEndpointOptions> mcpEndpointSettings,
        IOptions<AdminEndpointOptions> adminEndpointSettings,
        ILogger<TransportRateLimitingStartupReport> logger)
    {
        ArgumentNullException.ThrowIfNull(mcpEndpointSettings);
        ArgumentNullException.ThrowIfNull(adminEndpointSettings);

        this.mcpEndpointSettings = mcpEndpointSettings.Value;
        this.adminEndpointSettings = adminEndpointSettings.Value;
        this.logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (this.mcpEndpointSettings.Enabled)
        {
            this.Report(
                McpEndpointName,
                McpEndpointRoute.Path,
                $"{McpEndpointOptions.SectionName}:{nameof(McpEndpointOptions.RateLimiting)}",
                this.mcpEndpointSettings.RateLimiting);
        }

        if (this.adminEndpointSettings.Enabled)
        {
            this.Report(
                AdminEndpointName,
                AdminEndpointOptions.RoutePrefix,
                $"{AdminEndpointOptions.SectionName}:{nameof(AdminEndpointOptions.RateLimiting)}",
                this.adminEndpointSettings.RateLimiting);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>States one endpoint's applied limits, or warns that it has none.</summary>
    private void Report(
        string endpointName,
        string endpointPath,
        string rateLimitingSectionPath,
        TransportRateLimitingOptions rateLimitingSettings)
    {
        if (!rateLimitingSettings.Enabled)
        {
            this.LogEndpointServedWithoutRateLimits(endpointName, endpointPath, rateLimitingSectionPath);

            return;
        }

        this.LogEndpointRateLimits(
            endpointName,
            endpointPath,
            rateLimitingSettings.MaxConcurrentRequests,
            rateLimitingSettings.ConcurrencyQueueLimit,
            rateLimitingSettings.TokenCapacity,
            rateLimitingSettings.TokensPerReplenishmentPeriod,
            rateLimitingSettings.ReplenishmentPeriod,
            rateLimitingSettings.RequestQueueLimit);
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The {EndpointName} endpoint is enabled on {EndpointPath} with rate limiting turned off, so one caller "
            + "can hold every database connection, response stream, and thread the process has until something runs out. "
            + "This is the right setting only where something in front of this process already bounds the traffic "
            + "reaching it. Remove {RateLimitingSection}:Enabled to run under the product defaults.")]
    private partial void LogEndpointServedWithoutRateLimits(
        string endpointName,
        string endpointPath,
        string rateLimitingSection);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The {EndpointName} endpoint on {EndpointPath} serves at most {MaxConcurrentRequests} requests at once "
            + "across every caller, queueing {ConcurrencyQueueLimit} beyond that, and allows each caller a burst of "
            + "{TokenCapacity} requests restored at {TokensPerReplenishmentPeriod} every {ReplenishmentPeriod}, queueing "
            + "{RequestQueueLimit} of its requests beyond that. The limits are counted in this process alone, so a "
            + "deployment running several enforces them once per process rather than once in total.")]
    private partial void LogEndpointRateLimits(
        string endpointName,
        string endpointPath,
        int maxConcurrentRequests,
        int concurrencyQueueLimit,
        int tokenCapacity,
        int tokensPerReplenishmentPeriod,
        TimeSpan replenishmentPeriod,
        int requestQueueLimit);
}
