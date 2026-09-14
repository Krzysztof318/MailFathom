// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the reader several suites build a classification gate from.</summary>
public sealed class StubSpamClassificationSettingsReaderTests
{
    [Fact]
    public void SettingsFor_AnAccountItClassifiesFor_AnswersTheSettingsItWasGivenUnchanged()
    {
        // Arrange
        var settings = SpamClassificationSettings.Create(
            isEnabled: true,
            usesScanner: false,
            [MailFolderAlias.Create("INBOX")]);
        var account = MailAccountId.Create("primary");

        // Act
        var answered = new StubSpamClassificationSettingsReader(settings, account).SettingsFor(account);

        // Assert
        Assert.Same(settings, answered);
    }

    /// <summary>
    /// The deployed reader answers an account its roster does not resolve with the disabled posture, and this double
    /// has to as well: a use case that resolved a message's account and then failed to narrow by it would otherwise
    /// read back somebody else's mailbox's settings and pass.
    /// </summary>
    [Fact]
    public void SettingsFor_AnAccountOutsideItsScope_ClassifiesNothing()
    {
        // Arrange
        var settings = SpamClassificationSettings.Create(
            isEnabled: true,
            usesScanner: false,
            [MailFolderAlias.Create("INBOX")]);

        // Act
        var answered = new StubSpamClassificationSettingsReader(settings, MailAccountId.Create("primary"))
            .SettingsFor(MailAccountId.Create("archive"));

        // Assert
        Assert.False(answered.IsEnabled);
        Assert.Empty(answered.ScannedFolderAliases);
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
        Assert.False(reader.SettingsFor(MailAccountId.Create("anything")).IsEnabled);
        Assert.Empty(reader.SettingsFor(MailAccountId.Create("anything")).ScannedFolderAliases);
        Assert.Empty(reader.ScopeInForce.ClassifyingAccounts);
    }
}
