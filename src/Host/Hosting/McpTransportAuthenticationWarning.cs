// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using MailMcp.Host.Configuration;
using MailMcp.Mcp;
using Microsoft.Extensions.Options;

namespace MailMcp.Host.Hosting;

/// <summary>States at startup that an enabled MCP endpoint has no transport authentication in front of it.</summary>
/// <remarks>
/// <para>
/// The interim posture is deliberate: OAuth 2.1 and mTLS belong to a later stage, and until they land an enabled endpoint
/// is reachable by anything that can reach its address. What this refuses to let happen is for that to be discovered
/// months later, so enabling the endpoint costs one unmissable warning naming the controls that are missing and where
/// they arrive.
/// </para>
/// <para>
/// The warning is unconditional on the endpoint being enabled because there is no transport authentication to detect
/// yet — no scheme, no certificate requirement, nothing a check could observe. When the OAuth work adds one, this
/// becomes a real condition rather than a new mechanism: the warning stays and starts asking whether authentication is
/// configured.
/// </para>
/// <para>
/// It runs as a hosted service so it reports during startup, next to the other startup diagnostics an operator reads,
/// rather than on the first request that reaches an endpoint nobody was watching.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class McpTransportAuthenticationWarning : IHostedService
{
    private readonly McpEndpointOptions endpointSettings;
    private readonly ILogger<McpTransportAuthenticationWarning> logger;

    /// <summary>Initializes a new startup warning.</summary>
    /// <param name="endpointSettings">The endpoint settings startup was composed from.</param>
    /// <param name="logger">The startup logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpointSettings" /> is <see langword="null" />.</exception>
    public McpTransportAuthenticationWarning(
        IOptions<McpEndpointOptions> endpointSettings,
        ILogger<McpTransportAuthenticationWarning> logger)
    {
        ArgumentNullException.ThrowIfNull(endpointSettings);

        this.endpointSettings = endpointSettings.Value;
        this.logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (this.endpointSettings.Enabled)
        {
            this.LogEndpointServedWithoutTransportAuthentication(McpEndpointRoute.Path);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The MCP endpoint is enabled on {McpEndpointPath} with no transport authentication: neither OAuth 2.1 "
            + "resource-server authentication nor mutual TLS is in place, so anything that can reach this address can read "
            + "the synchronized mailboxes. This is the interim posture until the OAuth 2.1 work of draft stage 9 lands. "
            + "Point it at development mailboxes only, and restrict who can reach the address at the network layer.")]
    private partial void LogEndpointServedWithoutTransportAuthentication(string mcpEndpointPath);
}
