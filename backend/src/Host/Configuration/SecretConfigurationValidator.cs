// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Common.ClientAssertions;
using MailFathom.Common.OAuth;
using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Configuration.DataEncryption;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Persistence;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.DataEncryption;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Secrets;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.Secrets.References;
using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.Infrastructure.Secrets.Sources;
using MailFathom.Infrastructure.Security.OAuth;

namespace MailFathom.Host.Configuration;

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
    private const string MailSynchronizationConfigurationPath = MailSynchronizationOptions.SectionName;

    private const string PersistenceConfigurationPath = PersistenceOptions.SectionName;

    private const string DataEncryptionConfigurationPath = DataEncryptionOptions.SectionName;

    private readonly TimeProvider timeProvider;

    private readonly ISecretReferenceResolver secretReferenceResolver;
    private readonly TrustAnchorLoader trustAnchorLoader;
    private readonly DatabaseConnectionSettingsMapper connectionSettingsMapper;
    private readonly IDatabaseConnectionSettingsValidator connectionSettingsValidator;
    private readonly PostgresTextSearchConfiguration schemaTextSearchConfiguration;
    private readonly DatabaseCommandTimeout composedCommandTimeout;
    private readonly ILogger<SecretConfigurationValidator> logger;

    /// <summary>Initializes a new secret configuration validator.</summary>
    /// <remarks>
    /// The text search configuration and the command timeout both arrive as the values composition actually used, not
    /// as settings to be read again, because that is exactly what a reloaded candidate has to be compared against.
    /// </remarks>
    public SecretConfigurationValidator(
        ISecretReferenceResolver secretReferenceResolver,
        TrustAnchorLoader trustAnchorLoader,
        DatabaseConnectionSettingsMapper connectionSettingsMapper,
        IDatabaseConnectionSettingsValidator connectionSettingsValidator,
        PostgresTextSearchConfiguration schemaTextSearchConfiguration,
        DatabaseCommandTimeout composedCommandTimeout,
        TimeProvider timeProvider,
        ILogger<SecretConfigurationValidator> logger)
    {
        this.secretReferenceResolver = secretReferenceResolver;
        this.trustAnchorLoader = trustAnchorLoader;
        this.connectionSettingsMapper = connectionSettingsMapper;
        this.connectionSettingsValidator = connectionSettingsValidator;
        this.schemaTextSearchConfiguration = schemaTextSearchConfiguration;
        this.composedCommandTimeout = composedCommandTimeout;
        this.timeProvider = timeProvider;
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

        errors.AddRange(this.FindCommandTimeoutErrors(candidate));

        var connectionFailures = await this.connectionSettingsValidator.FindConfigurationFailuresAsync(
            this.connectionSettingsMapper.Map(candidate),
            cancellationToken);

        errors.AddRange(connectionFailures.Select(DescribeConnectionFailure));

        return errors;
    }

    /// <summary>Finds everything an operator must fix before a data-encryption key ring can be used.</summary>
    /// <param name="candidate">The bound snapshot, which may be the startup one or a reloaded one.</param>
    /// <param name="cancellationToken">Cancels the resolution.</param>
    /// <returns>One message per unusable setting, empty when the ring is usable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="candidate" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Resolving the references is not enough, for the reason a trust anchor is loaded rather than merely resolved:
    /// material that resolves but is not a key would pass a reference check and then fail every read of a sealed value,
    /// which is exactly the failure this section exists to move to startup. The structural rules — a duplicate
    /// identifier, an active key naming nothing — are answered by the options type itself, because they need no
    /// resolution and reporting them here would leave a malformed ring judged twice.
    /// </remarks>
    internal async Task<IReadOnlyList<string>> FindDataEncryptionConfigurationErrorsAsync(
        DataEncryptionOptions candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var errors = new List<string>(
            await this.FindSecretReferenceErrorsAsync(DataEncryptionConfigurationPath, candidate, cancellationToken));

        errors.AddRange(await this.FindKeyMaterialErrorsAsync(candidate, cancellationToken));

        return errors;
    }

    /// <summary>Decodes every configured key and reports the material that is not one, discarding each key once it has proven usable.</summary>
    /// <remarks>
    /// Neither the material nor its length reaches the report. A message naming the length of a rejected key would tell
    /// anyone reading the log how much of it to guess, which is why the failure vocabulary is closed and the remedy is
    /// stated instead.
    /// </remarks>
    private async Task<IReadOnlyList<string>> FindKeyMaterialErrorsAsync(
        DataEncryptionOptions candidate,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        // The loop stays because each step awaits a resolution, and the position is part of the reported path.
        foreach (var (position, configuredKey) in candidate.Keys.Index())
        {
            // A key configuring no material at all is reported by the options type, which needs no resolution to see it.
            if (configuredKey.Material is not { } material)
            {
                continue;
            }

            var resolution = await this.secretReferenceResolver.ResolveAsync(material.SecretReference, cancellationToken);

            // A reference that does not resolve is already reported by the reference check every section runs.
            if (resolution.Secret is not { } resolvedMaterial)
            {
                continue;
            }

            using (resolvedMaterial)
            {
                using var key = DataEncryptionKey.Decode(configuredKey.KeyId, resolvedMaterial, out var failure);

                if (key is null)
                {
                    errors.Add(
                        $"{DataEncryptionConfigurationPath}:{nameof(DataEncryptionOptions.Keys)}:{position}:{nameof(DataEncryptionKeyOptions.Material)} — the material is not an AES-256 key [{failure}]. Generate one with 'openssl rand -base64 32'.");
                }
            }
        }

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
            yield return $"{PersistenceConfigurationPath}:{nameof(PersistenceOptions.TextSearchConfiguration)} — '{candidate.TextSearchConfiguration}' is not a PostgreSQL text search configuration MailFathom supports.";

            yield break;
        }

        if (!string.Equals(candidate.TextSearchConfiguration, this.schemaTextSearchConfiguration.Value, StringComparison.Ordinal))
        {
            yield return $"{PersistenceConfigurationPath}:{nameof(PersistenceOptions.TextSearchConfiguration)} — the lexical index was built with '{this.schemaTextSearchConfiguration.Value}'; changing it needs a schema change and a restart rather than a configuration reload.";
        }
    }

    /// <summary>Refuses a reloaded command timeout instead of adopting one that could not take effect.</summary>
    /// <remarks>
    /// The timeout is written into the EF Core context options during composition and nothing reapplies it afterwards,
    /// so a reloaded value would be published as adopted while every database command kept the bound the process
    /// started with. That gap is worse than refusing the change: an operator who raised the timeout to stop a report
    /// timing out would see the setting take and the timeouts continue. It is restart-required for the same reason the
    /// text search configuration and the credential source are.
    /// </remarks>
    private IEnumerable<string> FindCommandTimeoutErrors(PersistenceOptions candidate)
    {
        if (candidate.CommandTimeoutSeconds != (int)this.composedCommandTimeout.Value.TotalSeconds)
        {
            yield return $"{PersistenceConfigurationPath}:{nameof(PersistenceOptions.CommandTimeoutSeconds)} — database commands were composed with a {(int)this.composedCommandTimeout.Value.TotalSeconds}s timeout; changing it needs a restart rather than a configuration reload.";
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

    /// <summary>Finds everything an operator must fix before the MCP endpoint's secrets can be used.</summary>
    /// <param name="candidate">The bound endpoint settings, which are read once during composition.</param>
    /// <param name="cancellationToken">Cancels the resolution.</param>
    /// <returns>One message per unusable setting, empty when the section's secrets are all usable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="candidate" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A disabled endpoint configures no keys worth proving, and validating them anyway would fail a host over a
    /// credential nothing was going to read. The structural rules of the section are its own and run during
    /// composition; this covers the secrets it carries, on exactly the terms every other section's secrets are covered.
    /// </remarks>
    internal async Task<IReadOnlyList<string>> FindMcpEndpointConfigurationErrorsAsync(
        McpEndpointOptions candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (!candidate.Enabled)
        {
            return [];
        }

        var errors = new List<string>(
            await this.FindSecretReferenceErrorsAsync(McpEndpointOptions.SectionName, candidate, cancellationToken));

        errors.AddRange(await this.FindClientCertificateTrustAnchorErrorsAsync(candidate, cancellationToken));
        errors.AddRange(await this.FindUnreachableApiKeyErrorsAsync(candidate, cancellationToken));
        errors.AddRange(await this.FindClientPublicKeyErrorsAsync(
            McpEndpointOptions.SectionName,
            candidate.Authentication,
            cancellationToken));

        return errors;
    }

    /// <summary>Finds everything an operator must fix before the administrative endpoint's secrets can be used.</summary>
    /// <param name="candidate">The bound endpoint settings, which are read once during composition.</param>
    /// <param name="cancellationToken">Cancels the resolution.</param>
    /// <returns>One message per unusable setting, empty when the section's secrets are all usable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="candidate" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A disabled endpoint configures no credentials worth proving, and validating them anyway would fail a host over
    /// one nothing was going to read. The structural rules of the section are its own and run during composition; this
    /// covers the secrets it carries, on exactly the terms every other section's secrets are covered.
    /// </remarks>
    internal async Task<IReadOnlyList<string>> FindAdminEndpointConfigurationErrorsAsync(
        AdminEndpointOptions candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (!candidate.Enabled)
        {
            return [];
        }

        var errors = new List<string>(
            await this.FindSecretReferenceErrorsAsync(AdminEndpointOptions.SectionName, candidate, cancellationToken));

        errors.AddRange(await this.FindClientPublicKeyErrorsAsync(
            AdminEndpointOptions.SectionName,
            candidate.Authentication,
            cancellationToken));

        return errors;
    }

    /// <summary>Finds everything an operator must fix before the client endpoint's secrets can be used.</summary>
    /// <param name="candidate">The bound endpoint settings, which are read once during composition.</param>
    /// <param name="cancellationToken">Cancels the resolution.</param>
    /// <returns>One message per unusable setting, empty when the section's secrets are all usable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="candidate" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A disabled endpoint configures no credentials worth proving, and validating them anyway would fail a host over
    /// one nothing was going to read. The structural rules of the section are its own and run during composition; this
    /// covers the secrets it carries, on exactly the terms every other section's secrets are covered.
    /// </remarks>
    internal async Task<IReadOnlyList<string>> FindClientEndpointConfigurationErrorsAsync(
        ClientEndpointOptions candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (!candidate.Enabled)
        {
            return [];
        }

        var errors = new List<string>(
            await this.FindSecretReferenceErrorsAsync(ClientEndpointOptions.SectionName, candidate, cancellationToken));

        errors.AddRange(await this.FindClientPublicKeyErrorsAsync(
            ClientEndpointOptions.SectionName,
            candidate.Authentication,
            cancellationToken));

        return errors;
    }

    /// <summary>Reads every configured client public key and reports the material no assertion could be verified against.</summary>
    /// <remarks>
    /// <para>
    /// Resolving the reference is not enough, for the reason a trust anchor is loaded rather than merely resolved:
    /// material that resolves but is not a public key would pass a reference check and then refuse every client the
    /// entry exists to serve, which is exactly the failure startup validation exists to move forward.
    /// </para>
    /// <para>
    /// One of the faults is worth more than an operator's time. Material carrying a private key parses cleanly and would
    /// verify signatures correctly, so nothing about a running deployment would ever report it — while the host held the
    /// one thing key-pair authentication exists to keep off it. That is the case this refusal is written for, and it is
    /// named separately from every other kind of unusable material.
    /// </para>
    /// <para>
    /// The key is named by its configuration position and by nothing else. The material never appears, and neither does
    /// the name the operator gave it, because a message that named a key would be a message an unusable configuration
    /// prints about a credential.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<string>> FindClientPublicKeyErrorsAsync(
        string sectionName,
        IList<TransportAuthenticationOptions> authentication,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        // The loop stays because each step awaits a retrieval, and the position is part of the reported path.
        foreach (var (entryIndex, configuredKey) in authentication
            .Index()
            .Where(entry => entry.Item.PublicKey is not null)
            .Select(entry => (entry.Index, Key: entry.Item.PublicKey!)))
        {
            var configurationPath =
                $"{sectionName}:{TransportAuthenticationConfiguration.SettingName}:{entryIndex}:{nameof(TransportAuthenticationOptions.PublicKey)}";

            var resolution = await this.secretReferenceResolver.ResolveAsync(
                configuredKey.SecretReference,
                cancellationToken);

            // A reference that does not resolve is already reported by the reference check, which every section runs.
            if (resolution.Secret is not { } material)
            {
                continue;
            }

            using (material)
            {
                if (DescribeUnusablePublicKey(material) is { } fault)
                {
                    errors.Add($"{configurationPath} — {fault}");
                }
            }
        }

        return errors;
    }

    /// <summary>Reports what is wrong with resolved public key material, or nothing when it is usable.</summary>
    /// <remarks>The material is revealed into a pinned buffer and cleared here, on the same terms as every other reading of provisioned material in this process — which matters more than usual for the one case where what was provisioned turns out to be a private key.</remarks>
    private static string? DescribeUnusablePublicKey(ResolvedSecret material)
    {
        var revealedText = GC.AllocateArray<char>(material.TextLength, pinned: true);

        try
        {
            material.RevealTextInto(revealedText);

            using var publicKey = ClientAssertionKeyMaterial.ReadPublicKey(revealedText, out var fault);

            return publicKey is null ? DescribeKeyFault(fault) : null;
        }
        finally
        {
            revealedText.AsSpan().Clear();
        }
    }

    private static string DescribeKeyFault(ClientAssertionKeyFault fault) => fault switch
    {
        ClientAssertionKeyFault.WrongHalf =>
            "the material is a private key. This setting registers the public half of a client's key pair, and holding the private half is exactly what this deployment must not do; write the output of 'openssl pkey -in <key> -pubout' and keep the key itself on the client.",
        ClientAssertionKeyFault.ModulusTooShort =>
            $"the material is an RSA public key shorter than {ClientAssertionKeyMaterial.ShortestRsaModulusInBits} bits, which is not a signature this deployment accepts; generate the client a new key pair.",
        ClientAssertionKeyFault.UnsupportedAlgorithm =>
            "the material is a public key of a kind no permitted signature algorithm covers; generate the client an RSA or an elliptic-curve key pair over P-256, P-384, or P-521.",
        _ =>
            "the material is not a PEM public key; write the output of 'openssl pkey -in <key> -pubout', including its BEGIN and END lines.",
    };

    /// <summary>Reports every configured API key that no request could ever authenticate with.</summary>
    /// <remarks>
    /// <para>
    /// Both credentials arrive as a bearer credential, and the endpoint tells them apart by shape: a credential that is
    /// a JSON Web Token naming a configured authorization server reaches that server's token validator, and everything
    /// else reaches the API key comparison. A configured key that happens to have that shape therefore never reaches
    /// the comparison it exists for, and no client can authenticate with it however correctly it is presented.
    /// </para>
    /// <para>
    /// Only the overlap is refused rather than every token-shaped key, because the shape alone decides nothing: a key
    /// naming an issuer this deployment does not configure selects no validator and is compared like any other opaque
    /// credential. What makes the reported case unusable is that the deployment configured both sides of it.
    /// </para>
    /// <para>
    /// The key is named by its configuration position and by nothing else. Neither the material nor the issuer it names
    /// appears, because a key is a credential and the issuer was read out of one.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<string>> FindUnreachableApiKeyErrorsAsync(
        McpEndpointOptions candidate,
        CancellationToken cancellationToken)
    {
        if (!candidate.AllowsApiKey || !candidate.AllowsOAuth)
        {
            return [];
        }

        // Composed from the profiles that are well formed rather than from all of them, because this runs beside the
        // structural rules rather than after them: a malformed issuer is already being reported by its own check, and
        // asking it for a validated value here would raise instead of adding to that report.
        var configuredIssuers = candidate.OAuthMethods()
            .SelectMany(oauthMethod => oauthMethod.AuthorizationServers)
            .Where(authorizationServer => OAuthIdentifierUri.IsWellFormed(authorizationServer.Issuer))
            .Select(authorizationServer => authorizationServer.ValidatedIssuer())
            .ToHashSet(StringComparer.Ordinal);

        var errors = new List<string>();

        // The loop stays because each step awaits a retrieval, and the position is part of the reported path.
        foreach (var (entryIndex, configuredKey) in candidate.Authentication
            .Index()
            .Where(entry => entry.Item.ApiKey is not null)
            .Select(entry => (entry.Index, Key: entry.Item.ApiKey!)))
        {
            var resolution = await this.secretReferenceResolver.ResolveAsync(
                configuredKey.SecretReference,
                cancellationToken);

            // A reference that does not resolve is already reported by the reference check, which every section runs.
            if (resolution.Secret is not { } material)
            {
                continue;
            }

            using (material)
            {
                if (NamesAConfiguredIssuer(material, configuredIssuers))
                {
                    errors.Add(
                        $"{McpEndpointOptions.SectionName}:{TransportAuthenticationConfiguration.SettingName}:{entryIndex}:{nameof(TransportAuthenticationOptions.ApiKey)} — this key is a JSON Web Token naming one of the configured authorization servers, so every request presenting it is judged as an access token by that server and the key itself is never compared; issue an opaque key instead.");
                }
            }
        }

        return errors;
    }

    /// <summary>Reports whether resolved key material is a token naming one of the configured authorization servers.</summary>
    /// <remarks>The material is revealed into a pinned buffer and cleared here, on the same terms as every other reading of a secret in this process.</remarks>
    private static bool NamesAConfiguredIssuer(ResolvedSecret material, HashSet<string> configuredIssuers)
    {
        var revealedText = GC.AllocateArray<char>(material.TextLength, pinned: true);

        try
        {
            material.RevealTextInto(revealedText);

            return UnverifiedJsonWebToken.TryReadClaimedIssuer(revealedText, out var claimedIssuer)
                && configuredIssuers.Contains(claimedIssuer);
        }
        finally
        {
            revealedText.AsSpan().Clear();
        }
    }

    /// <summary>Loads every trust anchor a client certificate profile names and reports the ones no request could use.</summary>
    /// <remarks>
    /// Resolving the reference is not enough, for the reason the mail anchors are loaded too: material that resolves but
    /// does not parse as a certificate would pass a reference check and then refuse every client the profile exists to
    /// serve. Each anchor is discarded once it has proven loadable, because a request loads its own.
    /// </remarks>
    private async Task<IReadOnlyList<string>> FindClientCertificateTrustAnchorErrorsAsync(
        McpEndpointOptions candidate,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        // The positions are part of the reported configuration path, so they come from Index rather than from counters
        // the loop bodies could forget to advance. The loops themselves stay, because each step awaits a retrieval.
        foreach (var (profileIndex, profile) in candidate.ClientCertificateProfiles.Index())
        {
            foreach (var (anchorIndex, configuredAnchor) in profile.TrustAnchors.Index())
            {
                var configurationPath =
                    $"{McpEndpointOptions.SectionName}:{nameof(McpEndpointOptions.ClientCertificateProfiles)}:{profileIndex}:{nameof(McpClientCertificateProfileOptions.TrustAnchors)}:{anchorIndex}";

                using var loadResult = await this.trustAnchorLoader.LoadAsync(configuredAnchor, cancellationToken);

                if (loadResult.TrustAnchor is { } trustAnchor)
                {
                    this.LogTrustAnchorLoaded(configurationPath, trustAnchor.Subject, trustAnchor.Thumbprint);
                }
                else
                {
                    errors.Add($"{configurationPath} — the trust anchor material could not be loaded [{loadResult.Failure}].");
                }
            }
        }

        return errors;
    }

    /// <summary>Resolves every secret reference in a bound options graph and reports the ones an operator must fix.</summary>
    /// <param name="rootConfigurationPath">The configuration path of the bound root, which prefixes every reported path.</param>
    /// <param name="boundOptions">The bound options root.</param>
    /// <param name="cancellationToken">Cancels the resolution.</param>
    /// <returns>One message per faulty declaration, per unresolvable reference, and per plain string setting that names a secret.</returns>
    /// <remarks>
    /// The material is erased immediately: this proves the reference is reachable, and each actual use resolves again.
    /// A secret whose lifetime has already ended is reported to the log rather than refused, because an expired entry
    /// left beside its replacement is what a completed rotation looks like and failing a host over it would make
    /// rotating one harder than not rotating at all.
    /// </remarks>
    internal async Task<IReadOnlyList<string>> FindSecretReferenceErrorsAsync(
        string rootConfigurationPath,
        object boundOptions,
        CancellationToken cancellationToken)
    {
        var discovered = ConfiguredSecretDiscovery.FindSecretBearingSettings(boundOptions, rootConfigurationPath);
        var errors = new List<string>(discovered.RawSecretPropertyPaths.Select(DescribeRawSecretProperty));

        errors.AddRange(discovered.FindDeclarationErrors().Select(DescribeDeclarationError));

        var now = this.timeProvider.GetUtcNow();

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

            this.ReportExpiredLifetime(block, now);
        }

        return errors;
    }

    /// <summary>Records a secret whose configured lifetime has already ended.</summary>
    /// <remarks>
    /// <para>
    /// The name and the path are both operator-chosen configuration identities and carry no material, which is what
    /// makes this line safe to write at all.
    /// </para>
    /// <para>
    /// The name is written only once <see cref="SecretName" /> has accepted it. That type exists to keep a configured
    /// value safe to place in a log line unescaped, and a name carrying a newline would otherwise forge a second line
    /// here — in a run that reports the malformed declaration and fails anyway, so nothing is lost by staying silent.
    /// </para>
    /// </remarks>
    private void ReportExpiredLifetime(DiscoveredSecret block, DateTimeOffset now)
    {
        if (!SecretName.TryCreate(block.Secret.Name, out var secretName))
        {
            return;
        }

        if (SecretLifetime.TryParse(block.Secret.Lifetime, out var lifetime) && lifetime.HasExpiredAt(now))
        {
            this.LogSecretExpired(block.ConfigurationPath, secretName.Value!, lifetime.ToString());
        }
    }

    private static string DescribeConnectionFailure(DatabaseConnectionConfigurationFailure failure) =>
        $"{PersistenceConfigurationPath} — the database connection settings cannot be used [{failure}].";

    private static string DescribeRawSecretProperty(string configurationPath) =>
        $"{configurationPath} — a setting that names a secret must bind to a secret reference block rather than to a plain string.";

    private static string DescribeDeclarationError(SecretDeclarationError error) => error.Failure switch
    {
        SecretDeclarationFailure.NameMissing =>
            $"{error.ConfigurationPath}:{nameof(ConfiguredSecret.Name)} — every secret needs a name, which is the identity a rotation, an expiry, and an audit record name it by.",
        SecretDeclarationFailure.NameMalformed =>
            $"{error.ConfigurationPath}:{nameof(ConfiguredSecret.Name)} — a name may carry up to {SecretName.MaximumLength} letters, digits, dots, dashes, and underscores, and must begin with a letter or a digit.",
        SecretDeclarationFailure.NameDuplicated =>
            $"{error.ConfigurationPath}:{nameof(ConfiguredSecret.Name)} — another secret in this section already carries this name, so neither could be named unambiguously.",
        SecretDeclarationFailure.LifetimeMissing =>
            $"{error.ConfigurationPath}:{nameof(ConfiguredSecret.Lifetime)} — a blank lifetime states nothing; write '{SecretLifetime.NoLimitValue}' or the instant the secret expires.",
        _ =>
            $"{error.ConfigurationPath}:{nameof(ConfiguredSecret.Lifetime)} — write '{SecretLifetime.NoLimitValue}' or an ISO 8601 instant carrying an explicit offset, such as '2027-01-31T00:00:00Z'.",
    };

    /// <summary>Loads every configured trust anchor and reports the ones no connection could use.</summary>
    /// <remarks>
    /// The anchor is discarded once it has proven loadable, because each connection attempt loads its own. A loaded
    /// anchor is logged by subject and thumbprint, which is public certificate material and the detail an operator
    /// needs to confirm that the authority MailFathom trusts is the one they provisioned.
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
        Level = LogLevel.Warning,
        Message = "Configuration setting {ConfigurationPath} carries the secret {SecretName}, whose configured lifetime ended at {Expiration}. Consumers that enforce a lifetime refuse it; the rest keep using it, so remove or re-date it once its replacement is in place.")]
    private partial void LogSecretExpired(string configurationPath, string secretName, string expiration);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Configuration setting {ConfigurationPath} trusts the certificate authority {TrustAnchorSubject} with thumbprint {TrustAnchorThumbprint}.")]
    private partial void LogTrustAnchorLoaded(
        string configurationPath,
        string trustAnchorSubject,
        string trustAnchorThumbprint);
}
