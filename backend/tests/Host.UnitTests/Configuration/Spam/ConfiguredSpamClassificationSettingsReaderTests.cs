// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.OwnerSettings;
using MailFathom.Host.Configuration.Spam;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Spam;

/// <summary>Covers how each owner's source becomes the settings a classification of their mail runs with.</summary>
public sealed class ConfiguredSpamClassificationSettingsReaderTests
{
    /// <summary>An operator who wrote no folder asked for the default, which is whichever alias each account maps to its inbox.</summary>
    [Fact]
    public void SettingsFor_NoScannedFolderConfigured_TakesEachAccountsInboxAlias()
    {
        // Arrange
        var reader = ReaderFor(
            new SpamClassificationOptions { Enabled = true },
            AccountMapping("primary-mail", "Inbox"));

        // Act
        var settings = reader.SettingsFor(SyntheticMailOwner.Deployment);

        // Assert
        Assert.True(settings.IsEnabled);
        Assert.Equal([MailFolderAlias.Create("PRIMARY-MAIL")], settings.ScannedFolderAliases);
    }

    /// <summary>The bound on the ordering is the operator's, and it is what stops a wedged scanner stopping the index.</summary>
    [Fact]
    public void ScopeInForce_AClassificationWaitConfigured_CarriesItToTheGate()
    {
        // Arrange
        var reader = ReaderFor(
            new SpamClassificationOptions { Enabled = true, ClassificationWait = TimeSpan.FromHours(2) },
            AccountMapping("inbox", "Inbox"));

        // Act
        var scope = reader.ScopeInForce;

        // Assert
        Assert.Equal(TimeSpan.FromHours(2), scope.MaximumClassificationWait);
    }

    /// <summary>An operator who named no wait gets one anyway, because a wait of none would release every message.</summary>
    [Fact]
    public void ScopeInForce_NoClassificationWaitConfigured_TakesTheDefaultWait()
    {
        // Arrange
        var reader = ReaderFor(
            new SpamClassificationOptions { Enabled = true },
            AccountMapping("inbox", "Inbox"));

        // Act
        var scope = reader.ScopeInForce;

        // Assert
        Assert.Equal(
            SpamClassificationScope.DefaultMaximumClassificationWait,
            scope.MaximumClassificationWait);
    }

    /// <summary>An operator who wrote no folders at all asked for none, which the default must not quietly overrule.</summary>
    [Fact]
    public void SettingsFor_AnExplicitlyEmptyScannedFolderList_ClassifiesNoFolder()
    {
        // Arrange
        var reader = ReaderFor(
            new SpamClassificationOptions { Enabled = true, ScannedFolders = [] },
            AccountMapping("inbox", "Inbox"));

        // Act
        var settings = reader.SettingsFor(SyntheticMailOwner.Deployment);

        // Assert
        Assert.True(settings.IsEnabled);
        Assert.Empty(settings.ScannedFolderAliases);
    }

    [Fact]
    public void SettingsFor_ScannedFoldersConfigured_TakesThemRatherThanTheInbox()
    {
        // Arrange
        var reader = ReaderFor(
            new SpamClassificationOptions
            {
                Enabled = true,
                UseScanner = true,
                ScannedFolders = ["junk", "inbox"],
                ScannerThreshold = 7.5,
            },
            AccountMapping("inbox", "Inbox"));

        // Act
        var settings = reader.SettingsFor(SyntheticMailOwner.Deployment);

        // Assert
        Assert.True(settings.UsesScanner);
        Assert.Equal(7.5, settings.ScannerThreshold);
        Assert.Equal(
            [MailFolderAlias.Create("INBOX"), MailFolderAlias.Create("JUNK")],
            settings.ScannedFolderAliases);
    }

