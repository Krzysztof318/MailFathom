// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
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
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Spam;

/// <summary>Covers how each account's own record becomes the settings a classification of its mail runs with.</summary>
public sealed class ConfiguredSpamClassificationSettingsReaderTests
{
    private static readonly MailAccountId PrimaryAccount = MailAccountId.Create("primary");

    private static readonly MailAccountId SecondAccount = MailAccountId.Create("second-account");

    /// <summary>An operator who wrote no folder asked for the default, which is whichever alias each account maps to its inbox.</summary>
    [Fact]
    public void SettingsFor_NoScannedFolderConfigured_TakesEachAccountsInboxAlias()
    {
        // Arrange
        var reader = ReaderFor(
            new MailAccountSpamClassificationOptions { Enabled = true },
            AccountMapping("primary-mail", "Inbox"));

        // Act
        var settings = reader.SettingsFor(PrimaryAccount);

        // Assert
        Assert.True(settings.IsEnabled);
        Assert.Equal([MailFolderAlias.Create("PRIMARY-MAIL")], settings.ScannedFolderAliases);
    }

    /// <summary>The bound on the ordering is the operator's, and it is what stops a wedged scanner stopping the index.</summary>
    [Fact]
    public async Task ReadScopeInForceAsync_AClassificationWaitConfigured_CarriesItToTheGate()
    {
        // Arrange
        var reader = ReaderFor(
            new SpamClassificationOptions { ClassificationWait = TimeSpan.FromHours(2) },
            new MailAccountSpamClassificationOptions { Enabled = true },
            AccountMapping("inbox", "Inbox"));

        // Act
        var scope = await reader.ReadScopeInForceAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(TimeSpan.FromHours(2), scope.MaximumClassificationWait);
    }

    /// <summary>An operator who named no wait gets one anyway, because a wait of none would release every message.</summary>
    [Fact]
    public async Task ReadScopeInForceAsync_NoClassificationWaitConfigured_TakesTheDefaultWait()
    {
        // Arrange
        var reader = ReaderFor(
            new MailAccountSpamClassificationOptions { Enabled = true },
            AccountMapping("inbox", "Inbox"));

        // Act
        var scope = await reader.ReadScopeInForceAsync(TestContext.Current.CancellationToken);

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
            new MailAccountSpamClassificationOptions { Enabled = true, ScannedFolders = [] },
            AccountMapping("inbox", "Inbox"));

        // Act
        var settings = reader.SettingsFor(PrimaryAccount);

