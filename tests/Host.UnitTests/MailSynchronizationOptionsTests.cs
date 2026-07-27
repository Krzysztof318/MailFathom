// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Accounts;
using MailMcp.Domain.Transport;
using MailMcp.Host.Configuration;
using MailMcp.Infrastructure.Certificates;
using MailMcp.Infrastructure.Mail;
using MailMcp.Infrastructure.Secrets;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailMcp.Host.UnitTests;

public sealed class MailSynchronizationOptionsTests
{
    [Fact]
    public void ValidateForSynchronization_DisabledWithNoAccounts_ReportsNoError()
    {
        // Arrange
        var options = new MailSynchronizationOptions();

        // Act
        var results = options.ValidateForSynchronization().ToArray();

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void ValidateForSynchronization_EnabledWithNoAccounts_RequiresAnAccount()
    {
        // Arrange
        var options = new MailSynchronizationOptions { Enabled = true };

        // Act
        var results = options.ValidateForSynchronization().ToArray();

        // Assert
        var result = Assert.Single(results);
        Assert.Contains("At least one account", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateForSynchronization_AccountIdsDifferingOnlyByNormalization_ReportsThemAsDuplicates()
    {
        // Arrange
        var options = new MailSynchronizationOptions
        {
            Accounts =
            [
                CreateAccount("primary"),
                CreateAccount("  primary  "),
            ],
        };

        // Act
        var results = options.ValidateForSynchronization().ToArray();

        // Assert
        Assert.Contains(results, result => result.ErrorMessage!.Contains("unique after normalization", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateForSynchronization_EnabledAccountMissingHostAndUserName_ReportsBoth()
    {
        // Arrange
        var options = new MailSynchronizationOptions
        {
            Enabled = true,
            Accounts = [new MailSynchronizationAccountOptions { AccountId = "primary" }],
        };

        // Act
        var messages = options.ValidateForSynchronization().Select(result => result.ErrorMessage).ToArray();

        // Assert
        Assert.Contains(messages, message => message!.Contains("IMAP host is required", StringComparison.Ordinal));
        Assert.Contains(messages, message => message!.Contains("IMAP user name is required", StringComparison.Ordinal));
    }

    /// <summary>The password is no longer a configuration value, so its absence is a resolution failure rather than a binding rule.</summary>
    [Fact]
    public void ValidateForSynchronization_EnabledAccountWithoutASecretReference_ReportsNoPasswordRule()
    {
        // Arrange
        var options = new MailSynchronizationOptions
        {
            Enabled = true,
            Accounts = [CreateAccount("primary", secretReference: string.Empty)],
        };

        // Act
        var messages = options.ValidateForSynchronization().Select(result => result.ErrorMessage).ToArray();

        // Assert
        Assert.DoesNotContain(messages, message => message!.Contains("password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateForSynchronization_UnsafeTransportSecurity_NamesTheAccountAndTheViolationIdentity()
    {
        // Arrange
        var account = CreateAccount("primary");
        account.TransportSecurity.ConnectionSecurity = MailConnectionSecurity.None;
        var options = new MailSynchronizationOptions { Enabled = true, Accounts = [account] };

        // Act
        var messages = options.ValidateForSynchronization().Select(result => result.ErrorMessage).ToArray();

        // Assert
        Assert.Contains(messages, message => message!.Contains("Account 'primary'", StringComparison.Ordinal)
            && message.Contains(nameof(MailTransportSecurityViolation.UnencryptedConnectionRequiresExplicitOptIn), StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateForSynchronization_UnsafeTransportSecurity_NeverNamesTheUserNameOrTheSecretReference()
    {
        // Arrange
        var account = CreateAccount("primary", secretReference: "systemd-credential:imap-primary-password");
        account.UserName = "mailmcp@example.test";
        account.TransportSecurity.ConnectionSecurity = MailConnectionSecurity.None;
        var options = new MailSynchronizationOptions { Enabled = true, Accounts = [account] };

        // Act
        var messages = string.Join(' ', options.ValidateForSynchronization().Select(result => result.ErrorMessage));

        // Assert
        Assert.DoesNotContain("mailmcp@example.test", messages, StringComparison.Ordinal);
        Assert.DoesNotContain("imap-primary-password", messages, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateForSynchronization_DuplicateFolderNamesAfterNormalization_ReportsThem()
    {
        // Arrange
        var account = CreateAccount("primary");
        account.Folders = ["INBOX", "  INBOX  "];
        var options = new MailSynchronizationOptions { Accounts = [account] };

        // Act
        var messages = options.ValidateForSynchronization().Select(result => result.ErrorMessage).ToArray();

        // Assert
        Assert.Contains(messages, message => message!.Contains("Configured folder names must be unique", StringComparison.Ordinal));
    }

    [Fact]
    public void EffectiveFolders_FoldersOmitted_AppliesThePostBindingDefault()
    {
        // Arrange
        var account = new MailSynchronizationAccountOptions();

        // Act
        var folders = account.EffectiveFolders;

        // Assert
        Assert.Equal(["INBOX"], folders);
    }

    [Fact]
    public void GetPolicy_ConfiguredAccount_ReturnsTheAccountsValidatedDomainPolicy()
    {
        // Arrange
        var options = new MailSynchronizationOptions { Accounts = [CreateAccount("  primary  ")] };

        // Act
        var policy = options.GetPolicy(MailAccountId.Create("primary"));

        // Assert
        Assert.Equal(MailConnectionSecurity.TlsOnConnect, policy.ConnectionSecurity);
    }

    [Fact]
    public async Task ResolveSettingsAsync_ConfiguredAccount_ResolvesTheAccountPasswordForTheCallerToOwn()
    {
        // Arrange
        var account = CreateAccount("  primary  ", secretReference: "plaintext:dev-password");
        account.Host = "  imap.example.test  ";
        account.UserName = "mailmcp@example.test";
        var options = new MailSynchronizationOptions { Accounts = [account] };

        // Act
        using var settings = (await options.ResolveSettingsAsync(
            "primary",
            new PlaintextOnlySecretReferenceResolver(),
            CreateTrustAnchorLoader(),
            CancellationToken.None)).Material;

        // Assert
        Assert.Equal("dev-password", settings.Password.RevealAsString());
    }

    [Fact]
    public async Task ResolveSettingsAsync_ConfiguredAccount_CarriesTheEndpointSettingsUnchanged()
    {
        // Arrange
        var account = CreateAccount("primary", secretReference: "plaintext:dev-password");
        account.Host = " imap.example.test ";
        account.Port = 1993;
        account.UserName = "mailmcp@example.test";
        var options = new MailSynchronizationOptions { Accounts = [account] };

        // Act
        var settings = await options.ResolveSettingsAsync(
            "  primary ",
            new PlaintextOnlySecretReferenceResolver(),
            CreateTrustAnchorLoader(),
            CancellationToken.None);

        // Assert
        using (settings.Material)
        {
            Assert.Equal("primary", settings.AccountId);
            Assert.Equal("imap.example.test", settings.Host);
            Assert.Equal(1993, settings.Port);
            Assert.Equal("mailmcp@example.test", settings.UserName);
        }
    }

    [Fact]
    public async Task ResolveSettingsAsync_UnresolvableReference_FailsClosedInsteadOfReturningSettings()
    {
        // Arrange
        var options = new MailSynchronizationOptions
        {
            Accounts = [CreateAccount("primary", secretReference: "file:/run/secrets/absent")],
        };

        // Act, Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => options.ResolveSettingsAsync(
            "primary",
            new PlaintextOnlySecretReferenceResolver(),
            CreateTrustAnchorLoader(),
            CancellationToken.None));
    }

    [Fact]
    public void Bind_FlatColonSeparatedKeys_ProducesTheSameAccountShapeAsAJsonDocument()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MailSynchronization:Enabled"] = "true",
                ["MailSynchronization:Accounts:0:AccountId"] = "primary",
                ["MailSynchronization:Accounts:0:Host"] = "imap.example.test",
                ["MailSynchronization:Accounts:0:UserName"] = "mailmcp@example.test",
                ["MailSynchronization:Accounts:0:Secrets:Password:SecretReference"] = "systemd-credential:imap-primary-password",
            })
            .Build();

        // Act
        var options = configuration.GetSection("MailSynchronization").Get<MailSynchronizationOptions>()!;

        // Assert
        var account = Assert.Single(options.Accounts);
        Assert.Equal("systemd-credential:imap-primary-password", account.Secrets.Password.SecretReference);
        Assert.Empty(options.ValidateForSynchronization());
    }

    private static TrustAnchorLoader CreateTrustAnchorLoader() =>
        new(new PlaintextOnlySecretReferenceResolver());

    private static MailSynchronizationAccountOptions CreateAccount(
        string accountId,
        string secretReference = "systemd-credential:imap-primary-password") => new()
        {
            AccountId = accountId,
            Host = "imap.example.test",
            UserName = "mailmcp@example.test",
            Secrets = new MailAccountSecretOptions
            {
                Password = new ConfiguredSecret { SecretReference = secretReference },
            },
        };
}
