// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Diagnostics.CodeAnalysis;
using MailFathom.Host.Configuration;
using MailFathom.Infrastructure.Secrets;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Hosting;

/// <summary>Fails host startup when a configured secret cannot be resolved or the material behind it cannot be used.</summary>
/// <remarks>
/// <para>
/// The check runs in <see cref="IHostedLifecycleService.StartingAsync" />, which the host is documented to run before
/// any hosted service's <see cref="IHostedService.StartAsync" />, so the synchronization worker never starts against an
/// unresolvable secret or an unusable trust anchor. Options validation would be the obvious home, but it is
/// synchronous and resolution is not: running an asynchronous resolver inside it would mean blocking, which is exactly
/// what the asynchronous contract exists to avoid. Structural options validation stays in <c>ValidateOnStart</c>; only
/// secret resolution and certificate loading happen here.
/// </para>
/// <para>
/// Every failure is reported together. An operator who provisions five accounts and mistypes two credential names
/// otherwise pays one restart per mistake. The message carries the configuration path and the failure identity and
/// nothing else — no target path, no variable name, no material.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class SecretConfigurationStartupValidator : IHostedLifecycleService
{
    private readonly ISettingsSnapshot<MailSynchronizationOptions> mailSynchronizationSettings;
    private readonly ISettingsSnapshot<PersistenceOptions> persistenceSettings;
    private readonly McpEndpointOptions mcpEndpointSettings;
    private readonly SecretConfigurationValidator validator;
    private readonly SecretResolutionOptions resolutionOptions;
    private readonly HostStartupGates startupGates;
    private readonly ILogger<SecretConfigurationStartupValidator> logger;

    /// <summary>Initializes a new secret configuration startup validator.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="mcpEndpointSettings" /> or <paramref name="startupGates" /> is <see langword="null" />.</exception>
    /// <remarks>The endpoint settings arrive as the composed value rather than as a snapshot, because the section is read once while the host is built and takes a restart to change.</remarks>
    public SecretConfigurationStartupValidator(
        ISettingsSnapshot<MailSynchronizationOptions> mailSynchronizationSettings,
        ISettingsSnapshot<PersistenceOptions> persistenceSettings,
        IOptions<McpEndpointOptions> mcpEndpointSettings,
        SecretConfigurationValidator validator,
        SecretResolutionOptions resolutionOptions,
        HostStartupGates startupGates,
        ILogger<SecretConfigurationStartupValidator> logger)
    {
        ArgumentNullException.ThrowIfNull(mcpEndpointSettings);
        ArgumentNullException.ThrowIfNull(startupGates);

        this.mailSynchronizationSettings = mailSynchronizationSettings;
        this.persistenceSettings = persistenceSettings;
        this.mcpEndpointSettings = mcpEndpointSettings.Value;
        this.validator = validator;
        this.resolutionOptions = resolutionOptions;
        this.startupGates = startupGates;
        this.logger = logger;
    }

    /// <inheritdoc />
    /// <exception cref="OptionsValidationException">Thrown when any configured secret is unusable, carrying one message per failure.</exception>
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        this.LogActiveInterpretation(this.resolutionOptions.Interpretation);

        var failures = new List<string>(
            await this.validator.FindMailConfigurationErrorsAsync(
                this.mailSynchronizationSettings.Current,
                cancellationToken));

        failures.AddRange(
            await this.validator.FindPersistenceConfigurationErrorsAsync(
                this.persistenceSettings.Current,
                cancellationToken));

        failures.AddRange(
            await this.validator.FindMcpEndpointConfigurationErrorsAsync(
                this.mcpEndpointSettings,
                cancellationToken));

        if (failures.Count > 0)
        {
            // The failures span every bound root, so the exception is named after the secret configuration rather than
            // after one options type; each message already names the exact path an operator edits.
            throw new OptionsValidationException("Secrets", typeof(SecretResolutionOptions), failures);
        }

        this.startupGates.MarkCompleted(HostStartupGate.SecretConfiguration);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Secret-bearing configuration values are interpreted as {Interpretation}.")]
    private partial void LogActiveInterpretation(SecretValueInterpretation interpretation);
}
