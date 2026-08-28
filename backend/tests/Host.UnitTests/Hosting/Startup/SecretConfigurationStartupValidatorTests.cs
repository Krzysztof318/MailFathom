// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Common;
using MailFathom.Domain.Transport;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Configuration.DataEncryption;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Persistence;
using MailFathom.Host.Hosting.Startup;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.DataEncryption;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.Infrastructure.Secrets.Sources;
using MailFathom.Infrastructure.Security.ClientCertificates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Startup;

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
    public async Task StartingAsync_EveryReferenceResolvable_ReportsTheSecretGateToTheStartupProbe()
    {
        // Arrange
        var harness = CreateHarness(
            ConfiguredAccounts.WithPasswordReferences(("primary", "plaintext:dev-password")),
            new PersistenceOptions());

        // Act
        await harness.Validator.StartingAsync(CancellationToken.None);

        // Assert
        Assert.True(harness.StartupGates.Completed);
    }

    /// <summary>
    /// A host whose secrets are unusable never comes up, so the gate stays outstanding rather than reporting a step
    /// that raised.
    /// </summary>
    [Fact]
    public async Task StartingAsync_UnresolvableReference_LeavesTheSecretGateOutstanding()
    {
        // Arrange
        var harness = CreateHarness(
            ConfiguredAccounts.WithPasswordReferences(("primary", "file:/run/secrets/absent")),
            new PersistenceOptions());

        // Act
        await Assert.ThrowsAsync<OptionsValidationException>(() => harness.Validator.StartingAsync(CancellationToken.None));

        // Assert
        Assert.False(harness.StartupGates.Completed);
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
        synchronization.Accounts[0].Secrets.Password!.Name = string.Empty;
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
        synchronization.Accounts[1].Secrets.Password!.Name = synchronization.Accounts[0].Secrets.Password!.Name;
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
                Name = synchronization.Accounts[0].Secrets.Password!.Name,
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
        synchronization.Accounts[0].Secrets.Password!.Lifetime = "next Tuesday";
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
        synchronization.Accounts[0].Secrets.Password!.Lifetime = "2026-07-30T00:00:00Z";
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
        synchronization.Accounts[0].Secrets.Password!.Lifetime = "2027-07-30T00:00:00Z";
        var harness = CreateHarness(synchronization, new PersistenceOptions());

        // Act
        await harness.Validator.StartingAsync(CancellationToken.None);

        // Assert
        Assert.DoesNotContain(harness.ReportedMessages, message => message.Contains("lifetime ended", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartingAsync_AnApiKeyThatCannotBeResolved_FailsStartupNamingItsPosition()
    {
        // Arrange
        var endpoint = EndpointAcceptingApiKeys();
        AcceptKey(endpoint, new ConfiguredSecret { Name = "workstation", SecretReference = "file:/run/secrets/absent" });
        var harness = CreateHarness(new MailSynchronizationOptions(), new PersistenceOptions(), adminEndpointOptions: endpoint);

        // Act
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            harness.Validator.StartingAsync(CancellationToken.None));

        // Assert
        var failure = Assert.Single(exception.Failures);
        Assert.StartsWith("AdminEndpoint:Authentication:0:ApiKey", failure, StringComparison.Ordinal);
        Assert.Contains(nameof(SecretResolutionFailure.MaterialNotFound), failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartingAsync_TwoApiKeysSharingAName_FailsStartupBecauseNeitherCouldBeRotatedByName()
    {
        // Arrange
        var endpoint = EndpointAcceptingApiKeys();
        AcceptKey(endpoint, new ConfiguredSecret { Name = "workstation", SecretReference = "plaintext:one" });
        AcceptKey(endpoint, new ConfiguredSecret { Name = "workstation", SecretReference = "plaintext:two" });
        var harness = CreateHarness(new MailSynchronizationOptions(), new PersistenceOptions(), adminEndpointOptions: endpoint);

        // Act
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            harness.Validator.StartingAsync(CancellationToken.None));

        // Assert
        var failure = Assert.Single(exception.Failures);
        Assert.StartsWith("AdminEndpoint:Authentication:1:ApiKey:Name", failure, StringComparison.Ordinal);
    }

    /// <summary>
    /// The endpoint tells the two credentials apart by shape, so a key that is a token naming a configured authorization
    /// server reaches that server's validator and the key comparison it exists for is never reached. Nothing about the
    /// deployment would look wrong: the key resolves, the profile is valid, and no client can ever authenticate.
    /// </summary>
    [Fact]
    public async Task StartingAsync_AnApiKeyShapedLikeATokenOfAConfiguredServer_FailsStartupNamingItsPosition()
    {
        // Arrange
        var endpoint = EndpointAcceptingBothCredentials();
        AcceptKey(endpoint, new ConfiguredSecret
        {
            Name = "workstation",
            SecretReference = $"plaintext:{TokenShapedKeyIssuedBy(WorkforceIssuer)}",
        });
        var harness = CreateHarness(new MailSynchronizationOptions(), new PersistenceOptions(), adminEndpointOptions: endpoint);

        // Act
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            harness.Validator.StartingAsync(CancellationToken.None));

        // Assert
        var failure = Assert.Single(exception.Failures);
        Assert.StartsWith("AdminEndpoint:Authentication:1:ApiKey", failure, StringComparison.Ordinal);
        Assert.DoesNotContain(WorkforceIssuer, failure, StringComparison.Ordinal);
    }

    /// <summary>The shape alone decides nothing: a key naming an issuer this deployment does not configure selects no validator and is compared like any other opaque credential.</summary>
    [Fact]
    public async Task StartingAsync_AnApiKeyShapedLikeATokenOfAnUnconfiguredServer_IsAccepted()
    {
        // Arrange
        var endpoint = EndpointAcceptingBothCredentials();
        AcceptKey(endpoint, new ConfiguredSecret
        {
            Name = "workstation",
            SecretReference = $"plaintext:{TokenShapedKeyIssuedBy("https://sso.other.test/realms/mailfathom")}",
        });
        var harness = CreateHarness(new MailSynchronizationOptions(), new PersistenceOptions(), adminEndpointOptions: endpoint);

        // Act
        await harness.Validator.StartingAsync(CancellationToken.None);

        // Assert
        Assert.DoesNotContain(harness.ReportedMessages, message => message.Contains("ApiKey", StringComparison.Ordinal));
    }

    /// <summary>
    /// The one configuration mistake nothing about a running deployment would ever report. A private key written where
    /// the public half belongs imports cleanly and verifies every client's signature correctly, so the host would start,
    /// serve, and hold exactly the credential key-pair authentication exists to keep off it.
    /// </summary>
    [Fact]
    public async Task StartingAsync_APublicKeySettingHoldingAPrivateKey_FailsStartupSayingSo()
    {
        // Arrange
        using var clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var endpoint = EndpointAcceptingPublicKey($"plaintext:{clientKey.ExportPkcs8PrivateKeyPem()}");
        var harness = CreateHarness(new MailSynchronizationOptions(), new PersistenceOptions(), adminEndpointOptions: endpoint);

        // Act
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            harness.Validator.StartingAsync(CancellationToken.None));

        // Assert
        var failure = Assert.Single(exception.Failures);
        Assert.StartsWith("AdminEndpoint:Authentication:0:PublicKey", failure, StringComparison.Ordinal);
        Assert.Contains("private key", failure, StringComparison.Ordinal);
    }

    /// <summary>Material that resolves but is not a public key would pass a reference check and then refuse every client the entry exists to serve.</summary>
    [Fact]
    public async Task StartingAsync_APublicKeySettingHoldingSomethingElse_FailsStartupNamingItsPosition()
    {
        // Arrange
        var endpoint = EndpointAcceptingPublicKey("plaintext:not-a-public-key");
        var harness = CreateHarness(new MailSynchronizationOptions(), new PersistenceOptions(), adminEndpointOptions: endpoint);

        // Act
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            harness.Validator.StartingAsync(CancellationToken.None));

        // Assert
        var failure = Assert.Single(exception.Failures);
        Assert.StartsWith("AdminEndpoint:Authentication:0:PublicKey", failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartingAsync_AUsablePublicKey_PassesStartup()
    {
        // Arrange
        using var clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var endpoint = EndpointAcceptingPublicKey($"plaintext:{clientKey.ExportSubjectPublicKeyInfoPem()}");
        var harness = CreateHarness(new MailSynchronizationOptions(), new PersistenceOptions(), adminEndpointOptions: endpoint);

        // Act, Assert
        await harness.Validator.StartingAsync(CancellationToken.None);
    }

    /// <summary>
    /// A mail-serving endpoint's section holds neither a key nor a public key, so a private key cannot be written onto
    /// one at all. What the read has to keep doing is the rest of that section's secrets, which is what this covers:
    /// a certificate anchor that resolves to nothing still fails the start.
    /// </summary>
    [Fact]
    public async Task StartingAsync_AnMcpTrustAnchorThatCannotBeResolved_FailsStartupNamingItsPosition()
    {
        // Arrange
        var endpoint = new McpEndpointOptions { Enabled = true };
        var profile = new McpClientCertificateProfileOptions
        {
            Name = "chatgpt-connector",
            Requirement = McpClientCertificateRequirement.Optional,
        };
        profile.TrustAnchors.Add(new ConfiguredSecret
        {
            Name = "openai-connectors-ca",
            SecretReference = "file:/run/secrets/absent",
        });
        profile.SubjectAlternativeNames.Add("mtls.prod.connectors.openai.com");
        endpoint.ClientCertificateProfiles.Add(profile);

        var harness = CreateHarness(
            new MailSynchronizationOptions(),
            new PersistenceOptions(),
            mcpEndpointOptions: endpoint);

        // Act
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            harness.Validator.StartingAsync(CancellationToken.None));

        // Assert
        Assert.Contains(
            exception.Failures,
            failure => failure.StartsWith("McpEndpoint:ClientCertificateProfiles:0:TrustAnchors:0", StringComparison.Ordinal)
                && failure.Contains(nameof(SecretResolutionFailure.MaterialNotFound), StringComparison.Ordinal));
    }

    /// <summary>
    /// The object-storage credential is resolved before every request rather than at startup, so nothing else in a
    /// running deployment would report a reference that resolves to nothing until the first scrape refused. Startup is
    /// where an operator finds it, and the failure names the setting rather than what the reference points at.
    /// </summary>
    [Fact]
    public async Task StartingAsync_AnUnresolvableObjectStorageCredential_FailsStartupNamingTheSetting()
    {
        // Arrange
        var contentStorage = new ContentStorageOptions
        {
            Backend = ContentStorageBackend.ObjectStorage,
            ObjectStorage = EndpointReferencing("file:key-id", "file:signing-secret"),
        };

        var harness = CreateHarness(
            new MailSynchronizationOptions(),
            new PersistenceOptions(),
            contentStorageOptions: contentStorage);

        // Act
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            harness.Validator.StartingAsync(CancellationToken.None));

        // Assert
        Assert.Contains(
            exception.Failures,
            failure => failure.StartsWith("ContentStorage:ObjectStorage:AccessKeyId", StringComparison.Ordinal));
        Assert.Contains(
            exception.Failures,
            failure => failure.StartsWith("ContentStorage:ObjectStorage:SecretAccessKey", StringComparison.Ordinal));
    }

    /// <summary>A deployment storing content in the database declares no endpoint, and must not be refused for one it does not have.</summary>
    [Fact]
    public async Task StartingAsync_TheDatabaseContentBackend_NeverJudgesTheObjectStorageBlock()
    {
        // Arrange
        var contentStorage = new ContentStorageOptions
        {
            ObjectStorage = EndpointReferencing("file:key-id", "file:signing-secret"),
        };

        var harness = CreateHarness(
            new MailSynchronizationOptions(),
            new PersistenceOptions(),
            contentStorageOptions: contentStorage);

        // Act
        await harness.Validator.StartingAsync(CancellationToken.None);

        // Assert
        Assert.True(harness.StartupGates.Completed);
    }

    /// <summary>Material that resolves but is not a certificate would pass a reference check and then refuse every client the profile exists to serve.</summary>
    [Fact]
    public async Task StartingAsync_AClientCertificateTrustAnchorThatIsNotACertificate_FailsStartupNamingItsPosition()
    {
        // Arrange
        var endpoint = new McpEndpointOptions { Enabled = true };
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
        var endpoint = EndpointAcceptingApiKeys();
        endpoint.Enabled = false;
        AcceptKey(endpoint, new ConfiguredSecret { Name = "workstation", SecretReference = "file:/run/secrets/absent" });
        var harness = CreateHarness(new MailSynchronizationOptions(), new PersistenceOptions(), adminEndpointOptions: endpoint);

        // Act, Assert
        await harness.Validator.StartingAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartingAsync_ADataEncryptionKeyOfTheWrongLength_IsRefusedNamingTheMaterial()
    {
        // Arrange — material that resolves but is not a key would pass a reference check and then fail every read of a
        // sealed value, which is the failure this section exists to move to startup. Thirty-three bytes is the mistake
        // that actually happens: it is what the command beside this one generates for a database password.
        var harness = CreateHarness(
            new MailSynchronizationOptions(),
            new PersistenceOptions(),
            dataEncryptionOptions: RingOf("2026-08", Convert.ToBase64String(new byte[33])));

        // Act
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            harness.Validator.StartingAsync(CancellationToken.None));

        // Assert
        var failure = Assert.Single(exception.Failures);
        Assert.StartsWith("DataEncryption:Keys:0:Material", failure, StringComparison.Ordinal);
        Assert.Contains(nameof(DataEncryptionKeyMaterialFailure.WrongLength), failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartingAsync_ADataEncryptionKeyThatIsNotBase64_IsRefusedNamingTheMaterial()
    {
        // Arrange
        var harness = CreateHarness(
            new MailSynchronizationOptions(),
            new PersistenceOptions(),
            dataEncryptionOptions: RingOf("2026-08", "not-base64-material"));

        // Act
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            harness.Validator.StartingAsync(CancellationToken.None));

        // Assert
        var failure = Assert.Single(exception.Failures);
        Assert.StartsWith("DataEncryption:Keys:0:Material", failure, StringComparison.Ordinal);
        Assert.Contains(nameof(DataEncryptionKeyMaterialFailure.NotBase64), failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartingAsync_ARingWhoseMaterialIsAKey_PassesTheGate()
    {
        // Arrange — the counterpart the two refusals need: without it they would pass against a validator that
        // rejected every ring it was handed.
        var harness = CreateHarness(
            new MailSynchronizationOptions(),
            new PersistenceOptions(),
            dataEncryptionOptions: RingOf(
                "2026-08",
                Convert.ToBase64String(new byte[AesGcmEnvelope.KeySizeInBytes])));

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

    private static AdminEndpointOptions EndpointAcceptingBothCredentials()
    {
        var authorizationServer = new AuthorizationServerOptions { Name = "workforce", Issuer = WorkforceIssuer };
        authorizationServer.AuthorizedSubjects.Add("9f2c");

        var oauth = new OAuthValidationOptions { Resource = "https://mail.example.test/admin" };
        oauth.AuthorizationServers.Add(authorizationServer);

        var endpoint = EndpointAcceptingApiKeys();
        endpoint.Authentication.Add(new TransportAuthenticationOptions { OAuth = oauth });

        return endpoint;
    }

    /// <summary>An enabled endpoint whose keys the caller adds, one entry per key.</summary>
    private static AdminEndpointOptions EndpointAcceptingApiKeys() => new() { Enabled = true };

    /// <summary>An enabled endpoint accepting one client's assertions, verified against whatever the reference resolves to.</summary>
    private static AdminEndpointOptions EndpointAcceptingPublicKey(string secretReference)
    {
        var endpoint = new AdminEndpointOptions { Enabled = true };
        endpoint.Authentication.Add(new TransportAuthenticationOptions
        {
            PublicKey = new ConfiguredSecret { Name = "nightly-digest", SecretReference = secretReference },
        });

        return endpoint;
    }

    /// <summary>Adds one key as an entry of its own, which is what a configured credential is.</summary>
    private static void AcceptKey(AdminEndpointOptions endpoint, ConfiguredSecret key) =>
        endpoint.Authentication.Add(new TransportAuthenticationOptions { ApiKey = key });

    private static string TokenShapedKeyIssuedBy(string issuer)
    {
        var payload = Base64Url.EncodeToString(Encoding.UTF8.GetBytes($$"""{"iss":"{{issuer}}","sub":"9f2c"}"""));

        return $"header.{payload}.signature";
    }

    private static DataEncryptionOptions RingOf(string keyId, string material)
    {
        var options = new DataEncryptionOptions { ActiveKeyId = keyId };
        options.Keys.Add(new DataEncryptionKeyOptions
        {
            KeyId = keyId,
            Material = new ConfiguredSecret { Name = "mailfathom-data-key", SecretReference = $"plaintext:{material}" },
        });

        return options;
    }

    private static ValidatorHarness CreateHarness(
        MailSynchronizationOptions synchronizationOptions,
        PersistenceOptions persistenceOptions,
        SecretValueInterpretation interpretation = SecretValueInterpretation.ReferenceOnly,
        SecretMaterialSource source = SecretMaterialSource.SchemeAdapter,
        IDatabaseConnectionSettingsValidator? databaseConnectionSettings = null,
        McpEndpointOptions? mcpEndpointOptions = null,
        AdminEndpointOptions? adminEndpointOptions = null,
        ClientEndpointOptions? clientEndpointOptions = null,
        DataEncryptionOptions? dataEncryptionOptions = null,
        ContentStorageOptions? contentStorageOptions = null)
    {
        var resolver = new PlaintextOnlySecretReferenceResolver { Source = source };
        var connectionSettingsValidator = databaseConnectionSettings ?? new StubDatabaseConnectionSettingsValidator();
        var validationLogger = new RecordingLogger<SecretConfigurationValidator>();
        var startupLogger = new RecordingLogger<SecretConfigurationStartupValidator>();
        var startupGates = new HostStartupGates(HostStartupGate.SecretConfiguration);

        var validator = new SecretConfigurationStartupValidator(
            new StubSettingsSnapshot<MailSynchronizationOptions>(synchronizationOptions),
            new StubSettingsSnapshot<PersistenceOptions>(persistenceOptions),
            new StubSettingsSnapshot<DataEncryptionOptions>(dataEncryptionOptions ?? new DataEncryptionOptions()),
            Options.Create(mcpEndpointOptions ?? new McpEndpointOptions()),
            Options.Create(adminEndpointOptions ?? new AdminEndpointOptions()),
            Options.Create(clientEndpointOptions ?? new ClientEndpointOptions()),
            Options.Create(contentStorageOptions ?? new ContentStorageOptions()),
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
            startupGates,
            startupLogger);

        return new ValidatorHarness(validator, startupGates, startupLogger, validationLogger);
    }

    /// <summary>An endpoint declaration whose two credential halves reference exactly what the caller states.</summary>
    /// <remarks>Written here rather than borrowed, because which scheme a reference carries is what each of these tests is arranging: this harness resolves <c>plaintext:</c> and fails everything else.</remarks>
    private static ObjectStorageOptions EndpointReferencing(string accessKeyIdReference, string secretAccessKeyReference) => new()
    {
        Endpoint = "https://objects.example.test:9000/",
        Bucket = "payloads",
        AccessKeyId = new ConfiguredSecret { Name = "object-storage-key-id", SecretReference = accessKeyIdReference },
        SecretAccessKey = new ConfiguredSecret { Name = "object-storage-secret", SecretReference = secretAccessKeyReference },
    };

    private sealed record ValidatorHarness(
        SecretConfigurationStartupValidator Validator,
        HostStartupGates StartupGates,
        RecordingLogger<SecretConfigurationStartupValidator> StartupLogger,
        RecordingLogger<SecretConfigurationValidator> ValidationLogger)
    {
        internal IReadOnlyList<string> ReportedMessages => [.. this.StartupLogger.Messages, .. this.ValidationLogger.Messages];
    }
}
