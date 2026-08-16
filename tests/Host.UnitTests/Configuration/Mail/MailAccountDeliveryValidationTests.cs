// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Transport;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Secrets.Discovery;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Mail;

/// <summary>
/// Covers where an account's mail is submitted: that the endpoint is judged by the rules the reading endpoint is
/// judged by, and that an unsafe or incomplete one is refused at startup rather than at the first send.
/// </summary>
public sealed class MailAccountDeliveryValidationTests
{
    /// <summary>An account that names no submission host sends nothing, which is the default and an ordinary shape.</summary>
    [Fact]
    public void Validate_AccountConfiguringNoSubmissionEndpoint_ReportsNoError()
    {
        // Arrange
        var options = CreateOptions(CreateAccount());

        // Act
        var results = Validate(options);

        // Assert
        Assert.Empty(results);
        Assert.Null(options.GetDeliveryPolicy(MailAccountId.Create("primary")));
    }

    /// <summary>A complete submission endpoint under the account's own policy is accepted and reachable.</summary>
    [Fact]
    public void Validate_CompleteSubmissionEndpoint_ReportsNoErrorAndPublishesItsPolicy()
    {
        // Arrange
        var account = CreateAccount();
        account.Delivery = CreateDelivery();
        var options = CreateOptions(account);

        // Act
        var results = Validate(options);

        // Assert
        Assert.Empty(results);
        var policy = options.GetDeliveryPolicy(MailAccountId.Create("primary"));
        Assert.Equal(MailConnectionSecurity.StartTlsRequired, policy?.ConnectionSecurity);
        Assert.Equal(
            options.GetPolicy(MailAccountId.Create("primary")).Authentication.PermittedMechanisms,
            policy?.Authentication.PermittedMechanisms);
    }

    /// <summary>The two endpoints are two servers, so the submission one may be encrypted differently from the reading one.</summary>
    [Fact]
    public void GetDeliveryPolicy_EndpointsWithDifferentConnectionSecurity_ReadsEachUnderItsOwn()
    {
        // Arrange
        var account = CreateAccount();
        account.TransportSecurity.ConnectionSecurity = MailConnectionSecurity.TlsOnConnect;
        account.Delivery = CreateDelivery();
        var options = CreateOptions(account);

        // Act
        var readingPolicy = options.GetPolicy(MailAccountId.Create("primary"));
        var deliveryPolicy = options.GetDeliveryPolicy(MailAccountId.Create("primary"));

        // Assert
        Assert.Equal(MailConnectionSecurity.TlsOnConnect, readingPolicy.ConnectionSecurity);
        Assert.Equal(MailConnectionSecurity.StartTlsRequired, deliveryPolicy?.ConnectionSecurity);
    }

    /// <summary>
    /// A downgrade the account refuses for reading is refused for submitting, under the rule the mode actually breaks:
    /// a mode that is never encrypted breaks a different one from a mode that gives the server the choice, and a report
    /// naming the wrong one would send an operator to the wrong opt-in.
    /// </summary>
    [Theory]
    [InlineData(MailConnectionSecurity.None, MailTransportSecurityViolation.UnencryptedConnectionRequiresExplicitOptIn)]
    [InlineData(MailConnectionSecurity.StartTlsWhenAvailable, MailTransportSecurityViolation.OpportunisticEncryptionRequiresExplicitOptIn)]
    [InlineData(MailConnectionSecurity.Auto, MailTransportSecurityViolation.OpportunisticEncryptionRequiresExplicitOptIn)]
    public void Validate_SubmissionEndpointWeakeningTheChannelWithoutTheOptIn_IsRefused(
        MailConnectionSecurity connectionSecurity,
        MailTransportSecurityViolation expectedViolation)
    {
        // Arrange
        var account = CreateAccount();
        account.Delivery = CreateDelivery();
        account.Delivery.ConnectionSecurity = connectionSecurity;

        // Act
        var results = Validate(CreateOptions(account));

        // Assert
        Assert.Contains(
            results,
            result => result.ErrorMessage!.Contains("submission endpoint", StringComparison.Ordinal)
                && result.ErrorMessage.EndsWith($"[{expectedViolation}]", StringComparison.Ordinal)
                && result.MemberNames.Contains($"{nameof(MailSynchronizationAccountOptions.Delivery)}.{nameof(MailAccountDeliveryOptions.ConnectionSecurity)}"));
    }

    /// <summary>A connection mode that is no mode at all cannot be classified, so it is refused rather than assumed safe.</summary>
    [Fact]
    public void Validate_SubmissionEndpointWithAnUndefinedConnectionMode_IsRefused()
    {
        // Arrange
        var account = CreateAccount();
        account.Delivery = CreateDelivery();
        account.Delivery.ConnectionSecurity = (MailConnectionSecurity)99;

        // Act
        var results = Validate(CreateOptions(account));

        // Assert
        Assert.Contains(
            results,
            result => result.ErrorMessage!.Contains(
                nameof(MailTransportSecurityViolation.ConnectionSecurityNotSupported),
                StringComparison.Ordinal));
    }

