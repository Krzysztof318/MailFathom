// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Host.Configuration.Endpoints;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Hosting.Warnings;

/// <summary>States at startup what an enabled administrative endpoint is missing.</summary>
/// <remarks>
/// <para>
/// Two postures are legitimate and neither is safe to assume, so both are announced rather than refused. An endpoint
/// requiring no credential is reachable by anything that can reach its address, and one served in clear text hands
/// whatever credential it does require to anybody watching the network. Either can be the right choice — a loopback
/// bind, a private network, a reverse proxy that terminates TLS — and only an operator knows which they have.
/// </para>
/// <para>
/// It says more than the MCP warning does about the same posture, because the consequence is different. An
/// unauthenticated MCP endpoint discloses mail; an unauthenticated administrative endpoint hands over the ability to
/// administer the service that reads it.
/// </para>
/// <para>
/// It runs as a hosted service so it reports during startup, next to the other startup diagnostics an operator reads,
/// rather than on the first request that reaches an endpoint nobody was watching. It is registered whether or not the
/// endpoint is enabled, because it is the warning that decides whether it has anything to say.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class AdminTransportSecurityWarning : IHostedService
{
    private readonly AdminEndpointOptions endpointSettings;
    private readonly ILogger<AdminTransportSecurityWarning> logger;

    /// <summary>Initializes a new startup warning.</summary>
    /// <param name="endpointSettings">The endpoint settings startup was composed from.</param>
    /// <param name="logger">The startup logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpointSettings" /> is <see langword="null" />.</exception>
    public AdminTransportSecurityWarning(
        IOptions<AdminEndpointOptions> endpointSettings,
        ILogger<AdminTransportSecurityWarning> logger)
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
            this.LogEndpointServedWithoutAuthentication(AdminEndpointOptions.RoutePrefix);
        }

        if (!this.endpointSettings.TerminatesTls)
        {
            this.LogEndpointServedWithoutTransportEncryption(this.endpointSettings.Port);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The administrative endpoint is enabled on {AdminRoutePrefix} with no authentication method "
            + "configured, so anything that can reach this address can administer this service. Add an entry to "
            + "AdminEndpoint:Authentication carrying an ApiKey block, a PublicKey block, an OAuth block, or any "
            + "combination of them, unless the address "
            + "is reachable only from this machine or from a network you control.")]
    private partial void LogEndpointServedWithoutAuthentication(string adminRoutePrefix);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The administrative endpoint is served in clear text on port {AdminPort}, so any credential a client "
            + "presents to it is readable by anything on the path. This is the right posture only behind a reverse "
            + "proxy that terminates TLS, or on a loopback bind. Configure AdminEndpoint:Https:Endpoints otherwise.")]
    private partial void LogEndpointServedWithoutTransportEncryption(int adminPort);
}
