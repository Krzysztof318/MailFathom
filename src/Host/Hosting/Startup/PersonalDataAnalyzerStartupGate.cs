// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.SensitiveContent.Detection;

namespace MailFathom.Host.Hosting.Startup;

/// <summary>Refuses to start when the personal-data scanner is switched on and its analyzer does not answer.</summary>
/// <remarks>
/// <para>
/// The scanner fails closed, so a deployment whose analyzer is absent refuses every read, derived write, and egress the
/// scanner guards, for as long as it runs. Refusing to start is diagnosed at once; an instance that logged the fact and
/// served anyway would look healthy while answering nothing, and the operator would read their own switch as protection in
/// force.
/// </para>
/// <para>
/// Registered only where that switch is on, so a deployment that never opted in runs no gate and probes nothing. It reports
/// its completion like the others, which is what puts the analyzer's own start-up time inside the interval a startup probe
/// covers rather than outside it.
/// </para>
/// <para>
/// The check runs in <see cref="IHostedService.StartAsync" /> rather than earlier, so it happens after the secret and
/// options work every configured value depends on, and it is registered ahead of the workers so nothing reads or writes
/// mail before the analyzer is proven. One attempt is made rather than a wait loop: the client it goes through already
/// carries the standard resilience handler, so an analyzer still loading its model is retried inside that window, and an
/// orchestrator restarting the process is what covers a longer absence — the same answer the database schema gate gives.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class PersonalDataAnalyzerStartupGate : IHostedService
{
    private readonly IPersonalDataAnalyzerProbe probe;
    private readonly HostStartupGates startupGates;
    private readonly ILogger<PersonalDataAnalyzerStartupGate> logger;

    /// <summary>Initializes a new personal-data analyzer startup gate.</summary>
    /// <param name="probe">Asks the configured analyzer whether it can answer at all.</param>
    /// <param name="startupGates">The tracker this gate reports its completion to, which is what the startup probe reads.</param>
    /// <param name="logger">The startup logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public PersonalDataAnalyzerStartupGate(
        IPersonalDataAnalyzerProbe probe,
        HostStartupGates startupGates,
        ILogger<PersonalDataAnalyzerStartupGate> logger)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(startupGates);
        ArgumentNullException.ThrowIfNull(logger);

        this.probe = probe;
        this.startupGates = startupGates;
        this.logger = logger;
    }

    /// <inheritdoc />
    /// <exception cref="PersonalDataAnalyzerUnavailableException">Thrown when the analyzer could not be reached, refused the probe, or recognises nothing a switched-on category maps onto.</exception>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await this.probe.VerifyAvailableAsync(cancellationToken);

        this.LogAnalyzerAvailable();

        this.startupGates.MarkCompleted(HostStartupGate.PersonalDataAnalyzer);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The personal-data analyzer answers for every category the scanner is switched on for.")]
    private partial void LogAnalyzerAvailable();
}
