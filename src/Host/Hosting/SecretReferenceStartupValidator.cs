// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using MailMcp.Host.Configuration;
using MailMcp.Infrastructure.Secrets;
using Microsoft.Extensions.Options;

namespace MailMcp.Host.Hosting;

/// <summary>Fails host startup when a configured secret reference cannot be resolved.</summary>
/// <remarks>
/// <para>
/// The check runs in <see cref="IHostedLifecycleService.StartingAsync" />, which the host is documented to run before
/// any hosted service's <see cref="IHostedService.StartAsync" />, so the synchronization worker never starts against an
/// unresolvable secret. Options validation would be the obvious home, but it is synchronous and resolution is not:
/// running an asynchronous resolver inside it would mean blocking, which is exactly what the asynchronous contract
/// exists to avoid. Structural options validation stays in <c>ValidateOnStart</c>; only secret resolution moves here.
/// </para>
/// <para>
/// Every failure is reported together. An operator who provisions five accounts and mistypes two credential names
/// otherwise pays one restart per mistake. The message carries the configuration path and the failure identity and
/// nothing else — no target path, no variable name, no material.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class SecretReferenceStartupValidator : IHostedLifecycleService
{
    private readonly IOptions<MailSynchronizationOptions> synchronizationOptions;
    private readonly IOptions<PersistenceOptions> persistenceOptions;
    private readonly ISecretReferenceResolver secretReferenceResolver;
    private readonly SecretResolutionOptions resolutionOptions;
    private readonly ILogger<SecretReferenceStartupValidator> logger;

    /// <summary>Initializes a new secret reference startup validator.</summary>
    public SecretReferenceStartupValidator(
        IOptions<MailSynchronizationOptions> synchronizationOptions,
        IOptions<PersistenceOptions> persistenceOptions,
        ISecretReferenceResolver secretReferenceResolver,
        SecretResolutionOptions resolutionOptions,
        ILogger<SecretReferenceStartupValidator> logger)
    {
        this.synchronizationOptions = synchronizationOptions;
        this.persistenceOptions = persistenceOptions;
        this.secretReferenceResolver = secretReferenceResolver;
        this.resolutionOptions = resolutionOptions;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        this.LogActiveInterpretation(this.resolutionOptions.Interpretation);

        var settings = FindSecretBearingSettings(
            ("MailSynchronization", this.synchronizationOptions.Value),
            ("Persistence", this.persistenceOptions.Value));

        var failures = new List<string>(settings.RawSecretPropertyPaths.Select(DescribeRawSecretProperty));

        foreach (var discovered in settings.Blocks)
        {
            var failure = await this.ValidateAsync(discovered, cancellationToken);
            if (failure is not null)
            {
                failures.Add(failure);
            }
        }

        if (failures.Count > 0)
        {
            // The failures span every bound root, so the exception is named after the secret configuration rather than
            // after one options type; each message already names the exact path an operator edits.
            throw new OptionsValidationException("Secrets", typeof(SecretResolutionOptions), failures);
        }
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

    private static DiscoveredSecretSettings FindSecretBearingSettings(
        params (string ConfigurationPath, object BoundOptions)[] roots)
    {
        var discoveredPerRoot = roots
            .Select(root => ConfiguredSecretDiscovery.FindSecretBearingSettings(root.BoundOptions, root.ConfigurationPath))
            .ToArray();

        return new DiscoveredSecretSettings(
            [.. discoveredPerRoot.SelectMany(discovered => discovered.Blocks)],
            [.. discoveredPerRoot.SelectMany(discovered => discovered.RawSecretPropertyPaths)]);
    }

    private static string DescribeRawSecretProperty(string configurationPath) =>
        $"{configurationPath} — a setting that names a secret must bind to a secret reference block rather than to a plain string.";

    /// <summary>Resolves one discovered block, discards the material, and describes the failure when there is one.</summary>
    /// <remarks>The material is erased immediately: startup proves the reference is reachable and each actual use resolves again, so nothing long-lived is kept.</remarks>
    private async Task<string?> ValidateAsync(DiscoveredSecret discovered, CancellationToken cancellationToken)
    {
        var result = await this.secretReferenceResolver.ResolveAsync(
            discovered.Secret.SecretReference,
            cancellationToken);

        if (result.Secret is not { } material)
        {
            return $"{discovered.ConfigurationPath} — the secret reference could not be resolved [{result.Failure}].";
        }

        material.Dispose();

        if (result.Source == SecretMaterialSource.InlineValue)
        {
            this.LogSettingResolvedInline(discovered.ConfigurationPath);
        }

        return null;
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Secret-bearing configuration values are interpreted as {Interpretation}.")]
    private partial void LogActiveInterpretation(SecretValueInterpretation interpretation);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Configuration setting {ConfigurationPath} resolved to an inline secret value rather than to a reference; inline material cannot be erased from process memory.")]
    private partial void LogSettingResolvedInline(string configurationPath);
}
