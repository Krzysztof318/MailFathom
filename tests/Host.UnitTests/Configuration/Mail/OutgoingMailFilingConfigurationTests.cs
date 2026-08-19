// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Secrets.Discovery;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Mail;

/// <summary>Covers what configuration decides about the copies MailFathom puts into an account's own folders.</summary>
public sealed class OutgoingMailFilingConfigurationTests
{
    private static readonly MailAccountId Primary = MailAccountId.Create("primary");

    /// <summary>
    /// RFC 6154 declares no outbox attribute, so nothing discovery could look for exists. Such a mapping would resolve
    /// to nothing and the operator would learn that from mail they never saw mirrored, which is why it is refused where
    /// it binds instead.
    /// </summary>
    [Fact]
    public void ValidateForSynchronization_AnOutboxRoleWithNoPath_IsRefusedNamingTheFolder()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions
        {
            Alias = "outbox",
            SpecialUse = "Outbox",
        }));

        // Act
        var messages = options.ValidateForSynchronization().Select(result => result.ErrorMessage!).ToArray();

        // Assert
        Assert.Contains(
            messages,
            message => message.Contains("'outbox'", StringComparison.Ordinal)
                && message.Contains("RemotePath", StringComparison.Ordinal));
    }

    /// <summary>Beside a path the role is a label like any other, which is how a deployment says which folder holds what is waiting.</summary>
    [Fact]
    public void ValidateForSynchronization_AnOutboxRoleOnAConfiguredPath_ReportsNoError()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions
        {
            Alias = "outbox",
            RemotePath = "INBOX.Outbox",
            SpecialUse = "Outbox",
        }));

        // Act
        var results = options.ValidateForSynchronization().ToArray();

        // Assert
        Assert.Empty(results);
    }

    /// <summary>The role is read from what the operator wrote, so the mirror has a folder to go into.</summary>
    [Fact]
    public void FindFolderPlayingRole_AnOutboxMapping_AnswersWithTheFolderTheOperatorNamed()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions
        {
            Alias = "outbox",
            RemotePath = "INBOX.Outbox",
            SpecialUse = "Outbox",
        }));

        // Act
        var folder = options.FindFolderPlayingRole(Primary, MailFolderSpecialUse.Outbox);

        // Assert
        Assert.NotNull(folder);
        Assert.Equal(MailFolderAlias.Create("outbox"), folder.Alias);
    }

    /// <summary>
    /// A deployment that says nothing maps no outbox folder, which is what keeps the mirror off by default — and a
    /// provider folder merely named like one carries no role, because nothing here reads a folder's name.
    /// </summary>
    [Fact]
    public void FindFolderPlayingRole_AFolderMerelyNamedLikeAnOutbox_PlaysNoOutboxRole()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions
        {
            Alias = "outbox",
            RemotePath = "INBOX.Outbox",
        }));

        // Act
        var folder = options.FindFolderPlayingRole(Primary, MailFolderSpecialUse.Outbox);

        // Assert
        Assert.Null(folder);
    }

    /// <summary>
    /// An account that says nothing about it files the copy, because a submission server files nothing and a deployment
    /// that appended nothing would leave the owner with mail they sent and no record of it.
    /// </summary>
    [Fact]
    public void FilesSentCopy_AnAccountThatSaysNothing_FilesTheCopy()
    {
        // Arrange
        var options = OptionsFor(CreateAccount());

        // Act
        var filesSentCopy = options.FilesSentCopy(Primary);

        // Assert
        Assert.True(filesSentCopy);
    }

    /// <summary>Turning it off is the account whose provider files the copy itself, which is configured and never detected.</summary>
    [Fact]
    public void FilesSentCopy_AnAccountThatTurnsItOff_FilesNothing()
    {
        // Arrange
        var account = CreateAccount();
        account.Delivery = new MailAccountDeliveryOptions
        {
            Host = "smtp.example.test",
            FileSentCopy = false,
        };

        // Act
        var filesSentCopy = OptionsFor(account).FilesSentCopy(Primary);

        // Assert
        Assert.False(filesSentCopy);
    }

    /// <summary>Nothing can send as an account nobody configured, so the answer is reached only by a caller asking about a message that cannot exist.</summary>
    [Fact]
    public void FilesSentCopy_AnAccountNothingConfigures_AnswersAsThoughItFiles()
    {
        // Arrange
        var options = OptionsFor(CreateAccount());

        // Act
        var filesSentCopy = options.FilesSentCopy(MailAccountId.Create("withdrawn"));

        // Assert
        Assert.True(filesSentCopy);
    }

    private static MailSynchronizationOptions OptionsFor(MailSynchronizationAccountOptions account) =>
        new() { Accounts = [account] };

    private static MailSynchronizationAccountOptions CreateAccount(params MailFolderMappingOptions[] folders) => new()
    {
        AccountId = "primary",
        DisplayName = "The primary mailbox",
        Host = "imap.example.test",
        UserName = "mailfathom@example.test",
        Secrets = new MailAccountSecretOptions
        {
            Password = new ConfiguredSecret { SecretReference = "systemd-credential:imap-primary-password" },
        },
        Folders = [.. folders],
    };
}
