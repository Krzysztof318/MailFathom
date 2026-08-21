// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the reader several suites build a classification gate from.</summary>
public sealed class StubSpamClassificationSettingsReaderTests
{
    [Fact]
    public void Settings_TheSettingsItWasGiven_AnswersThemUnchanged()
    {
        // Arrange
        var settings = SpamClassificationSettings.Create(
            isEnabled: true,
            usesScanner: false,
            [MailFolderAlias.Create("INBOX")]);

        // Act
        var reader = new StubSpamClassificationSettingsReader(settings);

        // Assert
        Assert.Same(settings, reader.Settings);
    }

    [Fact]
    public void Disabled_ADeploymentThatConfiguredNothing_ClassifiesNoMail()
    {
        // Arrange, Act
        var reader = StubSpamClassificationSettingsReader.Disabled;

        // Assert
        Assert.False(reader.Settings.IsEnabled);
        Assert.Empty(reader.Settings.ScannedFolderAliases);
    }
}