    /// <summary>A port outside the range names no endpoint, and the account is refused rather than left to fail on connecting.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    public void Validate_SubmissionPortOutsideTheRange_IsRefused(int port)
    {
        // Arrange
        var account = CreateAccount();
        account.Delivery = CreateDelivery();
        account.Delivery.Port = port;

        // Act
        var results = Validate(CreateOptions(account));

        // Assert
        Assert.Contains(results, result => result.ErrorMessage!.Contains("submission port", StringComparison.Ordinal));
    }

    /// <summary>A credential provisioned for an endpoint that does not exist is refused rather than left silently unused.</summary>
    [Fact]
    public void Validate_DeliveryCredentialWithoutASubmissionHost_IsRefused()
    {
        // Arrange
        var account = CreateAccount();
        account.Delivery = new MailAccountDeliveryOptions
        {
            Secrets = new MailAccountSecretOptions
            {
                Password = new ConfiguredSecret { SecretReference = "systemd-credential:smtp-primary-password" },
            },
        };

        // Act
        var results = Validate(CreateOptions(account));

        // Assert
        Assert.Contains(results, result => result.ErrorMessage!.Contains("no submission host", StringComparison.Ordinal));
    }

    /// <summary>
    /// An account that only submits is still validated, because submitting and reading are separate capabilities
    /// against separate servers and the reading rules do not run when synchronization is switched off.
    /// </summary>
    [Fact]
    public void Validate_SubmissionOnlyDeploymentWithNoCredentialAnywhere_IsRefused()
    {
        // Arrange
        var account = CreateAccount();
        account.Secrets = new MailAccountSecretOptions();
        account.Delivery = CreateDelivery();
        var options = CreateOptions(account);
        options.Enabled = false;

        // Act
        var results = Validate(options);

        // Assert
        Assert.Contains(results, result => result.ErrorMessage!.Contains("no password secret reference", StringComparison.Ordinal));
    }

    /// <summary>An account that submits with the same login it reads with configures nothing beyond the endpoint.</summary>
    [Fact]
    public void Validate_SubmissionOnlyDeploymentInheritingTheAccountsCredential_ReportsNoError()
    {
        // Arrange
        var account = CreateAccount();
        account.Delivery = CreateDelivery();
        var options = CreateOptions(account);
        options.Enabled = false;

        // Act
        var results = Validate(options);

        // Assert
        Assert.Empty(results);
    }

    /// <summary>The submission login is the block's where it names one, so a relay in front of the provider is configurable.</summary>
    [Fact]
    public void ResolveUserName_DeliveryBlockNamingItsOwnLogin_PrefersItOverTheAccounts()
    {
        // Arrange
        var delivery = CreateDelivery();
        delivery.UserName = "relay-user";

        // Act, Assert
        Assert.Equal("relay-user", delivery.ResolveUserName("mailfathom@example.test"));
        Assert.Equal("mailfathom@example.test", CreateDelivery().ResolveUserName("mailfathom@example.test"));
    }

    /// <summary>A delivery secret block naming no reference reads as absent, so the account's credential is what is presented.</summary>
    [Fact]
    public void ResolveSecrets_DeliveryBlockNamingNoReference_FallsBackToTheAccounts()
    {
        // Arrange
        var accountSecrets = new MailAccountSecretOptions
        {
            Password = new ConfiguredSecret { SecretReference = "systemd-credential:imap-primary-password" },
        };
        var delivery = CreateDelivery();
        delivery.Secrets = new MailAccountSecretOptions { Password = new ConfiguredSecret() };

        // Act, Assert
        Assert.Same(accountSecrets, delivery.ResolveSecrets(accountSecrets));

        delivery.Secrets = new MailAccountSecretOptions
        {
            Password = new ConfiguredSecret { SecretReference = "systemd-credential:smtp-primary-password" },
        };
        Assert.Same(delivery.Secrets, delivery.ResolveSecrets(accountSecrets));
    }

    private static IReadOnlyList<ValidationResult> Validate(MailSynchronizationOptions options) =>
        [.. options.Validate(new ValidationContext(options))];

    private static MailSynchronizationOptions CreateOptions(MailSynchronizationAccountOptions account) => new()
    {
        Enabled = true,
        Accounts = [account],
    };

    private static MailSynchronizationAccountOptions CreateAccount() => new()
    {
        AccountId = "primary",
        DisplayName = "The primary mailbox",
        Host = "imap.example.test",
        UserName = "mailfathom@example.test",
        Secrets = new MailAccountSecretOptions
        {
            Password = new ConfiguredSecret { SecretReference = "systemd-credential:imap-primary-password" },
        },
    };

    private static MailAccountDeliveryOptions CreateDelivery() => new()
    {
        Host = "smtp.example.test",
        Port = 587,
        ConnectionSecurity = MailConnectionSecurity.StartTlsRequired,
    };
}
