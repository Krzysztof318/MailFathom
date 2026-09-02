// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Host.Configuration.Endpoints;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Hosting.Warnings;

/// <summary>States at startup how many connections this process accepts at once, or that it accepts them without a ceiling.</summary>
/// <remarks>
/// <para>
/// One line for the process rather than one per endpoint, which is the whole point worth reporting: every other bound
/// in the transport configuration belongs to a surface, and this one is reached before anything knows which surface a
/// connection is for. An operator reading it beside the per-endpoint lines is reading the ceiling that applies to all
/// of them together, including the probes.
/// </para>
/// <para>
/// Turning it off is a warning, because the framework's own default is no ceiling at all: connections are accepted
/// until the operating system stops supplying them, and the work each one costs before a request exists — the accept,
/// the handshake, and any certificate chain building — is spent below every limit an endpoint can express. It is the
/// right setting where an ingress or a firewall already bounds them, and an accident everywhere else.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class ConnectionLimitsStartupReport : IHostedService
{
    private readonly ConnectionLimitsOptions connectionLimitSettings;
    private readonly ILogger<ConnectionLimitsStartupReport> logger;

    /// <summary>Initializes a new startup report.</summary>
    /// <param name="connectionLimitSettings">The connection limits startup was composed from.</param>
    /// <param name="logger">The startup logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="connectionLimitSettings" /> is <see langword="null" />.</exception>
    public ConnectionLimitsStartupReport(
        IOptions<ConnectionLimitsOptions> connectionLimitSettings,
        ILogger<ConnectionLimitsStartupReport> logger)
    {
        ArgumentNullException.ThrowIfNull(connectionLimitSettings);

        this.connectionLimitSettings = connectionLimitSettings.Value;
        this.logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!this.connectionLimitSettings.Enabled)
        {
            this.LogProcessServedWithoutConnectionLimit(
                $"{ConnectionLimitsOptions.SectionName}:{nameof(ConnectionLimitsOptions.Enabled)}");

            return Task.CompletedTask;
        }

        this.LogProcessConnectionLimit(this.connectionLimitSettings.MaxConcurrentConnections);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "This process accepts connections without a ceiling, so a peer can spend accepts, TLS handshakes, and "
            + "certificate chain building without ever reaching a request an endpoint's limits could refuse. This is "
            + "the right setting only where an ingress or a firewall in front of this process already bounds them. "
            + "Remove {ConnectionLimitSetting} to run under the product default.")]
    private partial void LogProcessServedWithoutConnectionLimit(string connectionLimitSetting);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "This process accepts at most {MaxConcurrentConnections} connections at once across every listener it "
            + "opens, which is the whole process rather than one per endpoint. The limit is counted in this process "
            + "alone, so a deployment running several enforces it once per instance rather than once in total.")]
    private partial void LogProcessConnectionLimit(int maxConcurrentConnections);
}
