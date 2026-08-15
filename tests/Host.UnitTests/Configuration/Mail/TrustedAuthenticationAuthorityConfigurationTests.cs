// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Secrets.Discovery;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Mail;

/// <summary>Covers which server an account believes about who sent its mail, and what an unusable answer costs.</summary>
public sealed class TrustedAuthenticationAuthorityConfigurationTests
{
    /// <summary>The account's own authserv-id reaches the reading that selects a header, whatever case it was written in.</summary>
    [Fact]
    public void GetTrustedAuthority_ConfiguredAccount_AnswersWithTheServerItNamed()
    {
        // Arrange
        var account = CreateAccount("primary");
        account.TrustedAuthenticationServiceIdentifier = "MX.Example.Test";
        var options = OptionsFor(account);

        // Act
        var authority = options.GetTrustedAuthority(MailAccountId.Create("primary"));

        // Assert
        Assert.True(authority.NamesAServer);
        Assert.True(authority.Produced("mx.example.test"));
        Assert.False(authority.Produced("attacker.test"));
    }

    /// <summary>Naming no server is an ordinary choice, and it makes the account believe no header at all.</summary>
    [Fact]
    public void GetTrustedAuthority_AccountNamingNoServer_BelievesNothing()
    {
        // Act
        var authority = OptionsFor(CreateAccount("primary")).GetTrustedAuthority(MailAccountId.Create("primary"));

        // Assert
        Assert.Equal(TrustedAuthenticationAuthority.None, authority);
    }

    /// <summary>An extraction may run over an account a reload removed, and believing nothing is the answer rather than failing.</summary>
    [Fact]
    public void GetTrustedAuthority_AccountThisSnapshotNoLongerNames_BelievesNothing()
    {
        // Act
        var authority = OptionsFor(CreateAccount("primary")).GetTrustedAuthority(MailAccountId.Create("removed"));

        // Assert
        Assert.Equal(TrustedAuthenticationAuthority.None, authority);
    }

    /// <summary>A value that is present and unusable fails startup, because the alternative is mail that never authenticates.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("mx example test")]
    public void ValidateForSynchronization_UnusableTrustedServiceIdentifier_IsRefused(string configured)
    {
        // Arrange
        var account = CreateAccount("primary");
        account.TrustedAuthenticationServiceIdentifier = configured;

        // Act
        var messages = OptionsFor(account).ValidateForSynchronization().Select(result => result.ErrorMessage).ToArray();

        // Assert
        Assert.Contains(
            messages,
            message => message!.Contains("trusted authentication service identifier", StringComparison.Ordinal));
    }

    /// <summary>The refusal names the account and never the value, which the failure rules refuse as a host name.</summary>
    [Fact]
    public void ValidateForSynchronization_UnusableTrustedServiceIdentifier_DoesNotEchoTheValue()
    {
        // Arrange
        var account = CreateAccount("primary");
        account.TrustedAuthenticationServiceIdentifier = "mx internal example test";

        // Act
        var refusal = Assert.Single(
            OptionsFor(account).ValidateForSynchronization().Select(result => result.ErrorMessage),
            message => message!.Contains("trusted authentication service identifier", StringComparison.Ordinal));

        // Assert
        Assert.DoesNotContain("mx internal example test", refusal, StringComparison.Ordinal);
        Assert.Contains("primary", refusal, StringComparison.Ordinal);
    }

    /// <summary>Omitting the setting is not a mistake, so it produces no startup refusal.</summary>
    [Fact]
    public void ValidateForSynchronization_NoTrustedServiceIdentifier_IsAccepted()
    {
        // Act
        var messages = OptionsFor(CreateAccount("primary"))
            .ValidateForSynchronization()
            .Select(result => result.ErrorMessage)
            .ToArray();

        // Assert
        Assert.DoesNotContain(
            messages,
            message => message!.Contains("trusted authentication service identifier", StringComparison.Ordinal));
    }

    private static MailSynchronizationOptions OptionsFor(params MailSynchronizationAccountOptions[] accounts) => new()
    {
        Accounts = [.. accounts],
    };

    private static MailSynchronizationAccountOptions CreateAccount(string accountId) => new()
    {
        AccountId = accountId,
        DisplayName = $"Account {accountId}",
        Host = "imap.example.test",
        UserName = "mailfathom@example.test",
        Secrets = new MailAccountSecretOptions
        {
            Password = new ConfiguredSecret { SecretReference = $"systemd-credential:imap-{accountId}-password" },
        },
    };
}
