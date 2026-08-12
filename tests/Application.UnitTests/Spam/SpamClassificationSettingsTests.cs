// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam;
using MailFathom.Domain.Folders;
using Xunit;

namespace MailFathom.Application.UnitTests.Spam;

public sealed class SpamClassificationSettingsTests
{
    private static readonly string[] RepeatedAliases = ["JUNK", "INBOX", "JUNK"];

    /// <summary>A deployment that configured nothing runs every switch off and classifies no folder.</summary>
    [Fact]
    public void Disabled_TheSettingsAConfiguredNothingProduces_ClassifiesNothing()
    {
        // Arrange, Act
        var settings = SpamClassificationSettings.Disabled;

        // Assert
        Assert.False(settings.IsEnabled);
        Assert.False(settings.UsesScanner);
        Assert.Empty(settings.ScannedFolderAliases);
        Assert.Null(settings.ScannerThreshold);
        Assert.False(settings.Covers(MailFolderAlias.Create("INBOX")));
    }

    /// <summary>Two configured entries naming one folder are one folder, whatever order they arrived in.</summary>
    [Fact]
    public void Create_AliasesRepeatedAndOutOfOrder_AreOneNormalizedScope()
    {
        // Arrange
        var aliases = RepeatedAliases.Select(MailFolderAlias.Create);

        // Act
        var settings = SpamClassificationSettings.Create(isEnabled: true, usesScanner: false, aliases);

        // Assert
        Assert.Equal(["INBOX", "JUNK"], settings.ScannedFolderAliases.Select(alias => alias.Value));
    }

    [Fact]
    public void Covers_AnAliasOutsideTheConfiguredScope_IsNotClassified()
    {
        // Arrange
        var settings = SpamClassificationSettings.Create(
            isEnabled: true,
            usesScanner: false,
            [MailFolderAlias.Create("INBOX")]);

        // Act, Assert
        Assert.True(settings.Covers(MailFolderAlias.Create("INBOX")));
        Assert.False(settings.Covers(MailFolderAlias.Create("ARCHIVE")));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Create_AThresholdThatIsNotFinite_IsRefused(double threshold)
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => SpamClassificationSettings.Create(
            isEnabled: true,
            usesScanner: true,
            [MailFolderAlias.Create("INBOX")],
            threshold));
    }

    [Fact]
    public void Create_NoAliasSequence_IsRefused()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentNullException>(() => SpamClassificationSettings.Create(
            isEnabled: true,
            usesScanner: false,
            scannedFolderAliases: null!));
    }
}
