// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Infrastructure.Certificates;
using MailMcp.Infrastructure.Mail;
using MailMcp.Infrastructure.Secrets;

namespace MailMcp.Host.Configuration;

/// <summary>Proves that every secret-bearing setting of a configuration snapshot can actually be used.</summary>
/// <remarks>
/// <para>
/// The same check runs at startup and before a reloaded snapshot is published, because a reference that resolves at
/// startup says nothing about one an operator edits later. Resolving is not enough on its own: material that resolves
/// but does not load as a certificate would pass a reference check and then fail every connection, so the trust anchor
/// is loaded here too.
/// </para>
/// <para>
/// Every reported error names the configuration path and a stable failure identity. The reference target, the
/// environment variable's value, the material, and a bundle password never appear, because this output reaches
/// operator consoles and logs.
/// </para>
/// </remarks>
internal sealed partial class SecretConfigurationValidator
{
    private const string MailSynchronizationConfigurationPath = "MailSynchronization";

    private readonly ISecretReferenceResolver secretReferenceResolver;
    private readonly TrustAnchorLoader trustAnchorLoader;
    private readonly ILogger<SecretConfigurationValidator> logger;

    /// <summary>Initializes a new secret configuration validator.</summary>
    public SecretConfigurationValidator(
        ISecretReferenceResolver secretReferenceResolver,
        TrustAnchorLoader trustAnchorLoader,
        ILogger<SecretConfigurationValidator> logger)
    {
        this.secretReferenceResolver = secretReferenceResolver;
        this.trustAnchorLoader = trustAnchorLoader;
        this.logger = logger;
    }

    /// <summary>Finds everything an operator must fix before a mail synchronization snapshot can be used.</summary>
    /// <param name="candidate">The bound snapshot, which may be the startup one or a reloaded one.</param>
    /// <param name="cancellationToken">Cancels the resolution and the certificate loading.</param>
    /// <returns>One message per unusable setting, empty when the snapshot is usable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="candidate" /> is <see langword="null" />.</exception>
    internal async Task<IReadOnlyList<string>> FindMailConfigurationErrorsAsync(
        MailSynchronizationOptions candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var errors = new List<string>(
            await this.FindSecretReferenceErrorsAsync(MailSynchronizationConfigurationPath, candidate, cancellationToken));

        errors.AddRange(await this.FindTrustAnchorErrorsAsync(candidate, cancellationToken));

        return errors;
    }

    /// <summary>Resolves every secret reference in a bound options graph and reports the ones that produced no material.</summary>
    /// <param name="rootConfigurationPath">The configuration path of the bound root, which prefixes every reported path.</param>
    /// <param name="boundOptions">The bound options root.</param>
    /// <param name="cancellationToken">Cancels the resolution.</param>
    /// <returns>One message per unresolvable reference and per plain string setting that names a secret.</returns>
    /// <remarks>The material is erased immediately: this proves the reference is reachable, and each actual use resolves again.</remarks>
    internal async Task<IReadOnlyList<string>> FindSecretReferenceErrorsAsync(
        string rootConfigurationPath,
        object boundOptions,
        CancellationToken cancellationToken)
    {
        var discovered = ConfiguredSecretDiscovery.FindSecretBearingSettings(boundOptions, rootConfigurationPath);
        var errors = new List<string>(discovered.RawSecretPropertyPaths.Select(DescribeRawSecretProperty));

        foreach (var block in discovered.Blocks)
        {
            var result = await this.secretReferenceResolver.ResolveAsync(
                block.Secret.SecretReference,
                cancellationToken);

            if (result.Secret is not { } material)
            {
                errors.Add($"{block.ConfigurationPath} — the secret reference could not be resolved [{result.Failure}].");

                continue;
            }

            material.Dispose();

            if (result.Source == SecretMaterialSource.InlineValue)
            {
                this.LogSettingResolvedInline(block.ConfigurationPath);
            }
        }

        return errors;
    }

    private static string DescribeRawSecretProperty(string configurationPath) =>
        $"{configurationPath} — a setting that names a secret must bind to a secret reference block rather than to a plain string.";

    /// <summary>Loads every configured trust anchor and reports the ones no connection could use.</summary>
    /// <remarks>
    /// The anchor is discarded once it has proven loadable, because each connection attempt loads its own. A loaded
    /// anchor is logged by subject and thumbprint, which is public certificate material and the detail an operator
    /// needs to confirm that the authority MailMcp trusts is the one they provisioned.
    /// </remarks>
    private async Task<IReadOnlyList<string>> FindTrustAnchorErrorsAsync(
        MailSynchronizationOptions candidate,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        for (var accountIndex = 0; accountIndex < candidate.Accounts.Count; accountIndex++)
        {
            var account = candidate.Accounts[accountIndex];
            var configurationPath =
                $"{MailSynchronizationConfigurationPath}:{nameof(MailSynchronizationOptions.Accounts)}:{accountIndex}:{nameof(MailSynchronizationAccountOptions.TransportSecurity)}:{nameof(MailAccountTransportSecurityOptions.TrustedCertificateAuthority)}";

            using var loadResult = await account.TransportSecurity.LoadTrustedCertificateAuthorityAsync(
                this.trustAnchorLoader,
                cancellationToken);

            if (loadResult is null)
            {
                continue;
            }

            if (loadResult.TrustAnchor is { } trustAnchor)
            {
                this.LogTrustAnchorLoaded(configurationPath, trustAnchor.Subject, trustAnchor.Thumbprint);
            }
            else
            {
                errors.Add($"{configurationPath} — the trusted certificate authority material could not be loaded [{loadResult.Failure}].");
            }
        }

        return errors;
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Configuration setting {ConfigurationPath} resolved to an inline secret value rather than to a reference; inline material cannot be erased from process memory.")]
    private partial void LogSettingResolvedInline(string configurationPath);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Configuration setting {ConfigurationPath} trusts the certificate authority {TrustAnchorSubject} with thumbprint {TrustAnchorThumbprint}.")]
    private partial void LogTrustAnchorLoaded(
        string configurationPath,
        string trustAnchorSubject,
        string trustAnchorThumbprint);
}
