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

/// <summary>Covers the three switches a folder mapping carries and how configuration answers for them.</summary>
public sealed class MailFolderParticipationOptionsTests
{
    private static readonly MailAccountId Primary = MailAccountId.Create("primary");

    /// <summary>An account that configures no folder gets the inbox by role, and nothing about it is withheld.</summary>
    [Fact]
    public void GetParticipation_AnAccountConfiguringNoFolder_TakesPartInEverything()
    {
        // Arrange
        var options = OptionsFor(CreateAccount());

        // Act
        var participation = options.GetParticipation(Primary, MailFolderAlias.Create("INBOX"));

        // Assert
        Assert.Equal(MailFolderParticipation.Full, participation);
        Assert.Empty(options.FoldersHiddenFromTools);
        Assert.Empty(options.FoldersWithoutEmbeddings);
        Assert.Empty(options.FoldersNotMirrored);
    }

    /// <summary>A folder that names its target and no switch behaves exactly as it did before the switches existed.</summary>
    [Fact]
    public void GetParticipation_AFolderConfiguringNoSwitch_TakesPartInEverything()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions
        {
            Alias = "archive",
            SpecialUse = "Archive",
        }));

        // Act
        var participation = options.GetParticipation(Primary, MailFolderAlias.Create("ARCHIVE"));

        // Assert
        Assert.Equal(MailFolderParticipation.Full, participation);
    }

    /// <summary>Withholding a folder from tools leaves everything else about it alone, which is the whole point of the switch being its own.</summary>
    [Fact]
    public void FoldersHiddenFromTools_AFolderWithdrawnFromTools_NamesThatFolderAndKeepsItMirrored()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions
        {
            Alias = "private",
            RemotePath = "Private",
            VisibleToTools = false,
        }));

        // Act
        var hidden = options.FoldersHiddenFromTools;
        var participation = options.GetParticipation(Primary, MailFolderAlias.Create("PRIVATE"));

        // Assert
        Assert.Equal([new MailFolderIdentity(Primary, MailFolderAlias.Create("PRIVATE"))], hidden);
        Assert.True(participation.IsSynchronized);
        Assert.True(participation.GeneratesEmbeddings);
        Assert.False(participation.IsVisibleToTools);
        Assert.Empty(options.FoldersWithoutEmbeddings);
    }

    /// <summary>A noisy folder can stay listed and filterable while costing no provider tokens.</summary>
    [Fact]
    public void FoldersWithoutEmbeddings_AFolderWithdrawnFromEmbedding_NamesThatFolderAndKeepsItReadable()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions
        {
            Alias = "newsletters",
            RemotePath = "Newsletters",
            GenerateEmbeddings = false,
        }));

        // Act
        var participation = options.GetParticipation(Primary, MailFolderAlias.Create("NEWSLETTERS"));

        // Assert
        Assert.Equal(
            [new MailFolderIdentity(Primary, MailFolderAlias.Create("NEWSLETTERS"))],
            options.FoldersWithoutEmbeddings);
        Assert.False(participation.GeneratesEmbeddings);
        Assert.True(participation.IsVisibleToTools);
        Assert.Empty(options.FoldersHiddenFromTools);
    }

    /// <summary>
    /// A folder nothing mirrors takes part in nothing, so it appears in every exclusion without any of them being
    /// configured. That is what keeps the mail it stored before the switch was flipped inert while it is kept: no tool
    /// reads it, nothing embeds it, and no rule pass walks it.
    /// </summary>
    [Fact]
    public void GetParticipation_AFolderNothingMirrors_IsWithdrawnFromEveryReaderThereIs()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions
        {
            Alias = "junk",
            SpecialUse = "Junk",
            Synchronize = false,
        }));
        var junk = new MailFolderIdentity(Primary, MailFolderAlias.Create("JUNK"));

        // Act
        var participation = options.GetParticipation(Primary, MailFolderAlias.Create("JUNK"));

        // Assert
        Assert.Equal(MailFolderParticipation.MappedOnly, participation);
        Assert.Equal([junk], options.FoldersHiddenFromTools);
        Assert.Equal([junk], options.FoldersWithoutEmbeddings);
        Assert.Equal([junk], options.FoldersNotMirrored);
    }

    /// <summary>
    /// A mirrored folder withheld from tools is not a folder nothing mirrors, so the list a rule walk narrows by names
    /// it as little as the list a tool narrows by names a folder that is merely unembedded.
    /// </summary>
    [Fact]
    public void FoldersNotMirrored_AFolderWithdrawnFromToolsOrEmbedding_NamesNeither()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(
            new MailFolderMappingOptions { Alias = "private", RemotePath = "Private", VisibleToTools = false },
            new MailFolderMappingOptions
            {
                Alias = "newsletters",
                RemotePath = "Newsletters",
                GenerateEmbeddings = false,
            }));

        // Act, Assert
        Assert.Empty(options.FoldersNotMirrored);
    }

    /// <summary>A folder nothing maps is stored mail nobody withdrew anything from, so a removed mapping never hides a mailbox.</summary>
    [Fact]
    public void GetParticipation_AnAliasNothingMaps_TakesPartInEverything()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions
        {
            Alias = "junk",
            SpecialUse = "Junk",
            Synchronize = false,
        }));

        // Act
        var participation = options.GetParticipation(Primary, MailFolderAlias.Create("ARCHIVE"));

        // Assert
        Assert.Equal(MailFolderParticipation.Full, participation);
    }

    /// <summary>One account's decision is never another account's, which is what makes the identity a pair rather than an alias.</summary>
    [Fact]
    public void FoldersHiddenFromTools_TheSameAliasInTwoAccounts_NamesOnlyTheAccountThatWithheldIt()
    {
        // Arrange
        var options = new MailSynchronizationOptions
        {
            Accounts =
            [
                CreateAccount(new MailFolderMappingOptions
                {
                    Alias = "private",
                    RemotePath = "Private",
                    VisibleToTools = false,
                }),
                CreateAccount("secondary", new MailFolderMappingOptions
                {
                    Alias = "private",
                    RemotePath = "Private",
                }),
            ],
        };

        // Act
        var hidden = options.FoldersHiddenFromTools;

        // Assert
        Assert.Equal([new MailFolderIdentity(Primary, MailFolderAlias.Create("PRIVATE"))], hidden);
        Assert.True(options
            .GetParticipation(MailAccountId.Create("secondary"), MailFolderAlias.Create("PRIVATE"))
            .IsVisibleToTools);
    }

    /// <summary>Asking for something an unmirrored folder cannot do is refused where it binds, because the configuration would not do what it says.</summary>
    [Theory]
    [InlineData(true, null, "embeddings")]
    [InlineData(null, true, "visible to tools")]
    public void ValidateForSynchronization_AnUnmirroredFolderAskingForMore_IsRefusedNamingTheFolder(
        bool? generateEmbeddings,
        bool? visibleToTools,
        string expectedFragment)
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions
        {
            Alias = "junk",
            SpecialUse = "Junk",
            Synchronize = false,
            GenerateEmbeddings = generateEmbeddings,
            VisibleToTools = visibleToTools,
        }));

        // Act
        var messages = options.ValidateForSynchronization().Select(result => result.ErrorMessage!).ToArray();

        // Assert
        Assert.Contains(
            messages,
            message => message.Contains("'junk'", StringComparison.Ordinal)
                && message.Contains(expectedFragment, StringComparison.Ordinal));
    }

    /// <summary>Leaving a switch out is not asking for it, so the ordinary way to stop mirroring a folder is not a contradiction.</summary>
    [Fact]
    public void ValidateForSynchronization_AnUnmirroredFolderAskingForNothingElse_ReportsNoError()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions
        {
            Alias = "junk",
            SpecialUse = "Junk",
            Synchronize = false,
        }));

        // Act
        var results = options.ValidateForSynchronization().ToArray();

        // Assert
        Assert.Empty(results);
    }

    /// <summary>An embedded folder no tool may read is a cost with no current reader rather than a mistake, so it binds.</summary>
    [Fact]
    public void ValidateForSynchronization_AFolderEmbeddedButInvisible_ReportsNoError()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions
        {
            Alias = "private",
            RemotePath = "Private",
            GenerateEmbeddings = true,
            VisibleToTools = false,
        }));

        // Act
        var results = options.ValidateForSynchronization().ToArray();

        // Assert
        Assert.Empty(results);
    }

    /// <summary>
    /// A folder that does not exist advertises no role, so creating one from a role would mean either an extension
    /// whose support is uneven or MailFathom inventing a name in somebody's own mailbox. Writing the path the folder is
    /// to be created at is one line of configuration, so the contradiction is refused where it binds.
    /// </summary>
    [Fact]
    public void ValidateForSynchronization_ARoleMappingAskingToBeCreated_IsRefusedNamingTheFolder()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions
        {
            Alias = "junk",
            SpecialUse = "Junk",
            CreateIfMissing = true,
        }));

        // Act
        var messages = options.ValidateForSynchronization().Select(result => result.ErrorMessage!).ToArray();

        // Assert
        Assert.Contains(
            messages,
            message => message.Contains("'junk'", StringComparison.Ordinal)
                && message.Contains("RemotePath", StringComparison.Ordinal));
    }

    /// <summary>The switch belongs to a configured path, which is the only mapping that names where a folder would be created.</summary>
    [Fact]
    public void ValidateForSynchronization_APathMappingAskingToBeCreated_ReportsNoError()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions
        {
            Alias = "archive",
            RemotePath = "Archief/2026",
            CreateIfMissing = true,
        }));

        // Act
        var results = options.ValidateForSynchronization().ToArray();

        // Assert
        Assert.Empty(results);
    }

    /// <summary>Explicitly declining a creation beside a role says nothing a role mapping cannot do, so it is not the contradiction above.</summary>
    [Fact]
    public void ValidateForSynchronization_ARoleMappingDecliningCreation_ReportsNoError()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions
        {
            Alias = "junk",
            SpecialUse = "Junk",
            CreateIfMissing = false,
        }));

        // Act
        var results = options.ValidateForSynchronization().ToArray();

        // Assert
        Assert.Empty(results);
    }

    /// <summary>The junk role is read from what an operator configured, so a folder mapped to it is the one withheld.</summary>
    [Fact]
    public void JunkFolders_AFolderMappedToTheJunkRole_NamesThatFolder()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions
        {
            Alias = "junk",
            SpecialUse = "Junk",
        }));

        // Act, Assert
        Assert.Equal([new MailFolderIdentity(Primary, MailFolderAlias.Create("JUNK"))], options.JunkFolders);
        Assert.True(options.IsJunkFolder(Primary, MailFolderAlias.Create("JUNK")));
        Assert.False(options.IsJunkFolder(Primary, MailFolderAlias.Create("INBOX")));
    }

    /// <summary>A deployment that maps no junk folder answers with nothing, and every mailbox read behaves as it did before.</summary>
    [Fact]
    public void JunkFolders_NoFolderMappedToTheJunkRole_NamesNothing()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions
        {
            Alias = "archive",
            SpecialUse = "Archive",
        }));

        // Act, Assert
        Assert.Empty(options.JunkFolders);
        Assert.False(options.IsJunkFolder(Primary, MailFolderAlias.Create("ARCHIVE")));
    }

    /// <summary>An account that configures no folder still runs with an inbox mapping, which is what classification defaults to.</summary>
    [Fact]
    public void InboxFolderAliases_AnAccountConfiguringNoFolder_NamesTheInboxMappingItRunsWith()
    {
        // Arrange
        var options = OptionsFor(CreateAccount());

        // Act, Assert
        Assert.Equal([MailFolderAlias.Create("INBOX")], options.InboxFolderAliases);
    }

    /// <summary>A server presenting the inbox under another name is configured by role, and the default scope follows the role.</summary>
    [Fact]
    public void InboxFolderAliases_AnInboxMappedUnderAnotherAlias_NamesTheConfiguredAlias()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions
        {
            Alias = "primary-mail",
            RemotePath = "Skrzynka odbiorcza",
            SpecialUse = "Inbox",
        }));

        // Act, Assert
        Assert.Equal([MailFolderAlias.Create("PRIMARY-MAIL")], options.InboxFolderAliases);
    }

    private static MailSynchronizationOptions OptionsFor(MailSynchronizationAccountOptions account) =>
        new() { Accounts = [account] };

    private static MailSynchronizationAccountOptions CreateAccount(params MailFolderMappingOptions[] folders) =>
        CreateAccount("primary", folders);

    private static MailSynchronizationAccountOptions CreateAccount(
        string accountId,
        params MailFolderMappingOptions[] folders) => new()
        {
            AccountId = accountId,
            DisplayName = $"The {accountId} mailbox",
            Host = "imap.example.test",
            UserName = "mailfathom@example.test",
            Secrets = new MailAccountSecretOptions
            {
                Password = new ConfiguredSecret { SecretReference = $"systemd-credential:imap-{accountId}-password" },
            },
            Folders = [.. folders],
        };
}
