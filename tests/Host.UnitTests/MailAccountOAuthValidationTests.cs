// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Host.Configuration;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Mail.OAuth;
using MailFathom.Infrastructure.Secrets;
using Xunit;

namespace MailFathom.Host.UnitTests;

/// <summary>
/// Covers the rule that settles which credential an account needs before a connection is ever attempted, which is
/// what makes the password optional without making its absence a runtime discovery.
/// </summary>
public sealed class MailAccountOAuthValidationTests
{
    [Fact]
    public void Validate_TokenOnlyPolicyWithACompleteOAuthBlock_ReportsNoError()
    {
        // Arrange
        var options = CreateOptions(CreateTokenAuthenticatedAccount());

        // Act
        var results = Validate(options);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Validate_TokenOnlyPolicyWithNoPassword_DoesNotRequireOne()
    {
        // Arrange
        var account = CreateTokenAuthenticatedAccount();
        account.Secrets = new MailAccountSecretOptions();

        // Act
        var results = Validate(CreateOptions(account));

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Validate_PasswordPolicyWithNoPasswordReference_ReportsTheMissingCredential()
    {
        // Arrange
        var account = CreatePasswordAuthenticatedAccount();
        account.Secrets = new MailAccountSecretOptions();

        // Act
        var results = Validate(CreateOptions(account));

        // Assert
        Assert.Contains(results, result => result.ErrorMessage!.Contains("no password secret reference", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_PasswordPolicyCarryingAnOAuthBlock_RefusesCredentialsThatCouldNeverBeUsed()
    {
        // Arrange
        var account = CreatePasswordAuthenticatedAccount();
        account.OAuth = CreateOAuthOptions();

        // Act
        var results = Validate(CreateOptions(account));

        // Assert
        Assert.Contains(results, result => result.ErrorMessage!.Contains("could never be used", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("http://authorization.example.test/token")]
    [InlineData("authorization.example.test/token")]
    [InlineData("")]
    public void Validate_TokenEndpointThatIsNotAbsoluteHttps_IsRefused(string configuredEndpoint)
    {
        // Arrange
        var account = CreateTokenAuthenticatedAccount();
        account.OAuth.TokenEndpoint = configuredEndpoint;

        // Act
        var results = Validate(CreateOptions(account));

        // Assert
        Assert.Contains(results, result => result.ErrorMessage!.Contains("absolute HTTPS address", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RefreshTokenGrantWithoutARefreshTokenReference_PointsAtTheAuthorizationCommand()
    {
        // Arrange
        var account = CreateTokenAuthenticatedAccount();
        account.OAuth.RefreshToken = new ConfiguredSecret();

        // Act
        var results = Validate(CreateOptions(account));

        // Assert
        Assert.Contains(results, result => result.ErrorMessage!.Contains("mailfathom mailbox authorize", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ClientCredentialsGrantWithoutARefreshTokenReference_IsAccepted()
    {
        // Arrange: the app-only grant has no refresh token by definition.
        var account = CreateTokenAuthenticatedAccount();
        account.OAuth.Grant = "client_credentials";
        account.OAuth.RefreshToken = new ConfiguredSecret();

        // Act
        var results = Validate(CreateOptions(account));

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Validate_UnsupportedGrantName_IsRefused()
    {
        // Arrange
        var account = CreateTokenAuthenticatedAccount();
        account.OAuth.Grant = "authorization_code";

        // Act
        var results = Validate(CreateOptions(account));

        // Assert
        Assert.Contains(results, result => result.ErrorMessage!.Contains("OAuth grant must be one of", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_MissingClientIdOrClientSecret_IsRefused()
    {
        // Arrange
        var account = CreateTokenAuthenticatedAccount();
        account.OAuth.ClientId = string.Empty;
        account.OAuth.ClientSecret = new ConfiguredSecret();

        // Act
        var results = Validate(CreateOptions(account));

        // Assert
        Assert.Contains(results, result => result.ErrorMessage!.Contains("client identifier is required", StringComparison.Ordinal));
        Assert.Contains(results, result => result.ErrorMessage!.Contains("client secret reference is required", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_MixedAllowListPermittingBothCredentialKinds_RequiresBoth()
    {
        // Arrange
        var account = CreateTokenAuthenticatedAccount();
        account.TransportSecurity.PermittedAuthenticationMechanisms = ["OAUTHBEARER", "PLAIN"];
        account.Secrets = new MailAccountSecretOptions();

        // Act
        var results = Validate(CreateOptions(account));

        // Assert
        Assert.Contains(results, result => result.ErrorMessage!.Contains("no password secret reference", StringComparison.Ordinal));
    }

    private static IReadOnlyList<ValidationResult> Validate(MailSynchronizationOptions options) =>
        [.. options.Validate(new ValidationContext(options))];

    private static MailSynchronizationOptions CreateOptions(MailSynchronizationAccountOptions account) => new()
    {
        Enabled = true,
        Accounts = [account],
    };

    private static MailSynchronizationAccountOptions CreateTokenAuthenticatedAccount()
    {
        var account = CreatePasswordAuthenticatedAccount();
        account.TransportSecurity.PermittedAuthenticationMechanisms = ["OAUTHBEARER", "XOAUTH2"];
        account.OAuth = CreateOAuthOptions();

        return account;
    }

    private static MailSynchronizationAccountOptions CreatePasswordAuthenticatedAccount() => new()
    {
        AccountId = "primary",
        Host = "imap.example.test",
        UserName = "mailfathom@example.test",
        Secrets = new MailAccountSecretOptions
        {
            Password = new ConfiguredSecret { SecretReference = "systemd-credential:imap-primary-password" },
        },
    };

    private static MailAccountOAuthOptions CreateOAuthOptions() => new()
    {
        Grant = "refresh_token",
        TokenEndpoint = "https://authorization.example.test/token",
        ClientId = "client-id",
        Scope = "https://mail.example.test/",
        ClientSecret = new ConfiguredSecret { SecretReference = "systemd-credential:oauth-client-secret" },
        RefreshToken = new ConfiguredSecret { SecretReference = "systemd-credential:oauth-refresh-token" },
    };
}