    /// <summary>A section reloaded while the process runs takes effect on the next classification rather than at the next restart.</summary>
    [Fact]
    public void SettingsFor_ASectionReloaded_IsReadAgainRatherThanCaptured()
    {
        // Arrange
        var options = new TestOptionsMonitor<SpamClassificationOptions>(new SpamClassificationOptions());
        var reader = new ConfiguredSpamClassificationSettingsReader(
            options,
            SynchronizationOptionsWith(AccountMapping("inbox", "Inbox")));

        // Act
        var beforeReload = reader.SettingsFor(SyntheticMailOwner.Deployment);

        options.ReportReload(new SpamClassificationOptions { Enabled = true });

        var afterReload = reader.SettingsFor(SyntheticMailOwner.Deployment);

        // Assert
        Assert.False(beforeReload.IsEnabled);
        Assert.True(afterReload.IsEnabled);
    }

    /// <summary>Nobody is served a posture by default, so an owner this deployment does not hold classifies nothing.</summary>
    [Fact]
    public void SettingsFor_AnOwnerThisDeploymentDoesNotServe_ClassifiesNothing()
    {
        // Arrange
        var reader = ReaderFor(
            new SpamClassificationOptions { Enabled = true },
            AccountMapping("inbox", "Inbox"));

        // Act
        var settings = reader.SettingsFor(SyntheticMailOwner.Another);

        // Assert
        Assert.False(settings.IsEnabled);
        Assert.Empty(settings.ScannedFolderAliases);
    }

    [Fact]
    public void SettingsFor_NoOwner_Throws()
    {
        // Arrange
        var reader = ReaderFor(new SpamClassificationOptions { Enabled = true }, AccountMapping("inbox", "Inbox"));

        // Act, Assert
        Assert.Throws<ArgumentException>(() => reader.SettingsFor(default));
    }

    /// <summary>An owner whose document has been written is read from it, and the deployment's section stops reaching them.</summary>
    [Fact]
    public void SettingsFor_AnOwnerWhoseDocumentWasWritten_TakesTheBlockThatDocumentCarries()
    {
        // Arrange
        var reader = new ConfiguredSpamClassificationSettingsReader(
            new TestOptionsMonitor<SpamClassificationOptions>(new SpamClassificationOptions
            {
                Enabled = true,
                ScannedFolders = ["inbox"],
            }),
            RosterOf(DocumentOwner(
                SyntheticMailOwner.Another,
                new OwnerSpamClassificationOptions
                {
                    Enabled = true,
                    UseScanner = true,
                    ScannedFolders = ["archive"],
                    ScannerThreshold = 3.5,
                },
                "second-account",
                AccountMapping("archive", "Archive"))));

        // Act
        var settings = reader.SettingsFor(SyntheticMailOwner.Another);

        // Assert
        Assert.True(settings.UsesScanner);
        Assert.Equal(3.5, settings.ScannerThreshold);
        Assert.Equal([MailFolderAlias.Create("ARCHIVE")], settings.ScannedFolderAliases);
    }

    /// <summary>Switching classification off in a written record actually switches it off, rather than reverting to the file.</summary>
    [Fact]
    public void SettingsFor_AnOwnerWhoseDocumentSwitchedClassificationOff_ClassifiesNothingWhileTheSectionStaysOn()
    {
        // Arrange
        var reader = new ConfiguredSpamClassificationSettingsReader(
            new TestOptionsMonitor<SpamClassificationOptions>(new SpamClassificationOptions { Enabled = true }),
            RosterOf(DocumentOwner(
                SyntheticMailOwner.Another,
                new OwnerSpamClassificationOptions { Enabled = false },
                "second-account",
                AccountMapping("inbox", "Inbox"))));

        // Act
        var settings = reader.SettingsFor(SyntheticMailOwner.Another);

        // Assert
        Assert.False(settings.IsEnabled);
    }

    /// <summary>The wait bounds how long the index may be held back, which is the process's cost rather than one owner's choice.</summary>
    [Fact]
    public void ScopeInForce_EveryOwnerReadFromTheirOwnDocument_StillTakesTheDeploymentsClassificationWait()
    {
        // Arrange
        var reader = new ConfiguredSpamClassificationSettingsReader(
            new TestOptionsMonitor<SpamClassificationOptions>(new SpamClassificationOptions
            {
                ClassificationWait = TimeSpan.FromHours(3),
            }),
            RosterOf(DocumentOwner(
                SyntheticMailOwner.Another,
                new OwnerSpamClassificationOptions { Enabled = true },
                "second-account",
                AccountMapping("inbox", "Inbox"))));

        // Act
        var scope = reader.ScopeInForce;

        // Assert
        Assert.Equal(TimeSpan.FromHours(3), scope.MaximumClassificationWait);
    }

