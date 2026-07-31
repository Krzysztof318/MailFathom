// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Buffers.Text;
using System.Text;
using MailFathom.Domain.Transport;
using MailFathom.Host.Configuration;
using MailFathom.Host.Hosting;
using MailFathom.Infrastructure;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Secrets;
using MailFathom.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Host.UnitTests;

public sealed class SecretConfigurationStartupValidatorTests
{
    private const string WorkforceIssuer = "https://sso.example.test/realms/mailfathom";

    private static readonly DateTimeOffset ValidatedAt = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    private static readonly DatabaseCommandTimeout DefaultCommandTimeout =
        new(TimeSpan.FromSeconds(HostApplicationBuilderExtensions.DefaultDatabaseCommandTimeoutSeconds));

    [Fact]
    public async Task StartingAsync_EveryReferenceResolvable_CompletesSoHostedServicesMayStart()
    {
        // Arrange
        var harness = CreateHarness(
            ConfiguredAccounts.WithPasswordReferences(("primary", "plaintext:dev-password")),
            new PersistenceOptions { Password = new ConfiguredSecret { Name = "postgres", SecretReference = "plaintext:postgres-password" } });

        // Act, Assert
        await harness.Validator.StartingAsync(CancellationToken.None);
        Assert.DoesNotContain(harness.ReportedMessages, message => message.Contains("could not be resolved", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartingAsync_NoSecretConfigured_CompletesBecauseNothingWasDiscovered()
    {
        // Arrange
        var harness = CreateHarness(new MailSynchronizationOptions(), new PersistenceOptions());

        // Act, Assert
        await harness.Validator.StartingAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartingAsync_UnresolvableReference_FailsStartupNamingTheConfigurationPath()
    {
        // Arrange
        var harness = CreateHarness(
            ConfiguredAccounts.WithPasswordReferences(("primary", "file:/run/secrets/absent")),
            new PersistenceOptions());

        // Act
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            harness.Validator.StartingAsync(CancellationToken.None));

        // Assert
        var failure = Assert.Single(exception.Failures);
        Assert.StartsWith("MailSynchronization:Accounts:0:Secrets:Password", failure, StringComparison.Ordinal);
        Assert.Contains(nameof(SecretResolutionFailure.MaterialNotFound), failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartingAsync_SeveralUnresolvableReferences_ReportsThemAllAtOnce()
    {
        // Arrange
        var harness = CreateHarness(
            ConfiguredAccounts.WithPasswordReferences(
                ("primary", "file:/run/secrets/absent"),
                ("secondary", "a-pasted-password")),
            new PersistenceOptions { Password = new ConfiguredSecret { Name = "postgres", SecretReference = "file:/run/secrets/postgres" } });

        // Act
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            harness.Validator.StartingAsync(CancellationToken.None));

        // Assert
        Assert.Equal(
            [
                "MailSynchronization:Accounts:0:Secrets:Password",
                "MailSynchronization:Accounts:1:Secrets:Password",
                "Persistence:Password",
            ],
            exception.Failures.Select(failure => failure.Split(' ', 2)[0]).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task StartingAsync_PlainTextValueUnderReferenceOnly_FailsInsteadOfAcceptingItAsTheSecret()
    {
        // Arrange
        var harness = CreateHarness(
            ConfiguredAccounts.WithPasswordReferences(("primary", "a-pasted-password")),
            new PersistenceOptions());

        // Act
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            harness.Validator.StartingAsync(CancellationToken.None));

        // Assert
        var failure = Assert.Single(exception.Failures);
        Assert.Contains(nameof(SecretResolutionFailure.SchemeMissing), failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartingAsync_EveryFailure_NamesNeitherTheReferenceTargetNorTheMaterial()
    {
        // Arrange
        var harness = CreateHarness(
            ConfiguredAccounts.WithPasswordReferences(("primary", "file:/run/secrets/imap-primary-password")),
            new PersistenceOptions());

        // Act
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            harness.Validator.StartingAsync(CancellationToken.None));

        // Assert
        var reported = string.Join(' ', exception.Failures.Concat(harness.ReportedMessages));
        Assert.DoesNotContain("/run/secrets", reported, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartingAsync_TrustAnchorBlock_IsDiscoveredAndResolvedLikeAnyOtherSecret()
    {
        // Arrange
        var synchronization = ConfiguredAccounts.WithPasswordReferences(("primary", "plaintext:dev-password"));
        synchronization.Accounts[0].TransportSecurity.TrustedCertificateAuthority =
            new ConfiguredSecret { Name = "primary-ca", SecretReference = "file:/run/secrets/private-ca.pem" };
        var harness = CreateHarness(synchronization, new PersistenceOptions());

        // Act
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            harness.Validator.StartingAsync(CancellationToken.None));

        // Assert
        var failure = Assert.Single(exception.Failures);
        Assert.StartsWith(
            "MailSynchronization:Accounts:0:TransportSecurity:TrustedCertificateAuthority",
            failure,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartingAsync_TrustAnchorMaterialThatIsNotACertificate_FailsStartupNamingTheLoadFailure()
    {
        // Arrange
        var synchronization = ConfiguredAccounts.WithPasswordReferences(("primary", "plaintext:dev-password"));
        synchronization.Accounts[0].TransportSecurity.CertificateTrust = MailServerCertificateTrust.AdditionalTrustedAuthority;
        synchronization.Accounts[0].TransportSecurity.TrustedCertificateAuthority =
            new ConfiguredSecret { Name = "primary-ca", SecretReference = "plaintext:not-a-certificate" };
        var harness = CreateHarness(synchronization, new PersistenceOptions());

        // Act
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            harness.Validator.StartingAsync(CancellationToken.None));

        // Assert
        var failure = Assert.Single(exception.Failures);
        Assert.StartsWith(
            "MailSynchronization:Accounts:0:TransportSecurity:TrustedCertificateAuthority",
            failure,
            StringComparison.Ordinal);
        Assert.Contains(nameof(CertificateMaterialFailure.EncodingNotRecognized), failure, StringComparison.Ordinal);
    }

    /// <summary>Material that resolves but is not a connection string would otherwise replace a working snapshot and then fail every connection.</summary>
    [Fact]
    public async Task StartingAsync_UnusableDatabaseConnectionSettings_FailsStartupNamingTheFailure()
    {
        // Arrange
        var databaseConnectionSettings = new StubDatabaseConnectionSettingsValidator
        {
            Failures = [DatabaseConnectionConfigurationFailure.ConnectionStringNotParsable],
        };
        var harness = CreateHarness(
            new MailSynchronizationOptions(),
            new PersistenceOptions(),
            databaseConnectionSettings: databaseConnectionSettings);

        // Act
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            harness.Validator.StartingAsync(CancellationToken.None));

        // Assert
        var failure = Assert.Single(exception.Failures);
        Assert.StartsWith("Persistence", failure, StringComparison.Ordinal);
        Assert.Contains(nameof(DatabaseConnectionConfigurationFailure.ConnectionStringNotParsable), failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartingAsync_InlineResolvedSetting_IsLoggedByNameAndNeverByValue()
    {
        // Arrange
        var harness = CreateHarness(
            ConfiguredAccounts.WithPasswordReferences(("primary", "plaintext:top-secret-password")),
            new PersistenceOptions(),
            SecretValueInterpretation.ReferenceOrInline,
            SecretMaterialSource.InlineValue);

        // Act
        await harness.Validator.StartingAsync(CancellationToken.None);

        // Assert
        Assert.Contains(harness.ReportedMessages, message => message.Contains("MailSynchronization:Accounts:0:Secrets:Password", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.ReportedMessages, message => message.Contains("top-secret-password", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartingAsync_ReferenceResolvedThroughAnAdapter_LogsNoInlineWarning()
    {
        // Arrange
        var harness = CreateHarness(
            ConfiguredAccounts.WithPasswordReferences(("primary", "plaintext:dev-password")),
            new PersistenceOptions());

        // Act
        await harness.Validator.StartingAsync(CancellationToken.None);

        // Assert
        Assert.DoesNotContain(harness.ReportedMessages, message => message.Contains("inline secret value", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartingAsync_Always_LogsTheActiveInterpretationMode()
    {
        // Arrange
        var harness = CreateHarness(new MailSynchronizationOptions(), new PersistenceOptions());

        // Act
        await harness.Validator.StartingAsync(CancellationToken.None);

        // Assert
        Assert.Contains(harness.ReportedMessages, message => message.Contains(nameof(SecretValueInterpretation.ReferenceOnly), StringComparison.Ordinal));
    }

    /// <summary>
    /// The configuration is compiled into the search vector's generated column, so a snapshot naming another one
    /// describes an index that does not exist. Adopting it would leave queries stemmed one way and the stored lexemes
    /// another, which surfaces as missing results rather than as an error.
    /// </summary>
    [Fact]
    public async Task StartingAsync_TextSearchConfigurationOtherThanTheOneTheIndexWasBuiltWith_IsRefused()
    {
        // Arrange
        var harness = CreateHarness(
            new MailSynchronizationOptions(),
            new PersistenceOptions { TextSearchConfiguration = "english" });

        // Act
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            harness.Validator.StartingAsync(CancellationToken.None));

        // Assert
        var failure = Assert.Single(exception.Failures);
        Assert.StartsWith("Persistence:TextSearchConfiguration", failure, StringComparison.Ordinal);
        Assert.Contains("needs a schema change and a restart", failure, StringComparison.Ordinal);
    }

    /// <summary>
    /// The timeout is written into the EF Core context options during composition and nothing reapplies it, so a
    /// reloaded value would be reported as adopted while every command kept the bound the process started with.
    /// </summary>
    [Fact]
    public async Task StartingAsync_CommandTimeoutOtherThanTheOneCompositionUsed_IsRefused()
    {
        // Arrange
        var harness = CreateHarness(
            new MailSynchronizationOptions(),
            new PersistenceOptions { CommandTimeoutSeconds = HostApplicationBuilderExtensions.DefaultDatabaseCommandTimeoutSeconds + 1 });

        // Act
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            harness.Validator.StartingAsync(CancellationToken.None));

        // Assert
        var failure = Assert.Single(exception.Failures);
        Assert.StartsWith("Persistence:CommandTimeoutSeconds", failure, StringComparison.Ordinal);
        Assert.Contains("needs a restart", failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartingAsync_CommandTimeoutMatchingComposition_IsAccepted()
    {
        // Arrange
        var harness = CreateHarness(
            new MailSynchronizationOptions(),
            new PersistenceOptions { CommandTimeoutSeconds = HostApplicationBuilderExtensions.DefaultDatabaseCommandTimeoutSeconds });

        // Act, Assert
        await harness.Validator.StartingAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartingAsync_UnsupportedTextSearchConfiguration_IsRefusedAsUnsupportedRatherThanAsAChange()
    {
        // Arrange
        var harness = CreateHarness(
            new MailSynchronizationOptions(),
            new PersistenceOptions { TextSearchConfiguration = "klingon" });

        // Act
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            harness.Validator.StartingAsync(CancellationToken.None));

        // Assert
        var failure = Assert.Single(exception.Failures);
        Assert.Contains("is not a PostgreSQL text search configuration MailFathom supports", failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartingAsync_ASecretWithNoName_FailsStartupNamingTheSettingToAdd()
    {
        // Arrange
        var synchronization = ConfiguredAccounts.WithPasswordReferences(("primary", "plaintext:dev-password"));
        synchronization.Accounts[0].Secrets.Password.Name = string.Empty;
        var harness = CreateHarness(synchronization, new PersistenceOptions());

        // Act
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            harness.Validator.StartingAsync(CancellationToken.None));

        // Assert
        var failure = Assert.Single(exception.Failures);
        Assert.StartsWith("MailSynchronization:Accounts:0:Secrets:Password:Name", failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartingAsync_TwoSecretsSharingAName_FailsStartupBecauseNeitherCouldBeNamedUnambiguously()
    {
        // Arrange
        var synchronization = ConfiguredAccounts.WithPasswordReferences(
            ("primary", "plaintext:dev-password"),
            ("secondary", "plaintext:dev-password"));
        synchronization.Accounts[1].Secrets.Password.Name = synchronization.Accounts[0].Secrets.Password.Name;
        var harness = CreateHarness(synchronization, new PersistenceOptions());

        // Act
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            harness.Validator.StartingAsync(CancellationToken.None));

        // Assert
        var failure = Assert.Single(exception.Failures);
        Assert.StartsWith("MailSynchronization:Accounts:1:Secrets:Password:Name", failure, StringComparison.Ordinal);
    }

    /// <summary>Names identify secrets to an operator reading one section, so adding a section must not collide with one already working.</summary>
    [Fact]
    public async Task StartingAsync_TheSameNameInTwoSections_IsAcceptedBecauseUniquenessIsScopedToOne()
    {
        // Arrange
        var synchronization = ConfiguredAccounts.WithPasswordReferences(("primary", "plaintext:dev-password"));
        var persistence = new PersistenceOptions
        {
            Password = new ConfiguredSecret
            {
                Name = synchronization.Accounts[0].Secrets.Password.Name,
                SecretReference = "plaintext:postgres-password",
            },
        };
        var harness = CreateHarness(synchronization, persistence);

        // Act, Assert
        await harness.Validator.StartingAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartingAsync_AnUnreadableLifetime_FailsStartupRatherThanFallingBackToNoLimit()
    {
        // Arrange
        var synchronization = ConfiguredAccounts.WithPasswordReferences(("primary", "plaintext:dev-password"));
        synchronization.Accounts[0].Secrets.Password.Lifetime = "next Tuesday";
        var harness = CreateHarness(synchronization, new PersistenceOptions());

        // Act
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            harness.Validator.StartingAsync(CancellationToken.None));

        // Assert
        var failure = Assert.Single(exception.Failures);
        Assert.StartsWith("MailSynchronization:Accounts:0:Secrets:Password:Lifetime", failure, StringComparison.Ordinal);
    }

    /// <summary>
    /// An expired entry left beside its replacement is what a completed rotation looks like. Refusing to start over one
    /// would make rotating a credential harder than never rotating it, so it is reported and the host runs.
    /// </summary>
    [Fact]
    public async Task StartingAsync_AnExpiredSecret_IsReportedByNameRatherThanFailingStartup()
    {
        // Arrange
        var synchronization = ConfiguredAccounts.WithPasswordReferences(("primary", "plaintext:dev-password"));
        synchronization.Accounts[0].Secrets.Password.Lifetime = "2026-07-30T00:00:00Z";
        var harness = CreateHarness(synchronization, new PersistenceOptions());

        // Act
        await harness.Validator.StartingAsync(CancellationToken.None);

        // Assert
        Assert.Contains(
            harness.ReportedMessages,
            message => message.Contains("primary-password", StringComparison.Ordinal)
                && message.Contains("lifetime ended", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartingAsync_ASecretExpiringLater_IsNotReportedAsExpired()
    {
        // Arrange
        var synchronization = ConfiguredAccounts.WithPasswordReferences(("primary", "plaintext:dev-password"));
        synchronization.Accounts[0].Secrets.Password.Lifetime = "2027-07-30T00:00:00Z";
        var harness = CreateHarness(synchronization, new PersistenceOptions());

        // Act
        await harness.Validator.StartingAsync(CancellationToken.None);

        // Assert
        Assert.DoesNotContain(harness.ReportedMessages, message => message.Contains("lifetime ended", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartingAsync_AnMcpApiKeyThatCannotBeResolved_FailsStartupNamingItsPosition()
    {
        // Arrange
        var endpoint = new McpEndpointOptions { Enabled = true, Authentication = McpTransportAuthenticationMethods.ApiKey };
        endpoint.ApiKeys.Add(new ConfiguredSecret { Name = "workstation", SecretReference = "file:/run/secrets/absent" });
        var harness = CreateHarness(new MailSynchronizationOptions(), new PersistenceOptions(), mcpEndpointOptions: endpoint);

        // Act
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            harness.Validator.StartingAsync(CancellationToken.None));

        // Assert
        var failure = Assert.Single(exception.Failures);
        Assert.StartsWith("McpEndpoint:ApiKeys:0", failure, StringComparison.Ordinal);
        Assert.Contains(nameof(SecretResolutionFailure.MaterialNotFound), failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartingAsync_TwoMcpApiKeysSharingAName_FailsStartupBecauseNeitherCouldBeRotatedByName()
    {
        // Arrange
        var endpoint = new McpEndpointOptions { Enabled = true, Authentication = McpTransportAuthenticationMethods.ApiKey };
        endpoint.ApiKeys.Add(new ConfiguredSecret { Name = "workstation", SecretReference = "plaintext:one" });
        endpoint.ApiKeys.Add(new ConfiguredSecret { Name = "workstation", SecretReference = "plaintext:two" });
        var harness = CreateHarness(new MailSynchronizationOptions(), new PersistenceOptions(), mcpEndpointOptions: endpoint);

        // Act
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            harness.Validator.StartingAsync(CancellationToken.None));

        // Assert
        var failure = Assert.Single(exception.Failures);
        Assert.StartsWith("McpEndpoint:ApiKeys:1:Name", failure, StringComparison.Ordinal);
    }

    /// <summary>
    /// The endpoint tells the two credentials apart by shape, so a key that is a token naming a configured authorization
    /// server reaches that server's validator and the key comparison it exists for is never reached. Nothing about the
    /// deployment would look wrong: the key resolves, the profile is valid, and no client can ever authenticate.
    /// </summary>
    [Fact]
    public async Task StartingAsync_AnMcpApiKeyShapedLikeATokenOfAConfiguredServer_FailsStartupNamingItsPosition()
    {
        // Arrange
        var endpoint = EndpointAcceptingBothCredentials();
        endpoint.ApiKeys.Add(new ConfiguredSecret
        {
            Name = "workstation",
            SecretReference = $"plaintext:{TokenShapedKeyIssuedBy(WorkforceIssuer)}",
        });
        var harness = CreateHarness(new MailSynchronizationOptions(), new PersistenceOptions(), mcpEndpointOptions: endpoint);

        // Act
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            harness.Validator.StartingAsync(CancellationToken.None));

        // Assert
        var failure = Assert.Single(exception.Failures);
        Assert.StartsWith("McpEndpoint:ApiKeys:0", failure, StringComparison.Ordinal);
        Assert.DoesNotContain(WorkforceIssuer, failure, StringComparison.Ordinal);
    }

    /// <summary>The shape alone decides nothing: a key naming an issuer this deployment does not configure selects no validator and is compared like any other opaque credential.</summary>
    [Fact]
    public async Task StartingAsync_AnMcpApiKeyShapedLikeATokenOfAnUnconfiguredServer_IsAccepted()
    {
        // Arrange
        var endpoint = EndpointAcceptingBothCredentials();
        endpoint.ApiKeys.Add(new ConfiguredSecret
        {
            Name = "workstation",
            SecretReference = $"plaintext:{TokenShapedKeyIssuedBy("https://sso.other.test/realms/mailfathom")}",
        });
        var harness = CreateHarness(new MailSynchronizationOptions(), new PersistenceOptions(), mcpEndpointOptions: endpoint);

        // Act
        await harness.Validator.StartingAsync(CancellationToken.None);

        // Assert
        Assert.DoesNotContain(harness.ReportedMessages, message => message.Contains("ApiKeys", StringComparison.Ordinal));
    }

    /// <summary>Material that resolves but is not a certificate would pass a reference check and then refuse every client the profile exists to serve.</summary>
    [Fact]
    public async Task StartingAsync_AClientCertificateTrustAnchorThatIsNotACertificate_FailsStartupNamingItsPosition()
    {
        // Arrange
        var endpoint = new McpEndpointOptions { Enabled = true, Authentication = McpTransportAuthenticationMethods.None };
        var profile = new McpClientCertificateProfileOptions
        {
            Name = "chatgpt-connector",
            Requirement = McpClientCertificateRequirement.Optional,
        };
        profile.TrustAnchors.Add(new ConfiguredSecret
        {
            Name = "openai-connectors-ca",
            SecretReference = "plaintext:not-a-certificate",
        });
        profile.SubjectAlternativeNames.Add("mtls.prod.connectors.openai.com");
        endpoint.ClientCertificateProfiles.Add(profile);
        var harness = CreateHarness(new MailSynchronizationOptions(), new PersistenceOptions(), mcpEndpointOptions: endpoint);

        // Act
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            harness.Validator.StartingAsync(CancellationToken.None));

        // Assert
        var failure = Assert.Single(exception.Failures);
        Assert.StartsWith(
            "McpEndpoint:ClientCertificateProfiles:0:TrustAnchors:0",
            failure,
            StringComparison.Ordinal);
        Assert.Contains(nameof(CertificateMaterialFailure.EncodingNotRecognized), failure, StringComparison.Ordinal);
    }

    /// <summary>A disabled endpoint reads no key, so failing a host over one nothing was going to use would be a rule with no purpose.</summary>
    [Fact]
    public async Task StartingAsync_AnUnresolvableApiKeyUnderADisabledEndpoint_IsNotValidated()
    {
        // Arrange
        var endpoint = new McpEndpointOptions();
        endpoint.ApiKeys.Add(new ConfiguredSecret { Name = "workstation", SecretReference = "file:/run/secrets/absent" });
        var harness = CreateHarness(new MailSynchronizationOptions(), new PersistenceOptions(), mcpEndpointOptions: endpoint);

        // Act, Assert
        await harness.Validator.StartingAsync(CancellationToken.None);
    }

    /// <summary>Every lifecycle stage other than the starting one is deliberately empty, because the check belongs before hosted services start.</summary>
    [Fact]
    public async Task RemainingLifecycleMembers_Always_CompleteWithoutResolvingAnything()
    {
        // Arrange
        var harness = CreateHarness(
            ConfiguredAccounts.WithPasswordReferences(("primary", "file:/run/secrets/absent")),
            new PersistenceOptions());

        // Act
        await harness.Validator.StartAsync(CancellationToken.None);
        await harness.Validator.StartedAsync(CancellationToken.None);
        await harness.Validator.StoppingAsync(CancellationToken.None);
        await harness.Validator.StopAsync(CancellationToken.None);
        await harness.Validator.StoppedAsync(CancellationToken.None);

        // Assert
        Assert.Empty(harness.ReportedMessages);
    }

    private static McpEndpointOptions EndpointAcceptingBothCredentials()
    {
        var authorizationServer = new McpAuthorizationServerOptions { Name = "workforce", Issuer = WorkforceIssuer };
        authorizationServer.AuthorizedSubjects.Add("9f2c");

        var endpoint = new McpEndpointOptions
        {
            Enabled = true,
            Authentication = McpTransportAuthenticationMethods.ApiKey | McpTransportAuthenticationMethods.OAuth,
            OAuth = new McpOAuthOptions { Resource = "https://mail.example.test/mcp" },
        };

        endpoint.OAuth.AuthorizationServers.Add(authorizationServer);

        return endpoint;
    }

    private static string TokenShapedKeyIssuedBy(string issuer)
    {
        var payload = Base64Url.EncodeToString(Encoding.UTF8.GetBytes($$"""{"iss":"{{issuer}}","sub":"9f2c"}"""));

        return $"header.{payload}.signature";
    }

    private static ValidatorHarness CreateHarness(
        MailSynchronizationOptions synchronizationOptions,
        PersistenceOptions persistenceOptions,
        SecretValueInterpretation interpretation = SecretValueInterpretation.ReferenceOnly,
        SecretMaterialSource source = SecretMaterialSource.SchemeAdapter,
        IDatabaseConnectionSettingsValidator? databaseConnectionSettings = null,
        McpEndpointOptions? mcpEndpointOptions = null)
    {
        var resolver = new PlaintextOnlySecretReferenceResolver { Source = source };
        var connectionSettingsValidator = databaseConnectionSettings ?? new StubDatabaseConnectionSettingsValidator();
        var validationLogger = new RecordingLogger<SecretConfigurationValidator>();
        var startupLogger = new RecordingLogger<SecretConfigurationStartupValidator>();

        var validator = new SecretConfigurationStartupValidator(
            new StubSettingsSnapshot<MailSynchronizationOptions>(synchronizationOptions),
            new StubSettingsSnapshot<PersistenceOptions>(persistenceOptions),
            Options.Create(mcpEndpointOptions ?? new McpEndpointOptions()),
            new SecretConfigurationValidator(
                resolver,
                new TrustAnchorLoader(resolver),
                new DatabaseConnectionSettingsMapper(new ConfigurationBuilder().Build()),
                connectionSettingsValidator,
                PostgresTextSearchConfiguration.Default,
                DefaultCommandTimeout,
                new FakeTimeProvider(ValidatedAt),
                validationLogger),
            new SecretResolutionOptions(interpretation),
            startupLogger);

        return new ValidatorHarness(validator, startupLogger, validationLogger);
    }

    private sealed record ValidatorHarness(
        SecretConfigurationStartupValidator Validator,
        RecordingLogger<SecretConfigurationStartupValidator> StartupLogger,
        RecordingLogger<SecretConfigurationValidator> ValidationLogger)
    {
        internal IReadOnlyList<string> ReportedMessages => [.. this.StartupLogger.Messages, .. this.ValidationLogger.Messages];
    }
}
