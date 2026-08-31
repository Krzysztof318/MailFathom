// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the reader several suites build a classification gate from.</summary>
public sealed class StubSpamClassificationSettingsReaderTests
{
    [Fact]
    public void SettingsFor_TheSettingsItWasGiven_AnswersThemUnchanged()
    {
        // Arrange
        var settings = SpamClassificationSettings.Create(
            isEnabled: true,
            usesScanner: false,
            [MailFolderAlias.Create("INBOX")]);

        // Act
        var answered = new StubSpamClassificationSettingsReader(settings, MailAccountId.Create("primary"))
            .SettingsFor(MailOwnerId.Create(Guid.NewGuid()));

        // Assert
        Assert.Same(settings, answered);
    }

    /// <summary>A posture that classifies beside a scope naming nobody is a pairing the deployed reader cannot produce.</summary>
    /// <remarks>
    /// The gate reads the scope rather than the posture, so a test built that way would believe it had switched
    /// classification on while every message was admitted unscored — and it would pass, over a pipeline nothing gated.
    /// </remarks>
    [Fact]
    public void Constructor_AnEnabledPostureNamingNoAccount_Throws()
    {
        // Arrange
        var settings = SpamClassificationSettings.Create(
            isEnabled: true,
            usesScanner: false,
            [MailFolderAlias.Create("INBOX")]);

        // Act, Assert
        Assert.Throws<ArgumentException>(() => new StubSpamClassificationSettingsReader(settings));
    }

    [Fact]
    public void ScopeInForce_TheAccountsItWasGiven_ClassifiesEachOverTheConfiguredFolders()
    {
        // Arrange
        var settings = SpamClassificationSettings.Create(
            isEnabled: true,
            usesScanner: false,
            [MailFolderAlias.Create("INBOX")]);
        var account = MailAccountId.Create("primary");

        // Act
        var scope = new StubSpamClassificationSettingsReader(settings, account).ScopeInForce;

        // Assert
        Assert.Equal([account], scope.ClassifyingAccounts);
        Assert.Equal([new MailFolderIdentity(account, MailFolderAlias.Create("INBOX"))], scope.ClassifiedFolders);
    }

    [Fact]
    public void Disabled_ADeploymentThatConfiguredNothing_ClassifiesNoMail()
    {
        // Arrange, Act
        var reader = StubSpamClassificationSettingsReader.Disabled;

        // Assert
        Assert.False(reader.SettingsFor(MailOwnerId.Create(Guid.NewGuid())).IsEnabled);
        Assert.Empty(reader.SettingsFor(MailOwnerId.Create(Guid.NewGuid())).ScannedFolderAliases);
        Assert.Empty(reader.ScopeInForce.ClassifyingAccounts);
    }
}
