// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Synchronization;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.Infrastructure.Persistence.Users.AccountSettings;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.UserSettings;

/// <summary>
/// Covers the settings a question about every account filters on, read out of one account's own document. What is
/// written into the columns is what every deployment-wide read answers with, so each setting is asserted as the
/// document states it and the per-account readers would read it.
/// </summary>
public sealed class MailAccountQueryableSettingsReadingTests
{
    private static readonly Guid AccountId = new("0199a0c0-0000-7000-8000-0000000000c1");

    [Fact]
    public void Of_ADocumentStatingAMode_ReadsThatMode()
    {
        // Act
        var settings = MailAccountQueryableSettingsReading.Of(Record("""{"Mode":"Push"}"""));

        // Assert
        Assert.True(settings.IsReadable);
        Assert.Equal(MailSynchronizationMode.Push, settings.SynchronizationMode);
    }

    [Theory]
    [InlineData("""{"SpamClassification":{"Enabled":true}}""", true)]
    [InlineData("""{"SpamClassification":{"Enabled":false}}""", false)]
    [InlineData("{}", false)]
    public void Of_ASpamClassificationPosture_ReadsWhetherTheAccountClassifies(string document, bool classifies)
    {
        // Act
        var settings = MailAccountQueryableSettingsReading.Of(Record(document));

        // Assert
        Assert.Equal(classifies, settings.ClassifiesSpam);
    }

    /// <summary>A scanner is scanned for only where the account switched it on, so an unset switch is not a request.</summary>
    [Fact]
    public void Of_OneScannerSwitchedOnAndOneOff_ScansForTheOneSwitchedOn()
    {
        // Act
        var settings = MailAccountQueryableSettingsReading.Of(
            Record("""{"SensitiveContent":{"Secrets":{"Enabled":true},"Pii":{"Enabled":false}}}"""));

        // Assert
        Assert.Equal([SensitiveContentScannerKind.Secrets], settings.ScansFor);
    }

    /// <summary>The scanners outgoing mail is screened for are read whatever their case, once each, in order, and only where they name one.</summary>
    [Fact]
    public void Of_ScreenedScannersNamedInAnyCaseAndRepeated_ReadsEachKnownScannerOnceInOrder()
    {
        // Act
        var settings = MailAccountQueryableSettingsReading.Of(
            Record("""{"SensitiveContent":{"ScreenOutgoingMailFor":["pii","SECRETS","Pii","NoSuchScanner","7"]}}"""));

        // Assert
        Assert.Equal([SensitiveContentScannerKind.Secrets, SensitiveContentScannerKind.Pii], settings.ScreensOutgoingMailFor);
    }

    /// <summary>A folder is read with what it takes part in and the role it plays, as the per-account readers read it.</summary>
    [Fact]
    public void Of_DeclaredFolders_ReadsEachWithItsParticipationAndRole()
    {
        // Act
        var settings = MailAccountQueryableSettingsReading.Of(Record(
            """
            {
              "Folders": [
                { "Alias": "INBOX", "SpecialUse": "Inbox" },
                { "Alias": "SPAM", "SpecialUse": "Junk", "VisibleToTools": false }
              ]
            }
            """));

        // Assert
        Assert.Equal(
            [
                (MailFolderAlias.Create("INBOX"), (MailFolderSpecialUse?)MailFolderSpecialUse.Inbox, true),
                (MailFolderAlias.Create("SPAM"), MailFolderSpecialUse.Junk, false),
            ],
            settings.Folders.Select(folder => (folder.Alias, folder.SpecialUse, folder.Participation.IsVisibleToTools)));
    }

    /// <summary>An account declaring no folder synchronizes its inbox, so the inbox is the one folder its settings name.</summary>
    [Fact]
    public void Of_NoFolderDeclared_ReadsTheDefaultInbox()
    {
        // Act
        var settings = MailAccountQueryableSettingsReading.Of(Record("{}"));

        // Assert
        var folder = Assert.Single(settings.Folders);
        Assert.Equal(MailFolderAlias.Create("Inbox"), folder.Alias);
        Assert.Equal(MailFolderSpecialUse.Inbox, folder.SpecialUse);
    }

    /// <summary>A classifying account that names no folders classifies its inbox, and only its inbox.</summary>
    [Fact]
    public void Of_AClassifyingAccountNamingNoScannedFolders_ClassifiesItsInboxAlone()
    {
        // Act
        var settings = MailAccountQueryableSettingsReading.Of(Record(
            """
            {
              "SpamClassification": { "Enabled": true },
              "Folders": [ { "Alias": "INBOX", "SpecialUse": "Inbox" }, { "Alias": "ARCHIVE", "SpecialUse": "Archive" } ]
            }
            """));

        // Assert
        Assert.Equal([MailFolderAlias.Create("INBOX")], ClassifiedAliases(settings));
    }

    /// <summary>The folders a classifying account names are the ones classified, which need not include its inbox.</summary>
    [Fact]
    public void Of_AClassifyingAccountNamingItsScannedFolders_ClassifiesThoseFolders()
    {
        // Act
        var settings = MailAccountQueryableSettingsReading.Of(Record(
            """
            {
              "SpamClassification": { "Enabled": true, "ScannedFolders": ["ARCHIVE"] },
              "Folders": [ { "Alias": "INBOX", "SpecialUse": "Inbox" }, { "Alias": "ARCHIVE", "SpecialUse": "Archive" } ]
            }
            """));

        // Assert
        Assert.Equal([MailFolderAlias.Create("ARCHIVE")], ClassifiedAliases(settings));
    }

    /// <summary>An explicitly empty list is a scope of nothing rather than a request for the default.</summary>
    [Fact]
    public void Of_AClassifyingAccountNamingAnEmptyListOfFolders_ClassifiesNoFolder()
    {
        // Act
        var settings = MailAccountQueryableSettingsReading.Of(Record(
            """
            {
              "SpamClassification": { "Enabled": true, "ScannedFolders": [] },
              "Folders": [ { "Alias": "INBOX", "SpecialUse": "Inbox" } ]
            }
            """));

        // Assert
        Assert.Empty(ClassifiedAliases(settings));
    }

    /// <summary>A document this process could not bind, or would refuse to serve, is an account nobody is served, so its settings take part in nothing.</summary>
    [Theory]
    [InlineData("""{"NoSuchSetting":true}""")]
    [InlineData("[]")]
    [InlineData("""{"Port":0}""")]
    [InlineData("""{"SpamClassification":{"Enabled":true,"ScannedFolders":["IN\u0007BOX"]}}""")]
    public void Of_ADocumentThatDoesNotBind_ReadsTheUnreadableSettings(string document)
    {
        // Act
        var settings = MailAccountQueryableSettingsReading.Of(Record(document));

        // Assert
        Assert.Same(MailAccountQueryableSettings.Unreadable, settings);
    }

    private static MailFolderAlias[] ClassifiedAliases(MailAccountQueryableSettings settings) =>
        [.. settings.Folders.Where(folder => folder.IsClassifiedForSpam).Select(folder => folder.Alias)];

    private static MailAccountRecord Record(string document) =>
        new(AccountId, "alex@example.test", "Alex at work", ServableMailAccountDocuments.Completing(document), Version: 1);
}
