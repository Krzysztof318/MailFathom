// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Host.Configuration;
using MailFathom.Mcp;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Hosting;

/// <summary>States at startup that an enabled MCP endpoint terminates no TLS of its own.</summary>
/// <remarks>
/// <para>
/// Serving the endpoint over clear text is a supported posture rather than a mistake, which is why this reports and
/// does not refuse. Two deployments run it deliberately: local development, where the endpoint is reachable only from
/// the machine it runs on, and a deployment behind a TLS-terminating reverse proxy, where the proxy already holds the
/// operator's certificate and a second TLS layer inside the trust boundary protects nothing. Only an operator knows
/// which of those they have, and neither is something MailFathom can detect.
/// </para>
/// <para>
/// What it refuses to let happen is the third case: an endpoint reachable across a network that nobody meant to expose
/// in clear text. On that hop an API key is readable by anything on the path and so is every message the tools return,
/// so the warning names both rather than only the credential.
/// </para>
/// <para>
/// It is a separate warning from <see cref="McpTransportAuthenticationWarning" /> because it answers a separate
/// question — whether the transport is confidential, not whether the caller is identified — and because its condition
/// is independent. A deployment that requires an API key still runs unencrypted until it configures a profile or puts
/// a proxy in front, and telling it so is the whole point.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class McpTransportEncryptionWarning : IHostedService
{
    private readonly McpEndpointOptions endpointSettings;
    private readonly ILogger<McpTransportEncryptionWarning> logger;

    /// <summary>Initializes a new startup warning.</summary>
    /// <param name="endpointSettings">The endpoint settings startup was composed from.</param>
    /// <param name="logger">The startup logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpointSettings" /> is <see langword="null" />.</exception>
    public McpTransportEncryptionWarning(
        IOptions<McpEndpointOptions> endpointSettings,
        ILogger<McpTransportEncryptionWarning> logger)
    {
        ArgumentNullException.ThrowIfNull(endpointSettings);

        this.endpointSettings = endpointSettings.Value;
        this.logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (this.endpointSettings is { Enabled: true, Https.TerminatesTls: false })
        {
            this.LogEndpointServedWithoutTransportEncryption(McpEndpointRoute.Path);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The MCP endpoint is enabled on {McpEndpointPath} and no HTTPS profile is configured, so it is served "
            + "over whichever listener this host was started with — clear text unless that listener or something in front "
            + "of this process supplies HTTPS. On a clear-text hop anything on the network path can read the API key a "
            + "client presents and every message the tools return, and a client certificate never arrives at all. This is "
            + "the expected posture for local development and for a deployment behind a TLS-terminating reverse proxy; "
            + "anywhere else, configure McpEndpoint:Https:Endpoints so this process presents your domain's certificate "
            + "itself.")]
    private partial void LogEndpointServedWithoutTransportEncryption(string mcpEndpointPath);
}
