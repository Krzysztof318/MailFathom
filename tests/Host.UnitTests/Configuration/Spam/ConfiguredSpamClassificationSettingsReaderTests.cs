// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Folders;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Spam;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Secrets.Discovery;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Spam;

/// <summary>Covers how the bound section becomes the settings a classification runs with.</summary>
public sealed class ConfiguredSpamClassificationSettingsReaderTests
{
    /// <summary>An operator who wrote no folder asked for the default, which is whichever alias each account maps to its inbox.</summary>
    [Fact]
    public void Settings_NoScannedFolderConfigured_TakesEachAccountsInboxAlias()
    {
        // Arrange
        var reader = ReaderFor(
            new SpamClassificationOptions { Enabled = true },
            AccountMapping("primary-mail", "Inbox"));

        // Act
        var settings = reader.Settings;

        // Assert
        Assert.True(settings.IsEnabled);
        Assert.Equal([MailFolderAlias.Create("PRIMARY-MAIL")], settings.ScannedFolderAliases);
    }

    /// <summary>An operator who wrote no folders at all asked for none, which the default must not quietly overrule.</summary>
    [Fact]
    public void Settings_AnExplicitlyEmptyScannedFolderList_ClassifiesNoFolder()
    {
        // Arrange
        var reader = ReaderFor(
            new SpamClassificationOptions { Enabled = true, ScannedFolders = [] },
            AccountMapping("inbox", "Inbox"));

        // Act
        var settings = reader.Settings;

        // Assert
        Assert.True(settings.IsEnabled);
        Assert.Empty(settings.ScannedFolderAliases);
    }

    [Fact]
    public void Settings_ScannedFoldersConfigured_TakesThemRatherThanTheInbox()
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
        var settings = reader.Settings;

        // Assert
        Assert.True(settings.UsesScanner);
        Assert.Equal(7.5, settings.ScannerThreshold);
        Assert.Equal(
            [MailFolderAlias.Create("INBOX"), MailFolderAlias.Create("JUNK")],
            settings.ScannedFolderAliases);
    }

    /// <summary>A section reloaded while the process runs takes effect on the next classification rather than at the next restart.</summary>
    [Fact]
    public void Settings_ASectionReloaded_IsReadAgainRatherThanCaptured()
    {
        // Arrange
        var options = new TestOptionsMonitor<SpamClassificationOptions>(new SpamClassificationOptions());
        var reader = new ConfiguredSpamClassificationSettingsReader(
            options,
            SynchronizationOptionsWith(AccountMapping("inbox", "Inbox")));

        // Act
        var beforeReload = reader.Settings;

        options.ReportReload(new SpamClassificationOptions { Enabled = true });

        var afterReload = reader.Settings;

        // Assert
        Assert.False(beforeReload.IsEnabled);
        Assert.True(afterReload.IsEnabled);
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
        new()
        {
            Accounts =
            [
                new MailSynchronizationAccountOptions
                {
                    AccountId = "primary",
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
                },
            ],
        };
}
