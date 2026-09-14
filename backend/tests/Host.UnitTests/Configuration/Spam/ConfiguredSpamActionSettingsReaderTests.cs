// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Spam;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Spam;

/// <summary>
/// Covers how an account's own record becomes the settings an action on its junk is decided by. There is no second
/// source: the deployment's own section reaches no mailbox, so every answer here comes off the roster.
/// </summary>
public sealed class ConfiguredSpamActionSettingsReaderTests
{
    private static readonly MailAccountId PrimaryAccount = MailAccountId.Create("primary");

    private static readonly MailAccountId SecondAccount = MailAccountId.Create("second-account");

    [Fact]
    public void ActionsFor_ARecordSettingNothing_AsksForNoChangeToAnyMailbox()
    {
        // Arrange
        var reader = ReaderFor(Account(PrimaryAccount, new MailAccountSpamClassificationOptions()));

        // Act
        var settings = reader.ActionsFor(PrimaryAccount);

        // Assert
        Assert.False(settings.IsAnyActionEnabled);
    }

    [Fact]
    public void ActionsFor_BothSwitchesOn_CarriesTheDestinationAndTheThreshold()
    {
        // Arrange
        var reader = ReaderFor(Account(
            PrimaryAccount,
            new MailAccountSpamClassificationOptions
            {
                Enabled = true,
                Actions = new MailAccountSpamActionOptions
                {
                    MoveToJunkFolder = true,
                    MarkAsRead = true,
                    JunkFolder = "quarantine",
                    Threshold = 9,
                },
            }));

        // Act
        var settings = reader.ActionsFor(PrimaryAccount);

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
        var reader = ReaderFor(Account(
            PrimaryAccount,
            new MailAccountSpamClassificationOptions
            {
                Enabled = false,
                Actions = new MailAccountSpamActionOptions { MoveToJunkFolder = true, MarkAsRead = true },
            }));

        // Act
        var settings = reader.ActionsFor(PrimaryAccount);

        // Assert
        Assert.False(settings.IsAnyActionEnabled);
    }

    /// <summary>Each account decides what happens to its own junk, so one filing it does not file any other mailbox's.</summary>
    [Fact]
    public void ActionsFor_TwoAccountsWithDifferentPostures_AnswersEachWithItsOwn()
    {
        // Arrange
        var reader = ReaderFor(
            Account(
                PrimaryAccount,
                new MailAccountSpamClassificationOptions
                {
                    Enabled = true,
                    Actions = new MailAccountSpamActionOptions { MoveToJunkFolder = true, JunkFolder = "quarantine" },
                }),
            Account(
                SecondAccount,
                new MailAccountSpamClassificationOptions
                {
                    Enabled = true,
                    Actions = new MailAccountSpamActionOptions { MarkAsRead = true },
                }));

        // Act
        var filing = reader.ActionsFor(PrimaryAccount);
        var marking = reader.ActionsFor(SecondAccount);

        // Assert
        Assert.True(filing.FilesJunk);
        Assert.False(filing.MarksJunkRead);
        Assert.Equal(MailFolderAlias.Create("quarantine"), filing.JunkFolder.Alias);
        Assert.False(marking.FilesJunk);
        Assert.True(marking.MarksJunkRead);
    }

    /// <summary>Nothing writes to a mailbox this deployment does not serve, because no record says it may.</summary>
    [Fact]
    public void ActionsFor_AnAccountThisDeploymentDoesNotServe_AsksForNothing()
    {
        // Arrange
        var reader = ReaderFor(Account(
            PrimaryAccount,
            new MailAccountSpamClassificationOptions
            {
                Enabled = true,
                Actions = new MailAccountSpamActionOptions { MoveToJunkFolder = true },
            }));

        // Act
        var settings = reader.ActionsFor(SecondAccount);

        // Assert
        Assert.False(settings.IsAnyActionEnabled);
    }

    /// <summary>An account nobody named is one no record answers for, which reads exactly as a mailbox this deployment does not serve.</summary>
    [Fact]
    public void ActionsFor_NoAccountAtAll_AsksForNothing()
    {
        // Arrange
        var reader = ReaderFor(Account(PrimaryAccount, new MailAccountSpamClassificationOptions()));

        // Act
        var settings = reader.ActionsFor(default);

        // Assert
        Assert.False(settings.IsAnyActionEnabled);
    }

    private static ConfiguredSpamActionSettingsReader ReaderFor(params MailSynchronizationAccountOptions[] accounts) =>
        new(new MailSynchronizationOptions().WithServedUsers(
        [
            new ServedMailUser(
                SyntheticMailUser.Deployment,
                "the served user",
                accounts),
        ]));

    private static MailSynchronizationAccountOptions Account(
        MailAccountId account,
        MailAccountSpamClassificationOptions classification) =>
        new()
        {
            AccountId = account.Value,
            DisplayName = $"The {account.Value} mailbox",
            Host = "imap.example.test",
            UserName = "mailfathom@example.test",
            Secrets = new MailAccountSecretOptions
            {
                Password = new ConfiguredSecret
                {
                    SecretReference = "systemd-credential:imap-primary-password",
                },
            },
            SpamClassification = classification,
        };
}