        // Assert
        Assert.True(settings.IsEnabled);
        Assert.Empty(settings.ScannedFolderAliases);
    }

    [Fact]
    public void SettingsFor_ScannedFoldersConfigured_TakesThemRatherThanTheInbox()
    {
        // Arrange
        var reader = ReaderFor(
            new MailAccountSpamClassificationOptions
            {
                Enabled = true,
                UseScanner = true,
                ScannedFolders = ["junk", "inbox"],
                ScannerThreshold = 7.5,
            },
            AccountMapping("inbox", "Inbox"));

        // Act
        var settings = reader.SettingsFor(PrimaryAccount);

        // Assert
        Assert.True(settings.UsesScanner);
        Assert.Equal(7.5, settings.ScannerThreshold);
        Assert.Equal(
            [MailFolderAlias.Create("INBOX"), MailFolderAlias.Create("JUNK")],
            settings.ScannedFolderAliases);
    }

    /// <summary>A section reloaded while the process runs takes effect on the next classification rather than at the next restart.</summary>
    [Fact]
    public async Task ReadScopeInForceAsync_ASectionReloaded_IsReadAgainRatherThanCaptured()
    {
        // Arrange
        var options = new TestOptionsMonitor<SpamClassificationOptions>(new SpamClassificationOptions());
        var reader = new ConfiguredSpamClassificationSettingsReader(
            options,
            RosterOf(DocumentUser(
                SyntheticUser.Deployment,
                new MailAccountSpamClassificationOptions { Enabled = true },
                "primary",
                AccountMapping("inbox", "Inbox"))),
            StubDeploymentMailFolders.None);

        // Act
        var beforeReload = await reader.ReadScopeInForceAsync(TestContext.Current.CancellationToken);

        options.ReportReload(new SpamClassificationOptions { ClassificationWait = TimeSpan.FromHours(2) });

        var afterReload = await reader.ReadScopeInForceAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            SpamClassificationScope.DefaultMaximumClassificationWait,
            beforeReload.MaximumClassificationWait);
        Assert.Equal(TimeSpan.FromHours(2), afterReload.MaximumClassificationWait);
    }

    /// <summary>No mailbox is served a posture by default, so an account this deployment does not hold classifies nothing.</summary>
    [Fact]
    public void SettingsFor_AnAccountThisDeploymentDoesNotServe_ClassifiesNothing()
    {
        // Arrange
        var reader = ReaderFor(
            new MailAccountSpamClassificationOptions { Enabled = true },
            AccountMapping("inbox", "Inbox"));

        // Act
        var settings = reader.SettingsFor(SecondAccount);

        // Assert
        Assert.False(settings.IsEnabled);
        Assert.Empty(settings.ScannedFolderAliases);
    }

    /// <summary>An account nobody named is one no record answers for, which reads exactly as a mailbox this deployment does not serve.</summary>
    [Fact]
    public void SettingsFor_NoAccountAtAll_ClassifiesNothing()
    {
        // Arrange
        var reader = ReaderFor(new MailAccountSpamClassificationOptions { Enabled = true }, AccountMapping("inbox", "Inbox"));

        // Act
        var settings = reader.SettingsFor(default);

        // Assert
        Assert.False(settings.IsEnabled);
    }

    /// <summary>An account whose record has been written is read from it, and the deployment's section stops reaching it.</summary>
    [Fact]
    public void SettingsFor_AnAccountWhoseRecordWasWritten_TakesTheBlockThatRecordCarries()
    {
        // Arrange
        var reader = new ConfiguredSpamClassificationSettingsReader(
            new TestOptionsMonitor<SpamClassificationOptions>(new SpamClassificationOptions
            {
                Enabled = true,
                ScannedFolders = ["inbox"],
            }),
            RosterOf(DocumentUser(
                SyntheticUser.Another,
                new MailAccountSpamClassificationOptions
                {
                    Enabled = true,
                    UseScanner = true,
                    ScannedFolders = ["archive"],
                    ScannerThreshold = 3.5,
                },
                "second-account",
                AccountMapping("archive", "Archive"))),
            StubDeploymentMailFolders.None);

        // Act
        var settings = reader.SettingsFor(SecondAccount);

        // Assert
        Assert.True(settings.UsesScanner);
        Assert.Equal(3.5, settings.ScannerThreshold);
        Assert.Equal([MailFolderAlias.Create("ARCHIVE")], settings.ScannedFolderAliases);
    }

    /// <summary>Switching classification off in a written record actually switches it off, rather than reverting to the file.</summary>
    [Fact]
    public void SettingsFor_AnAccountWhoseRecordSwitchedClassificationOff_ClassifiesNothingWhileTheSectionStaysOn()
    {
        // Arrange
        var reader = new ConfiguredSpamClassificationSettingsReader(
            new TestOptionsMonitor<SpamClassificationOptions>(new SpamClassificationOptions { Enabled = true }),
            RosterOf(DocumentUser(
                SyntheticUser.Another,
                new MailAccountSpamClassificationOptions { Enabled = false },
                "second-account",
                AccountMapping("inbox", "Inbox"))),
            StubDeploymentMailFolders.None);

        // Act
        var settings = reader.SettingsFor(SecondAccount);

        // Assert
        Assert.False(settings.IsEnabled);
    }

    /// <summary>The wait bounds how long the index may be held back, which is the process's cost rather than one user's choice.</summary>
    [Fact]
    public async Task ReadScopeInForceAsync_EveryUserReadFromTheirOwnDocument_StillTakesTheDeploymentsClassificationWait()
    {
        // Arrange
        var reader = new ConfiguredSpamClassificationSettingsReader(
            new TestOptionsMonitor<SpamClassificationOptions>(new SpamClassificationOptions
            {
                ClassificationWait = TimeSpan.FromHours(3),
            }),
            RosterOf(DocumentUser(
                SyntheticUser.Another,
                new MailAccountSpamClassificationOptions { Enabled = true },
                "second-account",
                AccountMapping("inbox", "Inbox"))),
            StubDeploymentMailFolders.None);

        // Act
        var scope = await reader.ReadScopeInForceAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(TimeSpan.FromHours(3), scope.MaximumClassificationWait);
    }

    /// <summary>
    /// The scope a walk narrows by is read from the account records rather than from the roster: the accounts are the
    /// ones whose folders the classifying set names, once each however many folders they map, and the folders are the
    /// ones the classified set names.
    /// </summary>
    [Fact]
    public async Task ReadScopeInForceAsync_TheFolderSetsTheRecordsAnswer_ComposesTheScopeFromThem()
    {
        // Arrange
        var first = MailAccountId.Create("first-account");
        var deploymentFolders = Substitute.For<IDeploymentMailFolders>();
        deploymentFolders
            .ReadAsync(MailFolderSelection.OfAccountsClassifyingSpam, Arg.Any<CancellationToken>())
            .Returns([new MailFolderIdentity(first, MailFolderAlias.Create("ARCHIVE")), new MailFolderIdentity(first, MailFolderAlias.Create("INBOX"))]);
        deploymentFolders
            .ReadAsync(MailFolderSelection.ClassifiedForSpam, Arg.Any<CancellationToken>())
            .Returns([new MailFolderIdentity(first, MailFolderAlias.Create("INBOX"))]);
        var reader = new ConfiguredSpamClassificationSettingsReader(
            new TestOptionsMonitor<SpamClassificationOptions>(new SpamClassificationOptions()),
            new MailSynchronizationOptions(),
            deploymentFolders);

        // Act
        var scope = await reader.ReadScopeInForceAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([first], scope.ClassifyingAccounts);
        Assert.Equal([new MailFolderIdentity(first, MailFolderAlias.Create("INBOX"))], scope.ClassifiedFolders);
    }

    /// <summary>
    /// The claim the whole move is for: one mailbox assigned to two people is one copy of the mail, so it is classified
    /// once under the settings written on the account rather than twice under theirs. Neither user's record can decide
    /// it, which is why the answer is the same whichever of them the walk arrived through, and why the scope names the
    /// account once.
    /// </summary>
    [Fact]
    public void SettingsFor_AnAccountAssignedToTwoUsers_ClassifiesItOnceUnderItsOwnSettings()
    {
        // Arrange
        var shared = new MailAccountSpamClassificationOptions
        {
            Enabled = true,
            UseScanner = true,
            ScannedFolders = ["inbox"],
        };

        var reader = new ConfiguredSpamClassificationSettingsReader(
            new TestOptionsMonitor<SpamClassificationOptions>(new SpamClassificationOptions()),
            RosterOf(
                DocumentUser(SyntheticUser.Deployment, shared, "shared-account", AccountMapping("inbox", "Inbox")),
                DocumentUser(SyntheticUser.Another, shared, "shared-account", AccountMapping("inbox", "Inbox"))),
            StubDeploymentMailFolders.None);

        // Act
        var settings = reader.SettingsFor(MailAccountId.Create("shared-account"));

        // Assert
        Assert.True(settings.IsEnabled);
        Assert.True(settings.UsesScanner);
        Assert.Equal([MailFolderAlias.Create("INBOX")], settings.ScannedFolderAliases);
    }

    /// <summary>Records naming no classifying account classify nothing, whatever the deployment's section says.</summary>
    [Fact]
    public async Task ReadScopeInForceAsync_RecordsNamingNoClassifyingAccount_ClassifiesNothing()
    {
        // Arrange
        var reader = new ConfiguredSpamClassificationSettingsReader(
            new TestOptionsMonitor<SpamClassificationOptions>(new SpamClassificationOptions { Enabled = true }),
            new MailSynchronizationOptions(),
            StubDeploymentMailFolders.None);

        // Act
        var scope = await reader.ReadScopeInForceAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(scope.ClassifyingAccounts);
        Assert.Empty(scope.ClassifiedFolders);
    }

    /// <summary>Builds a reader over one account whose own record carries the posture, which is the only place one lives.</summary>
    private static ConfiguredSpamClassificationSettingsReader ReaderFor(
        MailAccountSpamClassificationOptions record,
        params MailFolderMappingOptions[] folders) =>
        ReaderFor(new SpamClassificationOptions(), record, folders);

    /// <summary>Builds the same reader under a deployment section, which supplies the wait and nothing about whose mail is classified.</summary>
    private static ConfiguredSpamClassificationSettingsReader ReaderFor(
        SpamClassificationOptions deployment,
        MailAccountSpamClassificationOptions record,
        params MailFolderMappingOptions[] folders) =>
        new(
            new TestOptionsMonitor<SpamClassificationOptions>(deployment),
            RosterOf(DocumentUser(SyntheticUser.Deployment, record, "primary", folders)),
            StubDeploymentMailFolders.None);

    private static MailFolderMappingOptions AccountMapping(string alias, string specialUse) => new()
    {
        Alias = alias,
        SpecialUse = specialUse,
    };

    private static MailSynchronizationOptions RosterOf(params ServedUser[] users) =>
        new MailSynchronizationOptions().WithServedUsers(users);

    private static ServedUser DocumentUser(
        UserId user,
        MailAccountSpamClassificationOptions classification,
        string accountId,
        params MailFolderMappingOptions[] folders) =>
        new(user, accountId, [Account(accountId, classification, folders)]);

    private static MailSynchronizationAccountOptions Account(
        string accountId,
        MailAccountSpamClassificationOptions classification,
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
            SpamClassification = classification,
        };
}
