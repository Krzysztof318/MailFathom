// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Transport;
using MailMcp.Host.Configuration;
using MailMcp.Host.Hosting;
using MailMcp.Infrastructure.Certificates;
using MailMcp.Infrastructure.Secrets;
using Microsoft.Extensions.Options;
using Xunit;

namespace MailMcp.Host.UnitTests;

public sealed class SecretConfigurationStartupValidatorTests
{
    [Fact]
    public async Task StartingAsync_EveryReferenceResolvable_CompletesSoHostedServicesMayStart()
    {
        // Arrange
        var harness = CreateHarness(
            ConfiguredAccounts.WithPasswordReferences(("primary", "plaintext:dev-password")),
            new PersistenceOptions { Password = new ConfiguredSecret { SecretReference = "plaintext:postgres-password" } });

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
            new PersistenceOptions { Password = new ConfiguredSecret { SecretReference = "file:/run/secrets/postgres" } });

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
            new ConfiguredSecret { SecretReference = "file:/run/secrets/private-ca.pem" };
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
            new ConfiguredSecret { SecretReference = "plaintext:not-a-certificate" };
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

    private static ValidatorHarness CreateHarness(
        MailSynchronizationOptions synchronizationOptions,
        PersistenceOptions persistenceOptions,
        SecretValueInterpretation interpretation = SecretValueInterpretation.ReferenceOnly,
        SecretMaterialSource source = SecretMaterialSource.SchemeAdapter)
    {
        var resolver = new PlaintextOnlySecretReferenceResolver { Source = source };
        var validationLogger = new RecordingLogger<SecretConfigurationValidator>();
        var startupLogger = new RecordingLogger<SecretConfigurationStartupValidator>();

        var validator = new SecretConfigurationStartupValidator(
            new StubSettingsSnapshot<MailSynchronizationOptions>(synchronizationOptions),
            new StubSettingsSnapshot<PersistenceOptions>(persistenceOptions),
            new SecretConfigurationValidator(resolver, new TrustAnchorLoader(resolver), validationLogger),
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
