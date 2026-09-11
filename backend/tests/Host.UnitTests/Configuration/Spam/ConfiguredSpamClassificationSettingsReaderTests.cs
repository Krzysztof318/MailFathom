// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Spam;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Spam;

/// <summary>Covers how each user's source becomes the settings a classification of their mail runs with.</summary>
public sealed class ConfiguredSpamClassificationSettingsReaderTests
{
    /// <summary>An operator who wrote no folder asked for the default, which is whichever alias each account maps to its inbox.</summary>
    [Fact]
    public void SettingsFor_NoScannedFolderConfigured_TakesEachAccountsInboxAlias()
    {
        // Arrange
        var reader = ReaderFor(
            new UserSpamClassificationOptions { Enabled = true },
            AccountMapping("primary-mail", "Inbox"));

        // Act
        var settings = reader.SettingsFor(SyntheticMailUser.Deployment);

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
            new SpamClassificationOptions { ClassificationWait = TimeSpan.FromHours(2) },
            new UserSpamClassificationOptions { Enabled = true },
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
            new UserSpamClassificationOptions { Enabled = true },
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
            new UserSpamClassificationOptions { Enabled = true, ScannedFolders = [] },
            AccountMapping("inbox", "Inbox"));

        // Act
        var settings = reader.SettingsFor(SyntheticMailUser.Deployment);

