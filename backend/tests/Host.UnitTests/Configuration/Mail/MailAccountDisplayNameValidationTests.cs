// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Synchronization;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Secrets.Discovery;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Mail;

/// <summary>Covers the rules that keep every configured account publishable under exactly one readable name.</summary>
public sealed class MailAccountDisplayNameValidationTests
{
    /// <summary>There is no fallback to the identifier, because a name MailFathom invented would be published as though an operator had chosen it.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateForSynchronization_AccountWithNoDisplayName_IsRefusedWhateverTheIdentifier(string configured)
    {
        // Arrange
        var options = OptionsFor(CreateAccount("primary", configured));

        // Act
        var messages = options.ValidateForSynchronization().Select(result => result.ErrorMessage).ToArray();

        // Assert
        Assert.Contains(messages, message => message!.Contains("a display name is required", StringComparison.Ordinal));
    }

    /// <summary>The stored copy stays readable after synchronization is switched off, so the account still needs the name it is published under.</summary>
    [Fact]
    public void ValidateForSynchronization_SynchronizationDisabled_StillRequiresTheDisplayName()
    {
        // Arrange
        var options = OptionsFor(CreateAccount("primary", displayName: string.Empty));
        options.Enabled = false;

        // Act
        var messages = options.ValidateForSynchronization().Select(result => result.ErrorMessage).ToArray();

        // Assert
        Assert.Contains(messages, message => message!.Contains("a display name is required", StringComparison.Ordinal));
    }

    /// <summary>The domain owns the rules and the message reports which one broke without repeating the text that broke it.</summary>
    [Theory]
    [InlineData("Work\nmail")]
    [InlineData("Work\tmail")]
    public void ValidateForSynchronization_DisplayNameCarryingAControlCharacter_IsRefusedWithoutEchoingIt(string configured)
    {
        // Arrange
        var options = OptionsFor(CreateAccount("primary", configured));

        // Act
        var messages = options.ValidateForSynchronization().Select(result => result.ErrorMessage).ToArray();

        // Assert
        var refusal = Assert.Single(messages, message => message!.Contains("display name is not usable", StringComparison.Ordinal));
        Assert.DoesNotContain('\n', refusal!.Replace("\r\n", string.Empty, StringComparison.Ordinal));
        Assert.DoesNotContain('\t', refusal);
    }

    [Fact]
    public void ValidateForSynchronization_DisplayNameBeyondTheLengthBound_IsRefused()
    {
        // Arrange
        var options = OptionsFor(
            CreateAccount("primary", new string('m', MailAccountDisplayName.MaximumLength + 1)));

        // Act
        var messages = options.ValidateForSynchronization().Select(result => result.ErrorMessage).ToArray();

        // Assert
        Assert.Contains(messages, message => message!.Contains("display name is not usable", StringComparison.Ordinal));
    }

    /// <summary>One name may never select two mailboxes, so a display name another account already answers to fails startup.</summary>
    [Theory]
    [InlineData("Work mail")]
    [InlineData("WORK MAIL")]
    public void ValidateForSynchronization_TwoAccountsSharingADisplayName_AreRefusedWhateverTheCase(string repeated)
    {
        // Arrange
        var options = OptionsFor(
            CreateAccount("acct-1", "Work mail"),
            CreateAccount("acct-2", repeated));

        // Act
        var messages = options.ValidateForSynchronization().Select(result => result.ErrorMessage).ToArray();

        // Assert
        Assert.Contains(
            messages,
            message => message!.Contains("could not say which mailbox it meant", StringComparison.Ordinal));
    }

    /// <summary>The identifiers share the naming space, so a display name spelling another account's identifier is the same ambiguity.</summary>
    [Fact]
    public void ValidateForSynchronization_DisplayNameSpellingAnotherAccountsIdentifier_IsRefused()
    {
        // Arrange
        var options = OptionsFor(
            CreateAccount("acct-1", "Work mail"),
            CreateAccount("acct-2", "ACCT-1"));

        // Act
        var messages = options.ValidateForSynchronization().Select(result => result.ErrorMessage).ToArray();

        // Assert
        Assert.Contains(
            messages,
            message => message!.Contains("could not say which mailbox it meant", StringComparison.Ordinal));
    }

    /// <summary>Both spellings then reach the same mailbox, so an operator whose identifier already reads well writes it twice rather than inventing a second one.</summary>
    [Fact]
    public void ValidateForSynchronization_DisplayNameEqualToItsOwnIdentifier_IsAccepted()
    {
        // Arrange
        var options = OptionsFor(CreateAccount("primary", "primary"), CreateAccount("acct-2", "Work mail"));

        // Act
        var messages = options.ValidateForSynchronization().Select(result => result.ErrorMessage).ToArray();

        // Assert
        Assert.DoesNotContain(
            messages,
            message => message!.Contains("could not say which mailbox it meant", StringComparison.Ordinal));
    }

    /// <summary>A configured account reaches every reader under the name it was given, together with the mode it was configured for.</summary>
    [Fact]
    public void ServedAccounts_ConfiguredAccounts_CarryTheirDisplayNameAndMode()
    {
        // Arrange
        var pushed = CreateAccount("acct-2", "Private mail");
        pushed.Mode = MailSynchronizationMode.Push;
        var options = OptionsFor(CreateAccount("acct-1", "  Work mail  "), pushed);

        // Act
        var servedAccounts = ConfiguredMailAccounts.CatalogOver(options).ServedAccounts;

        // Assert
        Assert.Equal(
            [
                ("acct-1", "Work mail", MailSynchronizationMode.Polling),
                ("acct-2", "Private mail", MailSynchronizationMode.Push),
            ],
            servedAccounts.Select(account => (account.Id.Value, account.DisplayName.Value, account.SynchronizationMode)));
    }

    /// <summary>Reading the set never fails on configuration startup refuses; an unnamed account is left out rather than named by MailFathom.</summary>
    [Fact]
    public void ServedAccounts_AccountWithNoUsableDisplayName_IsSkippedRatherThanNamedByDefault()
    {
        // Arrange
        var options = OptionsFor(CreateAccount("acct-1", "Work mail"), CreateAccount("acct-2", displayName: string.Empty));

        // Act
        var servedAccounts = ConfiguredMailAccounts.CatalogOver(options).ServedAccounts;

        // Assert
        Assert.Equal("acct-1", Assert.Single(servedAccounts).Id.Value);
    }

    /// <summary>The switch is a fact about the deployment rather than about any account, and every reader takes it from the same place.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SynchronizationEnabled_ConfiguredSwitch_IsWhatTheCatalogReports(bool enabled)
    {
        // Arrange
        var options = OptionsFor(CreateAccount("primary", "Work mail"));
        options.Enabled = enabled;

        // Act, Assert
        Assert.Equal(enabled, ConfiguredMailAccounts.CatalogOver(options).SynchronizationEnabled);
    }

    private static MailSynchronizationOptions OptionsFor(params MailSynchronizationAccountOptions[] accounts) => new()
    {
        Accounts = [.. accounts],
    };

    private static MailSynchronizationAccountOptions CreateAccount(string accountId, string displayName) => new()
    {
        AccountId = accountId,
        DisplayName = displayName,
        Host = "imap.example.test",
        UserName = "mailfathom@example.test",
        Secrets = new MailAccountSecretOptions
        {
            Password = new ConfiguredSecret { SecretReference = $"systemd-credential:imap-{accountId}-password" },
        },
    };
}
