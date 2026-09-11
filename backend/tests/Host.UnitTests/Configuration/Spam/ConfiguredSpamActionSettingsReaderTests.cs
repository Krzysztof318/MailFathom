// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Folders;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Spam;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Spam;

/// <summary>
/// Covers how a user's own record becomes the settings an action on their junk is decided by. There is no second
/// source: the deployment's own section reaches nobody's mailbox, so every answer here comes off the roster.
/// </summary>
public sealed class ConfiguredSpamActionSettingsReaderTests
{
    [Fact]
    public void ActionsFor_ARecordSettingNothing_AsksForNoChangeToAnyMailbox()
    {
        // Arrange
        var reader = ReaderFor(User(SyntheticMailUser.Deployment, new UserSpamClassificationOptions()));

        // Act
        var settings = reader.ActionsFor(SyntheticMailUser.Deployment);

        // Assert
        Assert.False(settings.IsAnyActionEnabled);
    }

    [Fact]
    public void ActionsFor_BothSwitchesOn_CarriesTheDestinationAndTheThreshold()
    {
        // Arrange
        var reader = ReaderFor(User(
            SyntheticMailUser.Deployment,
            new UserSpamClassificationOptions
            {
                Enabled = true,
                Actions = new UserSpamActionOptions
                {
                    MoveToJunkFolder = true,
                    MarkAsRead = true,
                    JunkFolder = "quarantine",
                    Threshold = 9,
                },
            }));

        // Act
        var settings = reader.ActionsFor(SyntheticMailUser.Deployment);

        // Assert
        Assert.True(settings.FilesJunk);
        Assert.True(settings.MarksJunkRead);
        Assert.Equal(MailFolderAlias.Create("quarantine"), settings.JunkFolder.Alias);
        Assert.Equal(9, settings.Threshold);
    }

    /// <summary>Classification switched off in a record answers for its actions too, whatever the switches beside it say.</summary>
    /// <remarks>Validation refuses this combination in a record, and the reader still cannot be the path that acts on verdicts nobody reaches.</remarks>
    [Fact]
    public void ActionsFor_ARecordWhoseClassificationIsOff_AsksForNothing()
    {
        // Arrange
        var reader = ReaderFor(User(
            SyntheticMailUser.Deployment,
            new UserSpamClassificationOptions
            {
                Enabled = false,
                Actions = new UserSpamActionOptions { MoveToJunkFolder = true, MarkAsRead = true },
            }));

        // Act
        var settings = reader.ActionsFor(SyntheticMailUser.Deployment);

        // Assert
        Assert.False(settings.IsAnyActionEnabled);
    }

    /// <summary>Each user decides what happens to their own junk, so one filing it does not file anybody else's.</summary>
    [Fact]
    public void ActionsFor_TwoUsersWithDifferentPostures_AnswersEachWithTheirOwn()
    {
        // Arrange
        var reader = ReaderFor(
            User(
                SyntheticMailUser.Deployment,
                new UserSpamClassificationOptions
                {
                    Enabled = true,
                    Actions = new UserSpamActionOptions { MoveToJunkFolder = true, JunkFolder = "quarantine" },
                }),
            User(
                SyntheticMailUser.Another,
                new UserSpamClassificationOptions
                {
                    Enabled = true,
                    Actions = new UserSpamActionOptions { MarkAsRead = true },
                }));

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

    /// <summary>Nothing writes to a mailbox this deployment does not serve, because no record says it may.</summary>
    [Fact]
    public void ActionsFor_AUserThisDeploymentDoesNotServe_AsksForNothing()
    {
        // Arrange
        var reader = ReaderFor(User(
            SyntheticMailUser.Deployment,
            new UserSpamClassificationOptions
            {
                Enabled = true,
                Actions = new UserSpamActionOptions { MoveToJunkFolder = true },
            }));

        // Act
        var settings = reader.ActionsFor(SyntheticMailUser.Another);

        // Assert
        Assert.False(settings.IsAnyActionEnabled);
    }

    [Fact]
    public void ActionsFor_NoUser_Throws()
    {
        // Arrange
        var reader = ReaderFor(User(SyntheticMailUser.Deployment, new UserSpamClassificationOptions()));

        // Act, Assert
        Assert.Throws<ArgumentException>(() => reader.ActionsFor(default));
    }

    private static ConfiguredSpamActionSettingsReader ReaderFor(params ServedMailUser[] served) =>
        new(new MailSynchronizationOptions().WithServedUsers(served));

    private static ServedMailUser User(MailUserId user, UserSpamClassificationOptions classification) =>
        new(user, $"user-{user.Value:D}", [], classification);
}
