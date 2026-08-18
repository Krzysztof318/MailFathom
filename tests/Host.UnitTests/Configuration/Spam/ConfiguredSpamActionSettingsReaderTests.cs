// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Folders;
using MailFathom.Host.Configuration.Spam;
using MailFathom.Host.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Spam;

/// <summary>Covers how the bound section becomes the settings an action is decided by.</summary>
public sealed class ConfiguredSpamActionSettingsReaderTests
{
    [Fact]
    public void Actions_ASectionSettingNothing_AsksForNoChangeToAnyMailbox()
    {
        // Arrange
        var reader = ReaderFor(new SpamClassificationOptions());

        // Act
        var settings = reader.Actions;

        // Assert
        Assert.False(settings.IsAnyActionEnabled);
    }

    [Fact]
    public void Actions_BothSwitchesOn_CarriesTheDestinationAndTheThreshold()
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
        var settings = reader.Actions;

        // Assert
        Assert.True(settings.FilesJunk);
        Assert.True(settings.MarksJunkRead);
        Assert.Equal(MailFolderAlias.Create("quarantine"), settings.JunkFolder.Alias);
        Assert.Equal(9, settings.Threshold);
    }

    /// <summary>Validation refuses this combination, and the reader still cannot be the path that acts on verdicts nobody reaches.</summary>
    [Fact]
    public void Actions_SwitchesOnWhileClassificationIsOff_AsksForNothing()
    {
        // Arrange
        var reader = ReaderFor(new SpamClassificationOptions
        {
            Enabled = false,
            Actions = new SpamActionOptions { MoveToJunkFolder = true, MarkAsRead = true },
        });

        // Act
        var settings = reader.Actions;

        // Assert
        Assert.False(settings.IsAnyActionEnabled);
    }

    [Fact]
    public void Actions_ASectionReloaded_IsReadAgainRatherThanCaptured()
    {
        // Arrange
        var options = new TestOptionsMonitor<SpamClassificationOptions>(new SpamClassificationOptions());
        var reader = new ConfiguredSpamActionSettingsReader(options);

        // Act
        var beforeReload = reader.Actions;

        options.ReportReload(new SpamClassificationOptions
        {
            Enabled = true,
            Actions = new SpamActionOptions { MoveToJunkFolder = true },
        });

        var afterReload = reader.Actions;

        // Assert
        Assert.False(beforeReload.FilesJunk);
        Assert.True(afterReload.FilesJunk);
    }

    private static ConfiguredSpamActionSettingsReader ReaderFor(SpamClassificationOptions options) =>
        new(new TestOptionsMonitor<SpamClassificationOptions>(options));
}