        // Assert
        Assert.True(settings.IsEnabled);
        Assert.Empty(settings.ScannedFolderAliases);
    }

    [Fact]
    public void SettingsFor_ScannedFoldersConfigured_TakesThemRatherThanTheInbox()
    {
        // Arrange
        var reader = ReaderFor(
            new UserSpamClassificationOptions
            {
                Enabled = true,
                UseScanner = true,
                ScannedFolders = ["junk", "inbox"],
                ScannerThreshold = 7.5,
            },
            AccountMapping("inbox", "Inbox"));

        // Act
        var settings = reader.SettingsFor(SyntheticMailUser.Deployment);

        // Assert
        Assert.True(settings.UsesScanner);
        Assert.Equal(7.5, settings.ScannerThreshold);
        Assert.Equal(
            [MailFolderAlias.Create("INBOX"), MailFolderAlias.Create("JUNK")],
            settings.ScannedFolderAliases);
    }

    /// <summary>A section reloaded while the process runs takes effect on the next classification rather than at the next restart.</summary>
    [Fact]
    public void ScopeInForce_ASectionReloaded_IsReadAgainRatherThanCaptured()
    {
        // Arrange
        var options = new TestOptionsMonitor<SpamClassificationOptions>(new SpamClassificationOptions());
        var reader = new ConfiguredSpamClassificationSettingsReader(
            options,
            RosterOf(DocumentUser(
                SyntheticMailUser.Deployment,
                new UserSpamClassificationOptions { Enabled = true },
                "primary",
                AccountMapping("inbox", "Inbox"))));

        // Act
        var beforeReload = reader.ScopeInForce;

        options.ReportReload(new SpamClassificationOptions { ClassificationWait = TimeSpan.FromHours(2) });

        var afterReload = reader.ScopeInForce;

        // Assert
        Assert.Equal(
            SpamClassificationScope.DefaultMaximumClassificationWait,
            beforeReload.MaximumClassificationWait);
        Assert.Equal(TimeSpan.FromHours(2), afterReload.MaximumClassificationWait);
    }

    /// <summary>Nobody is served a posture by default, so a user this deployment does not hold classifies nothing.</summary>
    [Fact]
    public void SettingsFor_AUserThisDeploymentDoesNotServe_ClassifiesNothing()
    {
        // Arrange
        var reader = ReaderFor(
            new UserSpamClassificationOptions { Enabled = true },
            AccountMapping("inbox", "Inbox"));

        // Act
        var settings = reader.SettingsFor(SyntheticMailUser.Another);

        // Assert
        Assert.False(settings.IsEnabled);
        Assert.Empty(settings.ScannedFolderAliases);
    }

    [Fact]
    public void SettingsFor_NoUser_Throws()
    {
        // Arrange
        var reader = ReaderFor(new UserSpamClassificationOptions { Enabled = true }, AccountMapping("inbox", "Inbox"));

        // Act, Assert
        Assert.Throws<ArgumentException>(() => reader.SettingsFor(default));
    }

    /// <summary>A user whose document has been written is read from it, and the deployment's section stops reaching them.</summary>
    [Fact]
    public void SettingsFor_AUserWhoseDocumentWasWritten_TakesTheBlockThatDocumentCarries()
    {
        // Arrange
        var reader = new ConfiguredSpamClassificationSettingsReader(
            new TestOptionsMonitor<SpamClassificationOptions>(new SpamClassificationOptions
            {
                Enabled = true,
                ScannedFolders = ["inbox"],
            }),
            RosterOf(DocumentUser(
                SyntheticMailUser.Another,
                new UserSpamClassificationOptions
                {
                    Enabled = true,
                    UseScanner = true,
                    ScannedFolders = ["archive"],
                    ScannerThreshold = 3.5,
                },
                "second-account",
                AccountMapping("archive", "Archive"))));

        // Act
        var settings = reader.SettingsFor(SyntheticMailUser.Another);

        // Assert
        Assert.True(settings.UsesScanner);
        Assert.Equal(3.5, settings.ScannerThreshold);
        Assert.Equal([MailFolderAlias.Create("ARCHIVE")], settings.ScannedFolderAliases);
    }

    /// <summary>Switching classification off in a written record actually switches it off, rather than reverting to the file.</summary>
    [Fact]
    public void SettingsFor_AUserWhoseDocumentSwitchedClassificationOff_ClassifiesNothingWhileTheSectionStaysOn()
    {
        // Arrange
        var reader = new ConfiguredSpamClassificationSettingsReader(
            new TestOptionsMonitor<SpamClassificationOptions>(new SpamClassificationOptions { Enabled = true }),
            RosterOf(DocumentUser(
                SyntheticMailUser.Another,
                new UserSpamClassificationOptions { Enabled = false },
                "second-account",
                AccountMapping("inbox", "Inbox"))));

        // Act
        var settings = reader.SettingsFor(SyntheticMailUser.Another);

        // Assert
        Assert.False(settings.IsEnabled);
    }

    /// <summary>The wait bounds how long the index may be held back, which is the process's cost rather than one user's choice.</summary>
    [Fact]
    public void ScopeInForce_EveryUserReadFromTheirOwnDocument_StillTakesTheDeploymentsClassificationWait()
    {
        // Arrange
        var reader = new ConfiguredSpamClassificationSettingsReader(
            new TestOptionsMonitor<SpamClassificationOptions>(new SpamClassificationOptions
            {
                ClassificationWait = TimeSpan.FromHours(3),
            }),
            RosterOf(DocumentUser(
                SyntheticMailUser.Another,
                new UserSpamClassificationOptions { Enabled = true },
                "second-account",
                AccountMapping("inbox", "Inbox"))));

        // Act
        var scope = reader.ScopeInForce;

        // Assert
        Assert.Equal(TimeSpan.FromHours(3), scope.MaximumClassificationWait);
    }

    /// <summary>The scope a walk narrows by is composed per user, so one user's decision reaches a query spanning users.</summary>
    [Fact]
    public void ScopeInForce_TwoUsersWithDifferentPostures_NamesOnlyTheClassifyingUsersAccounts()
    {
        // Arrange
        var reader = new ConfiguredSpamClassificationSettingsReader(
            new TestOptionsMonitor<SpamClassificationOptions>(new SpamClassificationOptions()),
            RosterOf(
                DocumentUser(
                    SyntheticMailUser.Deployment,
                    new UserSpamClassificationOptions { Enabled = true, ScannedFolders = ["inbox"] },
                    "first-account",
                    AccountMapping("inbox", "Inbox"),
                    AccountMapping("archive", "Archive")),
                DocumentUser(
                    SyntheticMailUser.Another,
                    new UserSpamClassificationOptions { Enabled = false },
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

    /// <summary>Builds a reader over one user whose own record carries the posture, which is the only place one lives.</summary>
    private static ConfiguredSpamClassificationSettingsReader ReaderFor(
        UserSpamClassificationOptions record,
        params MailFolderMappingOptions[] folders) =>
        ReaderFor(new SpamClassificationOptions(), record, folders);

    /// <summary>Builds the same reader under a deployment section, which supplies the wait and nothing about whose mail is classified.</summary>
    private static ConfiguredSpamClassificationSettingsReader ReaderFor(
        SpamClassificationOptions deployment,
        UserSpamClassificationOptions record,
        params MailFolderMappingOptions[] folders) =>
        new(
            new TestOptionsMonitor<SpamClassificationOptions>(deployment),
            RosterOf(DocumentUser(SyntheticMailUser.Deployment, record, "primary", folders)));

    private static MailFolderMappingOptions AccountMapping(string alias, string specialUse) => new()
    {
        Alias = alias,
        SpecialUse = specialUse,
    };

    private static MailSynchronizationOptions RosterOf(params ServedMailUser[] users) =>
        new MailSynchronizationOptions().WithServedUsers(users);

    private static ServedMailUser DocumentUser(
        MailUserId user,
        UserSpamClassificationOptions classification,
        string accountId,
        params MailFolderMappingOptions[] folders) =>
        new(
            user,
            accountId,
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
