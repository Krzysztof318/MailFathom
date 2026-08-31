// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Folders;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.OwnerSettings;
using MailFathom.Host.Configuration.Spam;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Spam;

/// <summary>Covers how each owner's source becomes the settings an action on their junk is decided by.</summary>
public sealed class ConfiguredSpamActionSettingsReaderTests
{
    [Fact]
    public void ActionsFor_ASectionSettingNothing_AsksForNoChangeToAnyMailbox()
    {
        // Arrange
        var reader = ReaderFor(new SpamClassificationOptions());

        // Act
        var settings = reader.ActionsFor(SyntheticMailOwner.Deployment);

        // Assert
        Assert.False(settings.IsAnyActionEnabled);
    }

    [Fact]
    public void ActionsFor_BothSwitchesOn_CarriesTheDestinationAndTheThreshold()
    {
        // Arrange
        var reader = ReaderFor(new SpamClassificationOptions
        {
            Enabled = true,
            Actions = new SpamActionOptions
            {
                MoveToJunkFolder = true,
                MarkAsRead = true,
                JunkFolder = "quarantine",
                Threshold = 9,
            },
        });

        // Act
        var settings = reader.ActionsFor(SyntheticMailOwner.Deployment);

        // Assert
        Assert.True(settings.FilesJunk);
        Assert.True(settings.MarksJunkRead);
        Assert.Equal(MailFolderAlias.Create("quarantine"), settings.JunkFolder.Alias);
        Assert.Equal(9, settings.Threshold);
    }

    /// <summary>Validation refuses this combination, and the reader still cannot be the path that acts on verdicts nobody reaches.</summary>
    [Fact]
    public void ActionsFor_SwitchesOnWhileClassificationIsOff_AsksForNothing()
    {
        // Arrange
        var reader = ReaderFor(new SpamClassificationOptions
        {
            Enabled = false,
            Actions = new SpamActionOptions { MoveToJunkFolder = true, MarkAsRead = true },
        });

        // Act
        var settings = reader.ActionsFor(SyntheticMailOwner.Deployment);

        // Assert
        Assert.False(settings.IsAnyActionEnabled);
    }

    [Fact]
    public void ActionsFor_ASectionReloaded_IsReadAgainRatherThanCaptured()
    {
        // Arrange
        var options = new TestOptionsMonitor<SpamClassificationOptions>(new SpamClassificationOptions());
        var reader = new ConfiguredSpamActionSettingsReader(options, ConfiguredOwnerRoster());

        // Act
        var beforeReload = reader.ActionsFor(SyntheticMailOwner.Deployment);

        options.ReportReload(new SpamClassificationOptions
        {
            Enabled = true,
            Actions = new SpamActionOptions { MoveToJunkFolder = true },
        });

        var afterReload = reader.ActionsFor(SyntheticMailOwner.Deployment);

        // Assert
        Assert.False(beforeReload.FilesJunk);
        Assert.True(afterReload.FilesJunk);
    }

    /// <summary>Nothing writes to a mailbox this deployment does not serve, whatever the deployment's own section says.</summary>
    [Fact]
    public void ActionsFor_AnOwnerThisDeploymentDoesNotServe_AsksForNothing()
    {
        // Arrange
        var reader = ReaderFor(new SpamClassificationOptions
        {
            Enabled = true,
            Actions = new SpamActionOptions { MoveToJunkFolder = true },
        });

        // Act
        var settings = reader.ActionsFor(SyntheticMailOwner.Another);

        // Assert
        Assert.False(settings.IsAnyActionEnabled);
    }

    [Fact]
    public void ActionsFor_NoOwner_Throws()
    {
        // Arrange
        var reader = ReaderFor(new SpamClassificationOptions());

        // Act, Assert
        Assert.Throws<ArgumentException>(() => reader.ActionsFor(default));
    }

    /// <summary>Each owner decides what happens to their own junk, so one filing it does not file anybody else's.</summary>
    [Fact]
    public void ActionsFor_TwoOwnersWithDifferentPostures_AnswersEachWithTheirOwn()
    {
        // Arrange
        var reader = new ConfiguredSpamActionSettingsReader(
            new TestOptionsMonitor<SpamClassificationOptions>(new SpamClassificationOptions()),
            new MailSynchronizationOptions().WithServedOwners(
            [
                DocumentOwner(new OwnerSpamClassificationOptions
                {
                    Enabled = true,
                    Actions = new OwnerSpamActionOptions { MoveToJunkFolder = true, JunkFolder = "quarantine" },
                }),
                AnotherDocumentOwner(new OwnerSpamClassificationOptions
                {
                    Enabled = true,
                    Actions = new OwnerSpamActionOptions { MarkAsRead = true },
                }),
            ]));

        // Act
        var filing = reader.ActionsFor(SyntheticMailOwner.Deployment);
        var marking = reader.ActionsFor(SyntheticMailOwner.Another);

        // Assert
        Assert.True(filing.FilesJunk);
        Assert.False(filing.MarksJunkRead);
        Assert.Equal(MailFolderAlias.Create("quarantine"), filing.JunkFolder.Alias);
        Assert.False(marking.FilesJunk);
        Assert.True(marking.MarksJunkRead);
    }

    /// <summary>Classification switched off in a record answers for its actions too, whatever the switches beside it say.</summary>
    [Fact]
    public void ActionsFor_ARecordWhoseClassificationIsOff_AsksForNothing()
    {
        // Arrange
        var reader = new ConfiguredSpamActionSettingsReader(
            new TestOptionsMonitor<SpamClassificationOptions>(new SpamClassificationOptions()),
            new MailSynchronizationOptions().WithServedOwners(
            [
                DocumentOwner(new OwnerSpamClassificationOptions
                {
                    Enabled = false,
                    Actions = new OwnerSpamActionOptions { MoveToJunkFolder = true, MarkAsRead = true },
                }),
            ]));

        // Act
        var settings = reader.ActionsFor(SyntheticMailOwner.Deployment);

        // Assert
        Assert.False(settings.IsAnyActionEnabled);
    }

    private static ConfiguredSpamActionSettingsReader ReaderFor(SpamClassificationOptions options) =>
        new(new TestOptionsMonitor<SpamClassificationOptions>(options), ConfiguredOwnerRoster());

    private static MailSynchronizationOptions ConfiguredOwnerRoster() =>
        new MailSynchronizationOptions().WithServedOwners(
        [
            new ServedMailOwner(
                SyntheticMailOwner.Deployment,
                "the deployment",
                MailOwnerAccountSource.DeploymentSection,
                []),
        ]);

    private static ServedMailOwner DocumentOwner(OwnerSpamClassificationOptions classification) => new(
        SyntheticMailOwner.Deployment,
        "the first owner",
        MailOwnerAccountSource.OwnerDocument,
        [],
        classification);

    private static ServedMailOwner AnotherDocumentOwner(OwnerSpamClassificationOptions classification) => new(
        SyntheticMailOwner.Another,
        "the second owner",
        MailOwnerAccountSource.OwnerDocument,
        [],
        classification);
}
