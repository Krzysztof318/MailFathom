// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Secrets.Discovery;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Mail;

/// <summary>Covers how configuration answers which folder of an account an alias or a role names.</summary>
/// <remarks>
/// This is what every feature asking for <em>this account's junk folder</em> reads, so the answers that matter most are
/// the ones it refuses to invent: nothing for a role no folder carries, and nothing for an account nobody configured.
/// </remarks>
public sealed class MailFolderMappingLookupTests
{
    private static readonly MailAccountId Primary = MailAccountId.Create("primary");

    [Fact]
    public void FindFolderPlayingRole_AFolderLabelledWithTheRoleBesideItsPath_AnswersWithThatFolder()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(
            new MailFolderMappingOptions { Alias = "inbox", SpecialUse = "Inbox" },
            new MailFolderMappingOptions { Alias = "spam", RemotePath = "INBOX.Spam", SpecialUse = "Junk" }));

        // Act
        var folder = options.Readers.FolderMappings.FindFolderPlayingRole(Primary, MailFolderSpecialUse.Junk);

        // Assert
        Assert.NotNull(folder);
        Assert.Equal(MailFolderAlias.Create("spam"), folder.Alias);
        Assert.Equal(MailFolderMappingTarget.RemotePath, folder.Target);
    }

    [Fact]
    public void FindFolderPlayingRole_AFolderFoundByTheRole_AnswersWithThatFolder()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions { Alias = "archive", SpecialUse = "Archive" }));

        // Act
        var folder = options.Readers.FolderMappings.FindFolderPlayingRole(Primary, MailFolderSpecialUse.Archive);

        // Assert
        Assert.NotNull(folder);
        Assert.Equal(MailFolderAlias.Create("archive"), folder.Alias);
    }

    /// <summary>Nothing is guessed: not the inbox, not the first mapping, and not a folder whose alias resembles the role.</summary>
    [Fact]
    public void FindFolderPlayingRole_ARoleNoFolderCarries_AnswersWithNothing()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(
            new MailFolderMappingOptions { Alias = "inbox", SpecialUse = "Inbox" },
            new MailFolderMappingOptions { Alias = "junk", RemotePath = "INBOX.Junk" }));

        // Act
        var folder = options.Readers.FolderMappings.FindFolderPlayingRole(Primary, MailFolderSpecialUse.Junk);

        // Assert
        Assert.Null(folder);
    }

    /// <summary>A folder nothing mirrors is exactly the kind a role is given to, so the answer cannot depend on what it takes part in.</summary>
    [Fact]
    public void FindFolderPlayingRole_AFolderNothingMirrors_StillAnswersWithIt()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions
        {
            Alias = "quarantine",
            RemotePath = "INBOX.Quarantine",
            SpecialUse = "Junk",
            Synchronize = false,
        }));

        // Act
        var folder = options.Readers.FolderMappings.FindFolderPlayingRole(Primary, MailFolderSpecialUse.Junk);

        // Assert
        Assert.NotNull(folder);
        Assert.Equal(MailFolderAlias.Create("quarantine"), folder.Alias);
        Assert.False(folder.Participation.IsSynchronized);
    }

    /// <summary>The default mapping an account is actually run with is the one a lookup has to see.</summary>
    [Fact]
    public void FindFolderPlayingRole_AnAccountConfiguringNoFolder_AnswersWithThePostBindingInbox()
    {
        // Arrange
        var options = OptionsFor(CreateAccount());

        // Act
        var folder = options.Readers.FolderMappings.FindFolderPlayingRole(Primary, MailFolderSpecialUse.Inbox);

        // Assert
        Assert.NotNull(folder);
        Assert.Equal(MailFolderAlias.Create("INBOX"), folder.Alias);
    }

    [Fact]
    public void FindFolderPlayingRole_AnAccountThisConfigurationDoesNotName_AnswersWithNothing()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions { Alias = "junk", SpecialUse = "Junk" }));

        // Act
        var folder = options.Readers.FolderMappings.FindFolderPlayingRole(MailAccountId.Create("withdrawn"), MailFolderSpecialUse.Junk);

        // Assert
        Assert.Null(folder);
    }

    [Fact]
    public void FindFolderNamed_AConfiguredAlias_AnswersWithThatFolderWhateverCaseItWasWrittenIn()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(
            new MailFolderMappingOptions { Alias = "spam", RemotePath = "INBOX.Spam", SpecialUse = "Junk" }));

        // Act
        var folder = options.Readers.FolderMappings.FindFolderNamed(Primary, MailFolderAlias.Create("SpAm"));

        // Assert
        Assert.NotNull(folder);
        Assert.Equal(MailFolderSpecialUse.Junk, folder.SpecialUse);
    }

    [Fact]
    public void FindFolderNamed_AnAliasConfigurationNoLongerNames_AnswersWithNothing()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions { Alias = "inbox", SpecialUse = "Inbox" }));

        // Act
        var folder = options.Readers.FolderMappings.FindFolderNamed(Primary, MailFolderAlias.Create("archive"));

        // Assert
        Assert.Null(folder);
    }

    /// <summary>Startup refuses such an entry, so the only way to meet one is a reload being rejected; a lookup must not raise over it.</summary>
    [Fact]
    public void FindFolderNamed_AFolderNamingNeitherAPathNorARole_IsSkippedRatherThanRaisedOver()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(
            new MailFolderMappingOptions { Alias = "broken" },
            new MailFolderMappingOptions { Alias = "inbox", SpecialUse = "Inbox" }));

        // Act
        var broken = options.Readers.FolderMappings.FindFolderNamed(Primary, MailFolderAlias.Create("broken"));
        var inbox = options.Readers.FolderMappings.FindFolderPlayingRole(Primary, MailFolderSpecialUse.Inbox);

        // Assert
        Assert.Null(broken);
        Assert.NotNull(inbox);
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
