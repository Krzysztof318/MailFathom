// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Mcp;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Hosting.Warnings;

/// <summary>States at startup that an enabled MCP endpoint requires no credential of the clients it serves.</summary>
/// <remarks>
/// <para>
/// The unauthenticated posture is what a deployment gets for turning no authentication method on, so this is the
/// startup message that keeps that from being a silent outcome: it fires when the enabled endpoint requires no
/// credential, and stays silent as soon as any method is configured. What it refuses to let happen is that a mailbox
/// turns out months later to have been reachable by anything that could reach its address.
/// </para>
/// <para>
/// Neither the origin policy nor a client certificate, when those arrive, can silence it. Neither identifies the person
/// whose mail is being read: an origin restricts which page a browser will let make the request, and a client
/// certificate names the application making it. Treating either as a reason to stay quiet here would report an
/// authenticated deployment to an operator who has none.
/// </para>
/// <para>
/// The origin policy does earn a second warning, because under this posture it is the only thing left between a web
/// page and the mailbox. Serving every browser origin with no credential required is what makes DNS rebinding work: a
/// page the user never visited reaches a loopback or private address, the browser attaches its own <c>Origin</c>, and
/// the permissive CORS headers then let the page read what came back. That combination is deliberately allowed rather
/// than refused at startup, because it is what makes the endpoint work behind a reverse proxy or on a trusted network
/// without further configuration — so it is reported, and the judgement stays the operator's.
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
        if (!this.endpointSettings.Enabled || this.endpointSettings.RequiresAuthentication)
        {
            return Task.CompletedTask;
        }

        this.LogEndpointServedWithoutTransportAuthentication(McpEndpointRoute.Path);

        if (this.endpointSettings.Cors.ServesEveryBrowserOrigin)
        {
            this.LogEveryBrowserOriginServedWithoutTransportAuthentication();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The MCP endpoint is enabled on {McpEndpointPath} with no authentication method configured, so anything "
            + "that can reach this address can read the synchronized mailboxes. Add an entry to McpEndpoint:Authentication "
            + "carrying an ApiKey block, a PublicKey block, an OAuth block, or any combination of them, unless the "
            + "address is reachable only from this "
            + "machine or from a network you control. Neither an origin policy nor a client certificate substitutes for "
            + "this: the first restricts which page a browser will let call, the second names the application calling, and "
            + "neither identifies the person whose mail is served.")]
    private partial void LogEndpointServedWithoutTransportAuthentication(string mcpEndpointPath);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Every browser origin is served while no credential is required, so a web page the user never visited "
            + "can reach this endpoint through DNS rebinding and read what it returns. This is the right posture only "
            + "where the address is unreachable from a browser that could be aimed at it, such as an intranet or a "
            + "reverse proxy that authenticates. Replace the '*' in McpEndpoint:Cors:AllowedOrigins with the origins "
            + "served wherever a browser could be pointed at this address.")]
    private partial void LogEveryBrowserOriginServedWithoutTransportAuthentication();
}
