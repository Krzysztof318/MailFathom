// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Host.Configuration;
using MailMcp.Host.Hosting;
using MailMcp.Infrastructure.Mail;
using MailMcp.Infrastructure.Secrets;
using Microsoft.Extensions.Options;
using Xunit;

namespace MailMcp.Host.UnitTests;

public sealed class SecretReferenceStartupValidatorTests
{
    [Fact]
    public async Task StartingAsync_EveryReferenceResolvable_CompletesSoHostedServicesMayStart()
    {
        // Arrange
        var validator = CreateValidator(
            CreateSynchronizationOptions(("primary", "plaintext:dev-password")),
            new PersistenceOptions { Password = new ConfiguredSecret { SecretReference = "plaintext:postgres-password" } },
            out var logger);

        // Act, Assert
        await validator.StartingAsync(CancellationToken.None);
        Assert.DoesNotContain(logger.Messages, message => message.Contains("could not be resolved", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartingAsync_NoSecretConfigured_CompletesBecauseNothingWasDiscovered()
    {
        // Arrange
        var validator = CreateValidator(new MailSynchronizationOptions(), new PersistenceOptions(), out _);

        // Act, Assert
        await validator.StartingAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartingAsync_UnresolvableReference_FailsStartupNamingTheConfigurationPath()
    {
        // Arrange
        var validator = CreateValidator(
            CreateSynchronizationOptions(("primary", "file:/run/secrets/absent")),
            new PersistenceOptions(),
            out _);

        // Act
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            validator.StartingAsync(CancellationToken.None));

        // Assert
        var failure = Assert.Single(exception.Failures);
        Assert.StartsWith("MailSynchronization:Accounts:0:Secrets:Password", failure, StringComparison.Ordinal);
        Assert.Contains(nameof(SecretResolutionFailure.MaterialNotFound), failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartingAsync_SeveralUnresolvableReferences_ReportsThemAllAtOnce()
    {
        // Arrange
        var validator = CreateValidator(
            CreateSynchronizationOptions(
                ("primary", "file:/run/secrets/absent"),
                ("secondary", "a-pasted-password")),
            new PersistenceOptions { Password = new ConfiguredSecret { SecretReference = "file:/run/secrets/postgres" } },
            out _);

        // Act
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            validator.StartingAsync(CancellationToken.None));

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
        var validator = CreateValidator(
            CreateSynchronizationOptions(("primary", "a-pasted-password")),
            new PersistenceOptions(),
            out _);

        // Act
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            validator.StartingAsync(CancellationToken.None));

        // Assert
        var failure = Assert.Single(exception.Failures);
        Assert.Contains(nameof(SecretResolutionFailure.SchemeMissing), failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartingAsync_EveryFailure_NamesNeitherTheReferenceTargetNorTheMaterial()
    {
        // Arrange
        var validator = CreateValidator(
            CreateSynchronizationOptions(("primary", "file:/run/secrets/imap-primary-password")),
            new PersistenceOptions(),
            out var logger);

        // Act
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            validator.StartingAsync(CancellationToken.None));

        // Assert
        var reported = string.Join(' ', exception.Failures.Concat(logger.Messages));
        Assert.DoesNotContain("/run/secrets", reported, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartingAsync_TrustAnchorBlock_IsDiscoveredAndResolvedLikeAnyOtherSecret()
    {
        // Arrange
        var synchronization = CreateSynchronizationOptions(("primary", "plaintext:dev-password"));
        synchronization.Accounts[0].TransportSecurity.TrustedCertificateAuthority =
            new ConfiguredSecret { SecretReference = "file:/run/secrets/private-ca.pem" };
        var validator = CreateValidator(synchronization, new PersistenceOptions(), out _);

        // Act
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            validator.StartingAsync(CancellationToken.None));

        // Assert
        var failure = Assert.Single(exception.Failures);
        Assert.StartsWith(
            "MailSynchronization:Accounts:0:TransportSecurity:TrustedCertificateAuthority",
            failure,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartingAsync_InlineResolvedSetting_IsLoggedByNameAndNeverByValue()
    {
        // Arrange
        var validator = CreateValidator(
            CreateSynchronizationOptions(("primary", "plaintext:top-secret-password")),
            new PersistenceOptions(),
            out var logger,
            SecretValueInterpretation.ReferenceOrInline,
            SecretMaterialSource.InlineValue);

        // Act
        await validator.StartingAsync(CancellationToken.None);

        // Assert
        Assert.Contains(logger.Messages, message => message.Contains("MailSynchronization:Accounts:0:Secrets:Password", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("top-secret-password", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartingAsync_ReferenceResolvedThroughAnAdapter_LogsNoInlineWarning()
    {
        // Arrange
        var validator = CreateValidator(
            CreateSynchronizationOptions(("primary", "plaintext:dev-password")),
            new PersistenceOptions(),
            out var logger);

        // Act
        await validator.StartingAsync(CancellationToken.None);

        // Assert
        Assert.DoesNotContain(logger.Messages, message => message.Contains("inline secret value", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartingAsync_Always_LogsTheActiveInterpretationMode()
    {
        // Arrange
        var validator = CreateValidator(new MailSynchronizationOptions(), new PersistenceOptions(), out var logger);

        // Act
        await validator.StartingAsync(CancellationToken.None);

        // Assert
        Assert.Contains(logger.Messages, message => message.Contains(nameof(SecretValueInterpretation.ReferenceOnly), StringComparison.Ordinal));
    }

    /// <summary>Every lifecycle stage other than the starting one is deliberately empty, because the check belongs before hosted services start.</summary>
    [Fact]
    public async Task RemainingLifecycleMembers_Always_CompleteWithoutResolvingAnything()
    {
        // Arrange
        var validator = CreateValidator(
            CreateSynchronizationOptions(("primary", "file:/run/secrets/absent")),
            new PersistenceOptions(),
            out var logger);

        // Act
        await validator.StartAsync(CancellationToken.None);
        await validator.StartedAsync(CancellationToken.None);
        await validator.StoppingAsync(CancellationToken.None);
        await validator.StopAsync(CancellationToken.None);
        await validator.StoppedAsync(CancellationToken.None);

        // Assert
        Assert.Empty(logger.Messages);
    }

    private static MailSynchronizationOptions CreateSynchronizationOptions(
        params (string AccountId, string SecretReference)[] accounts) => new()
        {
            Accounts = [.. accounts.Select(account => new MailSynchronizationAccountOptions
            {
                AccountId = account.AccountId,
                Host = "imap.example.test",
                UserName = "mailmcp@example.test",
                Secrets = new MailAccountSecretOptions
                {
                    Password = new ConfiguredSecret { SecretReference = account.SecretReference },
                },
            })],
        };

    private static SecretReferenceStartupValidator CreateValidator(
        MailSynchronizationOptions synchronizationOptions,
        PersistenceOptions persistenceOptions,
        out RecordingLogger<SecretReferenceStartupValidator> logger,
        SecretValueInterpretation interpretation = SecretValueInterpretation.ReferenceOnly,
        SecretMaterialSource source = SecretMaterialSource.SchemeAdapter)
    {
        logger = new RecordingLogger<SecretReferenceStartupValidator>();

        return new SecretReferenceStartupValidator(
            Options.Create(synchronizationOptions),
            Options.Create(persistenceOptions),
            new PlaintextOnlySecretReferenceResolver { Source = source },
            new SecretResolutionOptions(interpretation),
            logger);
    }
}
