// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Infrastructure.Certificates;
using MailMcp.Infrastructure.Mail;
using MailMcp.Infrastructure.Persistence;
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

    private const string PersistenceConfigurationPath = "Persistence";

    private readonly ISecretReferenceResolver secretReferenceResolver;
    private readonly TrustAnchorLoader trustAnchorLoader;
    private readonly DatabaseConnectionSettingsMapper connectionSettingsMapper;
    private readonly IDatabaseConnectionSettingsValidator connectionSettingsValidator;
    private readonly PostgresTextSearchConfiguration schemaTextSearchConfiguration;
    private readonly ILogger<SecretConfigurationValidator> logger;

    /// <summary>Initializes a new secret configuration validator.</summary>
    /// <remarks>
    /// The text search configuration arrives as the value the EF Core model was actually built from, not as a setting
    /// to be read again, because that is exactly what a reloaded candidate has to be compared against.
    /// </remarks>
    public SecretConfigurationValidator(
        ISecretReferenceResolver secretReferenceResolver,
        TrustAnchorLoader trustAnchorLoader,
        DatabaseConnectionSettingsMapper connectionSettingsMapper,
        IDatabaseConnectionSettingsValidator connectionSettingsValidator,
        PostgresTextSearchConfiguration schemaTextSearchConfiguration,
        ILogger<SecretConfigurationValidator> logger)
    {
        this.secretReferenceResolver = secretReferenceResolver;
        this.trustAnchorLoader = trustAnchorLoader;
        this.connectionSettingsMapper = connectionSettingsMapper;
        this.connectionSettingsValidator = connectionSettingsValidator;
        this.schemaTextSearchConfiguration = schemaTextSearchConfiguration;
        this.logger = logger;
    }

    /// <summary>Finds everything an operator must fix before a persistence snapshot can be used.</summary>
    /// <param name="candidate">The bound snapshot, which may be the startup one or a reloaded one.</param>
    /// <param name="cancellationToken">Cancels the resolution and the connection-string check.</param>
    /// <returns>One message per unusable setting, empty when the snapshot is usable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="candidate" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Resolving the references is not enough: material behind <c>Persistence:ConnectionString</c> that resolves but
    /// does not parse would pass a reference check, replace the last known good settings, and then fail every
    /// connection opened afterwards. The database adapter answers that half, because only it knows which setting
    /// currently supplies the credential.
    /// </remarks>
    internal async Task<IReadOnlyList<string>> FindPersistenceConfigurationErrorsAsync(
        PersistenceOptions candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var errors = new List<string>(
            await this.FindSecretReferenceErrorsAsync(PersistenceConfigurationPath, candidate, cancellationToken));

        errors.AddRange(this.FindTextSearchConfigurationErrors(candidate));

        var connectionFailures = await this.connectionSettingsValidator.FindConfigurationFailuresAsync(
            this.connectionSettingsMapper.Map(candidate),
            cancellationToken);

        errors.AddRange(connectionFailures.Select(DescribeConnectionFailure));

        return errors;
    }

    /// <summary>Refuses a reloaded text search configuration instead of adopting one that could not take effect.</summary>
    /// <remarks>
    /// The configuration is compiled into the search vector's generated column, so the schema already holds the value
    /// the model was built from and every indexed row was written under it. Publishing a different one would change
    /// nothing about the index and everything about what an operator believes the index contains: queries would be
    /// stemmed one way and the stored lexemes another, which shows up as missing results rather than as an error.
    /// Changing it is a schema change that rebuilds the search documents, so it is restart-required and reported as
    /// such, exactly as changing which setting supplies the database credential is.
    /// </remarks>
    private IEnumerable<string> FindTextSearchConfigurationErrors(PersistenceOptions candidate)
    {
        if (!PostgresTextSearchConfiguration.IsSupported(candidate.TextSearchConfiguration))
        {
            yield return $"{PersistenceConfigurationPath}:{nameof(PersistenceOptions.TextSearchConfiguration)} — '{candidate.TextSearchConfiguration}' is not a PostgreSQL text search configuration MailMcp supports.";

            yield break;
        }

        if (!string.Equals(candidate.TextSearchConfiguration, this.schemaTextSearchConfiguration.Value, StringComparison.Ordinal))
        {
            yield return $"{PersistenceConfigurationPath}:{nameof(PersistenceOptions.TextSearchConfiguration)} — the lexical index was built with '{this.schemaTextSearchConfiguration.Value}'; changing it needs a schema change and a restart rather than a configuration reload.";
        }
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

    private static string DescribeConnectionFailure(DatabaseConnectionConfigurationFailure failure) =>
        $"{PersistenceConfigurationPath} — the database connection settings cannot be used [{failure}].";

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

        // The position is part of the reported configuration path, so it comes from Index rather than from a counter
        // the loop body could forget to advance. The loop itself stays, because each step awaits a retrieval.
        foreach (var (accountIndex, account) in candidate.Accounts.Index())
        {
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
