// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using MailMcp.Host.Configuration;
using MailMcp.Mcp;
using Microsoft.Extensions.Options;

namespace MailMcp.Host.Hosting;

/// <summary>States at startup that an enabled MCP endpoint requires no credential of the clients it serves.</summary>
/// <remarks>
/// <para>
/// The unauthenticated posture is now a choice rather than the only option, so the warning is a condition rather than
/// an announcement: it fires when the enabled endpoint runs under
/// <see cref="McpTransportAuthenticationMode.None" /> and stays silent when a credential is required. What it refuses
/// to let happen is unchanged — that a mailbox turns out months later to have been reachable by anything that could
/// reach its address.
/// </para>
/// <para>
/// It says nothing about the origin policy, and it will say nothing about a client certificate when those arrive.
/// Neither identifies the person whose mail is being read: an origin restricts which page a browser will let make the
/// request, and a client certificate names the application making it. Treating either as a reason to stay quiet here
/// would report an authenticated deployment to an operator who has none.
/// </para>
/// <para>
/// It runs as a hosted service so it reports during startup, next to the other startup diagnostics an operator reads,
/// rather than on the first request that reaches an endpoint nobody was watching. It is registered whether or not the
/// endpoint is enabled, because it is the warning that decides whether it has anything to say.
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
        if (this.endpointSettings is { Enabled: true, Authentication: McpTransportAuthenticationMode.None })
        {
            this.LogEndpointServedWithoutTransportAuthentication(McpEndpointRoute.Path);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The MCP endpoint is enabled on {McpEndpointPath} with authentication set to None, so anything that can "
            + "reach this address can read the synchronized mailboxes. Configure API keys instead unless the address is "
            + "reachable only from this machine or from a network you control. Neither an origin policy nor a client "
            + "certificate substitutes for this: the first restricts which page a browser will let call, the second names "
            + "the application calling, and neither identifies the person whose mail is served.")]
    private partial void LogEndpointServedWithoutTransportAuthentication(string mcpEndpointPath);
}
