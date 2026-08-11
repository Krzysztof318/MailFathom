// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Mcp;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Hosting.Warnings;

/// <summary>States at startup how long each enabled endpoint lets one request run, or that it sets no ceiling.</summary>
/// <remarks>
/// <para>
/// Reported separately from the rate limits, and by a service of its own, because the two answer different questions
/// and an operator turns one off without the other. What they share is the reason either is reported at all: both apply
/// whether or not anybody wrote them down, so both are invisible in a deployment running on defaults unless startup
/// says what they are.
/// </para>
/// <para>
/// Turning the ceiling off is a warning rather than an observation, because from that point a request holds its
/// concurrency permit for as long as it takes and the endpoint's own concurrency limit stops being a bound on anything
/// but how many such requests there are at once. It is defensible behind something that already abandons a stalled
/// request, and an accident everywhere else.
/// </para>
/// <para>
/// It reports what an operator configured and nothing about who is calling. It runs as a hosted service so it appears
/// among the other startup diagnostics, and it is registered whether or not either endpoint is enabled, because it is
/// the report that decides whether it has anything to say.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class TransportRequestTimeoutStartupReport : IHostedService
{
    private const string McpEndpointName = "MCP";

    private const string AdminEndpointName = "administrative";

    private readonly McpEndpointOptions mcpEndpointSettings;
    private readonly AdminEndpointOptions adminEndpointSettings;
    private readonly ILogger<TransportRequestTimeoutStartupReport> logger;

    /// <summary>Initializes a new startup report.</summary>
    /// <param name="mcpEndpointSettings">The MCP endpoint settings startup was composed from.</param>
    /// <param name="adminEndpointSettings">The administrative endpoint settings startup was composed from.</param>
    /// <param name="logger">The startup logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="mcpEndpointSettings" /> or <paramref name="adminEndpointSettings" /> is <see langword="null" />.</exception>
    public TransportRequestTimeoutStartupReport(
        IOptions<McpEndpointOptions> mcpEndpointSettings,
        IOptions<AdminEndpointOptions> adminEndpointSettings,
        ILogger<TransportRequestTimeoutStartupReport> logger)
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
                $"{McpEndpointOptions.SectionName}:{nameof(McpEndpointOptions.RequestTimeout)}",
                this.mcpEndpointSettings.RequestTimeout);
        }

        if (this.adminEndpointSettings.Enabled)
        {
            this.Report(
                AdminEndpointName,
                AdminEndpointOptions.RoutePrefix,
                $"{AdminEndpointOptions.SectionName}:{nameof(AdminEndpointOptions.RequestTimeout)}",
                this.adminEndpointSettings.RequestTimeout);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>States one endpoint's applied ceiling, or warns that it has none.</summary>
    private void Report(
        string endpointName,
        string endpointPath,
        string requestTimeoutSectionPath,
        TransportRequestTimeoutOptions requestTimeoutSettings)
    {
        if (!requestTimeoutSettings.Enabled)
        {
            this.LogEndpointServedWithoutRequestTimeout(endpointName, endpointPath, requestTimeoutSectionPath);

            return;
        }

        this.LogEndpointRequestTimeout(endpointName, endpointPath, requestTimeoutSettings.Duration);
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The {EndpointName} endpoint is enabled on {EndpointPath} with no request ceiling, so one request can "
            + "hold a concurrency permit for as long as it takes and the endpoint's concurrency limit bounds how many "
            + "such requests run at once rather than how long any of them lasts. This is the right setting only where "
            + "something in front of this process already abandons a request its backend is still serving. Remove "
            + "{RequestTimeoutSection}:Enabled to run under the product default.")]
    private partial void LogEndpointServedWithoutRequestTimeout(
        string endpointName,
        string endpointPath,
        string requestTimeoutSection);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The {EndpointName} endpoint on {EndpointPath} abandons a request that has run for {RequestTimeout}, "
            + "answering 504 and releasing the concurrency permit it held. The ceiling encloses the outbound budgets a "
            + "request spends, so narrowing it below what a configured AI provider is allowed would report a gateway "
            + "timeout where that provider's own classified failure belonged.")]
    private partial void LogEndpointRequestTimeout(
        string endpointName,
        string endpointPath,
        TimeSpan requestTimeout);
}
