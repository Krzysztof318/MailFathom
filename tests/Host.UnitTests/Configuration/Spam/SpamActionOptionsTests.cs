// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Folders;
using MailFathom.Host.Configuration.Spam;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Spam;

/// <summary>Covers what the action block refuses, and what it means when an operator writes none of its keys.</summary>
public sealed class SpamActionOptionsTests
{
    [Fact]
    public void FindErrors_ABlockSettingNothing_ReportsNoErrorAndTouchesNoMailbox()
    {
        // Arrange
        var options = new SpamActionOptions();

        // Act
        var errors = options.FindErrors(classificationEnabled: true).ToArray();

        // Assert
        Assert.Empty(errors);
        Assert.False(options.IsAnyActionEnabled);
        Assert.Null(options.JunkFolder);
        Assert.Null(options.Threshold);
    }

    /// <summary>Acting on a verdict nothing produces is the same shape of contradiction as a scanner with classification off.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void FindErrors_AnActionAskedForWhileClassificationIsOff_IsRefused(bool fileInJunkFolder, bool markAsRead)
    {
        // Arrange
        var options = new SpamActionOptions { FileInJunkFolder = fileInJunkFolder, MarkAsRead = markAsRead };

        // Act
        var error = Assert.Single(options.FindErrors(classificationEnabled: false));

        // Assert
        Assert.Equal(
            [nameof(SpamActionOptions.FileInJunkFolder), nameof(SpamActionOptions.MarkAsRead)],
            error.MemberNames);
    }

    [Theory]
    [InlineData("role:NotARole")]
    [InlineData("   ")]
    public void FindErrors_AJunkFolderNamingNeitherAnAliasNorARole_IsRefused(string junkFolder)
    {
        // Arrange
        var options = new SpamActionOptions { FileInJunkFolder = true, JunkFolder = junkFolder };

        // Act
        var error = Assert.Single(options.FindErrors(classificationEnabled: true));

        // Assert
        Assert.Equal([nameof(SpamActionOptions.JunkFolder)], error.MemberNames);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100_000)]
    public void FindErrors_AThresholdOutsideTheUsableRange_IsRefused(double threshold)
    {
        // Arrange
        var options = new SpamActionOptions { MarkAsRead = true, Threshold = threshold };

        // Act
        var error = Assert.Single(options.FindErrors(classificationEnabled: true));

        // Assert
        Assert.Equal([nameof(SpamActionOptions.Threshold)], error.MemberNames);
    }

    [Fact]
    public void Destination_NoJunkFolderNamed_ReadsAsTheJunkRole()
    {
        // Arrange
        var options = new SpamActionOptions { FileInJunkFolder = true };

        // Act
        var destination = options.Destination;

        // Assert
        Assert.Equal(MailFolderSpecialUse.Junk, destination.Role);
    }

    [Fact]
    public void Destination_AnAliasNamed_ReadsAsThatFolderRatherThanARole()
    {
        // Arrange
        var options = new SpamActionOptions { FileInJunkFolder = true, JunkFolder = "quarantine" };

        // Act
        var destination = options.Destination;

        // Assert
        Assert.Equal(MailFolderAlias.Create("quarantine"), destination.Alias);
        Assert.Null(destination.Role);
    }

    [Fact]
    public void ToSettings_EveryKeyWritten_CarriesEachOneThrough()
    {
        // Arrange
        var options = new SpamActionOptions
        {
            FileInJunkFolder = true,
            MarkAsRead = true,
            JunkFolder = $"{MailFolderReference.RoleScheme}{MailFolderSpecialUse.Junk}",
            Threshold = 8,
        };

        // Act
        var settings = options.ToSettings();

        // Assert
        Assert.True(settings.FilesJunk);
        Assert.True(settings.MarksJunkRead);
        Assert.Equal(MailFolderSpecialUse.Junk, settings.JunkFolder.Role);
        Assert.Equal(8, settings.Threshold);
    }
}
