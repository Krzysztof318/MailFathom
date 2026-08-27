// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Host.Configuration.Endpoints;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Hosting.Warnings;

/// <summary>States at startup what an enabled client endpoint is missing.</summary>
/// <remarks>
/// <para>
/// The same two postures the other surfaces announce, and neither is safe to assume here either. An endpoint requiring
/// no credential is reachable by anything that can reach its address, and one served in clear text hands whatever
/// credential it does require to anybody watching the network. Either can be the right choice — a loopback bind, a
/// private network, a reverse proxy that terminates TLS — and only an operator knows which they have.
/// </para>
/// <para>
/// The consequence is the MCP endpoint's rather than the administrative one's: what an unguarded client endpoint
/// discloses is mail. It is warned about separately because an operator who enabled one surface and not the other has
/// to read which of them the warning is about.
/// </para>
/// <para>
/// It runs as a hosted service so it reports during startup, next to the other startup diagnostics an operator reads,
/// rather than on the first request that reaches an endpoint nobody was watching. It is registered whether or not the
/// endpoint is enabled, because it is the warning that decides whether it has anything to say.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class ClientTransportSecurityWarning : IHostedService
{
    private readonly ClientEndpointOptions endpointSettings;
    private readonly ILogger<ClientTransportSecurityWarning> logger;

    /// <summary>Initializes a new startup warning.</summary>
    /// <param name="endpointSettings">The endpoint settings startup was composed from.</param>
    /// <param name="logger">The startup logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpointSettings" /> is <see langword="null" />.</exception>
    public ClientTransportSecurityWarning(
        IOptions<ClientEndpointOptions> endpointSettings,
        ILogger<ClientTransportSecurityWarning> logger)
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

        if (!this.endpointSettings.RequiresAuthentication)
        {
            this.LogEndpointServedWithoutAuthentication(ClientEndpointOptions.RoutePrefix);
        }

        if (!this.endpointSettings.TerminatesTls)
        {
            this.LogEndpointServedWithoutTransportEncryption(this.endpointSettings.Port);
        }

        if (this.endpointSettings.Application.Enabled && this.endpointSettings.ServesClearText)
        {
            this.LogClientServedInClearText(this.endpointSettings.Port);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The client endpoint is enabled on {ClientRoutePrefix} with no authentication method configured, so "
            + "anything that can reach this address is served the mailbox this deployment holds. Add an entry to "
            + "ClientEndpoint:Authentication carrying an ApiKey block, a PublicKey block, an OAuth block, a Basic block, "
            + "or any combination of them, unless the address is reachable only from this machine or from a network "
            + "you control.")]
    private partial void LogEndpointServedWithoutAuthentication(string clientRoutePrefix);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The client is served in clear text on port {ClientPort}, which this deployment permitted with "
            + "ClientEndpoint:Application:AllowClearText. The page a browser downloads from this address, and every "
            + "request it then makes, crosses the network unprotected — so this is the right posture only where "
            + "something in front of this process terminates TLS, or where the address is reachable only from the "
            + "machine the browser runs on. It is reported at every startup rather than assumed to still be true.")]
    private partial void LogClientServedInClearText(int clientPort);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The client endpoint is served in clear text on port {ClientPort}, so any credential a client presents "
            + "to it is readable by anything on the path. This is the right posture only behind a reverse proxy that "
            + "terminates TLS, or on a loopback bind. Configure ClientEndpoint:Https:Endpoints otherwise.")]
    private partial void LogEndpointServedWithoutTransportEncryption(int clientPort);
}
