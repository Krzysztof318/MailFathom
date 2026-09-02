// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Host.Configuration.Endpoints;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Hosting.Warnings;

/// <summary>States at startup which clear-text port each surface redirects from, and to which domains.</summary>
/// <remarks>
/// <para>
/// A redirect listener is the one socket a deployment can open without having written a port, so it is the one an
/// operator auditing what the process listens on is most likely to be unable to account for. Reporting it beside the
/// domains it redirects to is what makes every socket the process opened readable from the startup log.
/// </para>
/// <para>
/// Information rather than a warning, because the posture is the safe one: the surface terminates TLS and the clear-text
/// socket serves nothing but the address of that TLS. The warnings about a surface that terminates no TLS at all are
/// separate and unchanged — <see cref="McpTransportEncryptionWarning" />, <see cref="AdminTransportSecurityWarning" />,
/// and <see cref="ClientTransportSecurityWarning" /> — and none has anything to say about a deployment this one reports
/// on.
/// </para>
/// <para>
/// One report for every surface rather than one per surface, so the sockets are listed together in the order they were
/// composed. It is registered whether or not any surface redirects, because it is the report that decides whether it
/// has anything to say.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class TransportClearTextRedirectReport : IHostedService
{
    private readonly McpEndpointOptions mcpEndpointSettings;
    private readonly AdminEndpointOptions adminEndpointSettings;
    private readonly ClientEndpointOptions clientEndpointSettings;
    private readonly ILogger<TransportClearTextRedirectReport> logger;

    /// <summary>Initializes a new startup report.</summary>
    /// <param name="mcpEndpointSettings">The MCP endpoint settings startup was composed from.</param>
    /// <param name="adminEndpointSettings">The administrative endpoint settings startup was composed from.</param>
    /// <param name="clientEndpointSettings">The client endpoint settings startup was composed from.</param>
    /// <param name="logger">The startup logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public TransportClearTextRedirectReport(
        IOptions<McpEndpointOptions> mcpEndpointSettings,
        IOptions<AdminEndpointOptions> adminEndpointSettings,
        IOptions<ClientEndpointOptions> clientEndpointSettings,
        ILogger<TransportClearTextRedirectReport> logger)
    {
        ArgumentNullException.ThrowIfNull(mcpEndpointSettings);
        ArgumentNullException.ThrowIfNull(adminEndpointSettings);
        ArgumentNullException.ThrowIfNull(clientEndpointSettings);

        this.mcpEndpointSettings = mcpEndpointSettings.Value;
        this.adminEndpointSettings = adminEndpointSettings.Value;
        this.clientEndpointSettings = clientEndpointSettings.Value;
        this.logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // The description is built into a local rather than passed as a call, so composing it is not an argument the
        // logging call has to evaluate before it knows the level is enabled. It is paid once, at startup, inside a branch
        // that has already decided this deployment has a socket to account for.
        if (this.mcpEndpointSettings is { Enabled: true, RedirectsClearText: true })
        {
            var mcpRedirectTargets = DescribeTargets(this.mcpEndpointSettings.Https);

            this.LogMcpEndpointRedirectsClearText(
                this.mcpEndpointSettings.Port,
                mcpRedirectTargets);
        }

        if (this.adminEndpointSettings is { Enabled: true, RedirectsClearText: true })
        {
            var adminRedirectTargets = DescribeTargets(this.adminEndpointSettings.Https);

            this.LogAdminEndpointRedirectsClearText(
                this.adminEndpointSettings.Port,
                adminRedirectTargets);
        }

        if (this.clientEndpointSettings is { Enabled: true, RedirectsClearText: true })
        {
            var clientRedirectTargets = DescribeTargets(this.clientEndpointSettings.Https);

            this.LogClientEndpointRedirectsClearText(
                this.clientEndpointSettings.Port,
                clientRedirectTargets);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Names each domain a redirect resolves to, beside the port it is served on.</summary>
    /// <remarks>The configured domains are this deployment's own published names rather than anything a caller supplied, which is what makes them safe to write to a log.</remarks>
    private static string DescribeTargets(TransportHttpsOptions httpsSettings) =>
        string.Join(
            ", ",
            httpsSettings.PublishedDomainPorts()
                .OrderBy(static target => target.Key, StringComparer.OrdinalIgnoreCase)
                .Select(static target => $"https://{target.Key}:{target.Value}"));

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The MCP endpoint redirects clear-text requests on port {ClearTextPort} to {RedirectTargets}. That "
            + "listener maps no route and answers every path with a 308, so nothing is reachable over it. A redirect "
            + "protects the next request and not the one that arrived — a credential already sent in clear text is on "
            + "the wire — so repoint your clients rather than relying on it. Set "
            + "McpEndpoint:Https:Redirect:Enabled to false to bind no clear-text port at all.")]
    private partial void LogMcpEndpointRedirectsClearText(int clearTextPort, string redirectTargets);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The administrative endpoint redirects clear-text requests on port {ClearTextPort} to "
            + "{RedirectTargets}. That listener maps no route and answers every path with a 308, so no administrative "
            + "operation is reachable over it. A redirect protects the next request and not the one that arrived — a "
            + "credential already sent in clear text is on the wire — so repoint mfctl rather than relying on it. Set "
            + "AdminEndpoint:Https:Redirect:Enabled to false to bind no clear-text port at all.")]
    private partial void LogAdminEndpointRedirectsClearText(int clearTextPort, string redirectTargets);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The client endpoint redirects clear-text requests on port {ClearTextPort} to {RedirectTargets}. That "
            + "listener maps no route and answers every path with a 308, so nothing of the mailbox is reachable over it. "
            + "A redirect protects the next request and not the one that arrived — a credential already sent in clear "
            + "text is on the wire — so repoint your clients rather than relying on it. Set "
            + "ClientEndpoint:Https:Redirect:Enabled to false to bind no clear-text port at all.")]
    private partial void LogClientEndpointRedirectsClearText(int clearTextPort, string redirectTargets);
}
