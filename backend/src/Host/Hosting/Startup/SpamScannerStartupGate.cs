// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Spam.Scanning;

namespace MailFathom.Host.Hosting.Startup;

/// <summary>Refuses to start when the spam scanner is switched on and no daemon answers.</summary>
/// <remarks>
/// <para>
/// The scanner is the one guarded dependency that does <b>not</b> fail closed: a scan that cannot be performed leaves an
/// occurrence with the verdict its headers reached, and the classification is recorded either way. That is the right
/// answer for one message and the reason this gate exists for the deployment — an instance whose sidecar never came up
/// would classify everything from headers alone, look entirely healthy doing it, and leave the operator reading a
/// switched-on scanner in their own configuration as a second opinion that was being taken.
/// </para>
/// <para>
/// Registered only where that switch is on, so a deployment that never opted in runs no gate and opens no socket. It
/// reports its completion like the others, which is what puts the daemon's own start-up — it fetches rule updates before
/// it listens — inside the interval a startup probe covers rather than outside it.
/// </para>
/// <para>
/// It runs in <see cref="IHostedService.StartAsync" /> rather than earlier, so it happens after the secret and options
/// work every configured value depends on, and it is registered ahead of the workers so nothing classifies mail before
/// the scanner is proven. One attempt rather than a wait loop, which is the answer the other two gates give: a daemon
/// that is still starting is reached by the orchestrator restarting this process.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class SpamScannerStartupGate : IHostedService
{
    private readonly ISpamScannerProbe probe;
    private readonly HostStartupGates startupGates;
    private readonly ILogger<SpamScannerStartupGate> logger;

    /// <summary>Initializes a new spam scanner startup gate.</summary>
    /// <param name="probe">Asks the configured scanner whether it can score anything at all.</param>
    /// <param name="startupGates">The tracker this gate reports its completion to, which is what the startup probe reads.</param>
    /// <param name="logger">The startup logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public SpamScannerStartupGate(
        ISpamScannerProbe probe,
        HostStartupGates startupGates,
        ILogger<SpamScannerStartupGate> logger)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(startupGates);
        ArgumentNullException.ThrowIfNull(logger);

        this.probe = probe;
        this.startupGates = startupGates;
        this.logger = logger;
    }

    /// <inheritdoc />
    /// <exception cref="SpamScannerUnavailableException">Thrown when the scanner could not be reached, did not answer inside its bound, or answered unintelligibly.</exception>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await this.probe.VerifyAvailableAsync(cancellationToken);

        this.LogScannerAvailable();

        this.startupGates.MarkCompleted(HostStartupGate.SpamScanner);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The spam scanner answers and has named the corpus classifications will record.")]
    private partial void LogScannerAvailable();
}