    /// <summary>The scope a walk narrows by is composed per owner, so one owner's decision reaches a query spanning owners.</summary>
    [Fact]
    public void ScopeInForce_TwoOwnersWithDifferentPostures_NamesOnlyTheClassifyingOwnersAccounts()
    {
        // Arrange
        var reader = new ConfiguredSpamClassificationSettingsReader(
            new TestOptionsMonitor<SpamClassificationOptions>(new SpamClassificationOptions()),
            RosterOf(
                DocumentOwner(
                    SyntheticMailOwner.Deployment,
                    new OwnerSpamClassificationOptions { Enabled = true, ScannedFolders = ["inbox"] },
                    "first-account",
                    AccountMapping("inbox", "Inbox"),
                    AccountMapping("archive", "Archive")),
                DocumentOwner(
                    SyntheticMailOwner.Another,
                    new OwnerSpamClassificationOptions { Enabled = false },
                    "second-account",
                    AccountMapping("inbox", "Inbox"))));

        // Act
        var scope = reader.ScopeInForce;

        // Assert
        Assert.Equal([MailAccountId.Create("first-account")], scope.ClassifyingAccounts);
        Assert.Equal(
            [new MailFolderIdentity(MailAccountId.Create("first-account"), MailFolderAlias.Create("INBOX"))],
            scope.ClassifiedFolders);
    }

    /// <summary>Nothing classifies before the startup gate publishes the roster, which is the answer every path takes until it has.</summary>
    [Fact]
    public void ScopeInForce_ARosterThatHasNotSettled_ClassifiesNothing()
    {
        // Arrange
        var reader = new ConfiguredSpamClassificationSettingsReader(
            new TestOptionsMonitor<SpamClassificationOptions>(new SpamClassificationOptions { Enabled = true }),
            new MailSynchronizationOptions());

        // Act
        var scope = reader.ScopeInForce;

        // Assert
        Assert.Empty(scope.ClassifyingAccounts);
        Assert.Empty(scope.ClassifiedFolders);
    }

    private static ConfiguredSpamClassificationSettingsReader ReaderFor(
        SpamClassificationOptions options,
        params MailFolderMappingOptions[] folders) =>
        new(
            new TestOptionsMonitor<SpamClassificationOptions>(options),
            SynchronizationOptionsWith(folders));

    private static MailFolderMappingOptions AccountMapping(string alias, string specialUse) => new()
    {
        Alias = alias,
        SpecialUse = specialUse,
    };

    private static MailSynchronizationOptions SynchronizationOptionsWith(params MailFolderMappingOptions[] folders) =>
        new MailSynchronizationOptions { Accounts = [Account("primary", folders)] }
            .WithServedOwners(
            [
                new ServedMailOwner(
                    SyntheticMailOwner.Deployment,
                    "the deployment",
                    MailOwnerAccountSource.DeploymentSection,
                    []),
            ]);

    private static MailSynchronizationOptions RosterOf(params ServedMailOwner[] owners) =>
        new MailSynchronizationOptions().WithServedOwners(owners);

    private static ServedMailOwner DocumentOwner(
        MailOwnerId owner,
        OwnerSpamClassificationOptions classification,
        string accountId,
        params MailFolderMappingOptions[] folders) =>
        new(
            owner,
            accountId,
            MailOwnerAccountSource.OwnerDocument,
            [Account(accountId, folders)],
            classification);

    private static MailSynchronizationAccountOptions Account(
        string accountId,
        params MailFolderMappingOptions[] folders) =>
        new()
        {
            AccountId = accountId,
            DisplayName = "The primary mailbox",
            Host = "imap.example.test",
            UserName = "mailfathom@example.test",
            Secrets = new MailAccountSecretOptions
            {
                Password = new ConfiguredSecret
                {
                    SecretReference = "systemd-credential:imap-primary-password",
                },
            },
            Folders = [.. folders],
        };
}
