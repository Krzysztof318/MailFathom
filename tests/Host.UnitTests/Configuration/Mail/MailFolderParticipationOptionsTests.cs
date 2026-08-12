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

    /// <summary>A folder nothing mirrors takes part in nothing, so it appears in both exclusions without either being configured.</summary>
    [Fact]
    public void GetParticipation_AFolderNothingMirrors_IsWithdrawnFromEmbeddingAndFromTools()
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
