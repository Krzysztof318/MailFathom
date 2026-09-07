// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Folders;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Spam;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Spam;

/// <summary>Covers how each user's source becomes the settings an action on their junk is decided by.</summary>
public sealed class ConfiguredSpamActionSettingsReaderTests
{
    [Fact]
    public void ActionsFor_ASectionSettingNothing_AsksForNoChangeToAnyMailbox()
    {
        // Arrange
        var reader = ReaderFor(new SpamClassificationOptions());

        // Act
        var settings = reader.ActionsFor(SyntheticMailUser.Deployment);

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
        var settings = reader.ActionsFor(SyntheticMailUser.Deployment);

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
        var settings = reader.ActionsFor(SyntheticMailUser.Deployment);

        // Assert
        Assert.False(settings.IsAnyActionEnabled);
    }

    [Fact]
    public void ActionsFor_ASectionReloaded_IsReadAgainRatherThanCaptured()
    {
        // Arrange
        var options = new TestOptionsMonitor<SpamClassificationOptions>(new SpamClassificationOptions());
        var reader = new ConfiguredSpamActionSettingsReader(options, ConfiguredUserRoster());

        // Act
        var beforeReload = reader.ActionsFor(SyntheticMailUser.Deployment);

        options.ReportReload(new SpamClassificationOptions
        {
            Enabled = true,
            Actions = new SpamActionOptions { MoveToJunkFolder = true },
        });

        var afterReload = reader.ActionsFor(SyntheticMailUser.Deployment);

        // Assert
        Assert.False(beforeReload.FilesJunk);
        Assert.True(afterReload.FilesJunk);
    }

    /// <summary>Nothing writes to a mailbox this deployment does not serve, whatever the deployment's own section says.</summary>
    [Fact]
    public void ActionsFor_AnUserThisDeploymentDoesNotServe_AsksForNothing()
    {
        // Arrange
        var reader = ReaderFor(new SpamClassificationOptions
        {
            Enabled = true,
            Actions = new SpamActionOptions { MoveToJunkFolder = true },
        });

        // Act
        var settings = reader.ActionsFor(SyntheticMailUser.Another);

        // Assert
        Assert.False(settings.IsAnyActionEnabled);
    }

    [Fact]
    public void ActionsFor_NoUser_Throws()
    {
        // Arrange
        var reader = ReaderFor(new SpamClassificationOptions());

        // Act, Assert
        Assert.Throws<ArgumentException>(() => reader.ActionsFor(default));
    }

    /// <summary>Each user decides what happens to their own junk, so one filing it does not file anybody else's.</summary>
    [Fact]
    public void ActionsFor_TwoUsersWithDifferentPostures_AnswersEachWithTheirOwn()
    {
        // Arrange
        var reader = new ConfiguredSpamActionSettingsReader(
            new TestOptionsMonitor<SpamClassificationOptions>(new SpamClassificationOptions()),
            new MailSynchronizationOptions().WithServedUsers(
            [
                DocumentUser(new UserSpamClassificationOptions
                {
                    Enabled = true,
                    Actions = new UserSpamActionOptions { MoveToJunkFolder = true, JunkFolder = "quarantine" },
                }),
                AnotherDocumentUser(new UserSpamClassificationOptions
                {
                    Enabled = true,
                    Actions = new UserSpamActionOptions { MarkAsRead = true },
                }),
            ]));

        // Act
        var filing = reader.ActionsFor(SyntheticMailUser.Deployment);
        var marking = reader.ActionsFor(SyntheticMailUser.Another);

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
            new MailSynchronizationOptions().WithServedUsers(
            [
                DocumentUser(new UserSpamClassificationOptions
                {
                    Enabled = false,
                    Actions = new UserSpamActionOptions { MoveToJunkFolder = true, MarkAsRead = true },
                }),
            ]));

        // Act
        var settings = reader.ActionsFor(SyntheticMailUser.Deployment);

        // Assert
        Assert.False(settings.IsAnyActionEnabled);
    }

    private static ConfiguredSpamActionSettingsReader ReaderFor(SpamClassificationOptions options) =>
        new(new TestOptionsMonitor<SpamClassificationOptions>(options), ConfiguredUserRoster());

    private static MailSynchronizationOptions ConfiguredUserRoster() =>
        new MailSynchronizationOptions().WithServedUsers(
        [
            new ServedMailUser(
                SyntheticMailUser.Deployment,
                "the deployment",
                MailUserAccountSource.DeploymentSection,
                []),
        ]);

    private static ServedMailUser DocumentUser(UserSpamClassificationOptions classification) => new(
        SyntheticMailUser.Deployment,
        "the first user",
        MailUserAccountSource.UserDocument,
        [],
        classification);

    private static ServedMailUser AnotherDocumentUser(UserSpamClassificationOptions classification) => new(
        SyntheticMailUser.Another,
        "the second user",
        MailUserAccountSource.UserDocument,
        [],
        classification);
}
