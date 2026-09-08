// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.UserSettings;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.UserSettings;

/// <summary>
/// Covers where one user's configured mailboxes are read from and what adopting them would write. The two sections are
/// not interchangeable — the deployment's own names no user and belongs to whichever sole user such a deployment
/// holds, while a declared user's is a numbered entry of the user collection — and what an adoption moves is what the
/// files say now rather than what the roster copied at the start.
/// </summary>
public sealed class ConfiguredUserSettingsTests
{
    private static readonly MailUserId Alex =
        MailUserId.Create(new Guid("1a7f6b1c-2d3e-4f50-8a91-b2c3d4e5f601"));

    private static readonly MailUserId Morgan =
        MailUserId.Create(new Guid("2b8f7c2d-3e4f-4a61-9b02-c3d4e5f6a712"));

    /// <summary>A deployment holding one user declares their mailboxes in the section that names nobody.</summary>
    [Fact]
    public void DeclaredFor_AUserServedFromTheDeploymentSection_ReadsThatSectionsMailboxes()
    {
        // Arrange
        var reading = Reading(
            new Dictionary<string, string?>
            {
                ["MailSynchronization:Accounts:0:AccountId"] = "primary",
                ["MailSynchronization:Accounts:1:AccountId"] = "archive",
            },
            Serving(Alex, MailUserAccountSource.DeploymentSection));

        // Act
        var declared = reading.DeclaredFor(Alex);

        // Assert
        Assert.Equal(["primary", "archive"], declared.Select(account => account.AccountId));
    }

    /// <summary>A declared user's mailboxes are addressed by the position their declaration occupies, which is how a configuration key names an element.</summary>
    [Fact]
    public void DeclaredFor_AUserDeclaringTheirOwnMailboxes_ReadsTheEntryTheyAreDeclaredIn()
    {
        // Arrange
        var reading = Reading(
            DeclaredUserPair(),
            Serving(Alex, MailUserAccountSource.UserDeclaration),
            Serving(Morgan, MailUserAccountSource.UserDeclaration));

        // Act
        var declared = reading.DeclaredFor(Morgan);

        // Assert
        Assert.Equal(["morgan-work"], declared.Select(account => account.AccountId));
    }

    /// <summary>
    /// A source may number its entries with a gap, and the binder records no key: it appends one element per child, so
    /// the position a user bound at and the key an operator wrote come apart. Addressing by the position then reads a
    /// section nobody wrote, which is an adoption committing an empty record over mailboxes the file declares.
    /// </summary>
    [Fact]
    public void DeclaredFor_ACollectionNumberedWithAGap_ReadsTheEntryTheKeyNamesRatherThanThePosition()
    {
        // Arrange
        var reading = Reading(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Accounts:0:Id"] = Alex.Value.ToString("D"),
                ["Accounts:0:DisplayName"] = "alex",
                ["Accounts:0:MailAccounts:0:AccountId"] = "alex-work",
                ["Accounts:3:Id"] = Morgan.Value.ToString("D"),
                ["Accounts:3:DisplayName"] = "morgan",
                ["Accounts:3:MailAccounts:0:AccountId"] = "morgan-work",
            },
            Serving(Alex, MailUserAccountSource.UserDeclaration),
            Serving(Morgan, MailUserAccountSource.UserDeclaration));

        // Act
        var declared = reading.DeclaredFor(Morgan);

        // Assert
        Assert.Equal(["morgan-work"], declared.Select(account => account.AccountId));
    }

    /// <summary>
    /// A user who has adopted answers with nothing because their record is their own from now on, and a user this
    /// process's roster does not hold answers with nothing because no file has ever named them. Neither is a failure:
    /// both are users an ordinary write reaches.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SectionFor_AUserNoConfigurationSourceReaches_ReportsNothing(bool onTheRoster)
    {
        // Arrange
        var reading = onTheRoster
            ? Reading(DeclaredUserPair(), Serving(Alex, MailUserAccountSource.UserDocument))
            : Reading(DeclaredUserPair(), Serving(Morgan, MailUserAccountSource.UserDeclaration));

        // Act
        var section = reading.SectionFor(Alex);

        // Assert
        Assert.Null(section);
    }

    /// <summary>A declaration the file no longer carries is a file edited between the start that reconciled the roster and this read.</summary>
    [Fact]
    public void SectionFor_AUserTheRosterHoldsAndTheFileNoLongerDeclares_ReportsNothing()
    {
        // Arrange
        var reading = Reading(
            new Dictionary<string, string?>(),
            Serving(Alex, MailUserAccountSource.UserDeclaration));

        // Act
        var section = reading.SectionFor(Alex);

        // Assert
        Assert.Null(section);
    }

    [Fact]
    public void SectionFor_AUserNamingNobody_IsRefused()
    {
        // Arrange
        var reading = Reading(new Dictionary<string, string?>(), Serving(Alex, MailUserAccountSource.DeploymentSection));

        // Act & Assert
        Assert.Throws<ArgumentException>(() => reading.SectionFor(default));
    }

    /// <summary>
    /// The keys are taken relative to the section and re-rooted at the record's own collection, so a deployment section
    /// and a declared user's both land on the one property a user's record holds mailboxes under.
    /// </summary>
    [Fact]
    public void AdoptionEditsFor_AUserServedFromTheDeploymentSection_RerootsEveryKeyAtTheRecordsOwnCollection()
    {
        // Arrange
        var reading = Reading(
            new Dictionary<string, string?>
            {
                ["MailSynchronization:Accounts:0:AccountId"] = "primary",
                ["MailSynchronization:Accounts:0:Host"] = "mail.example.test",
            },
            Serving(Alex, MailUserAccountSource.DeploymentSection));

        // Act
        var edits = reading.AdoptionEditsFor(Alex);

        // Assert
        Assert.Equal(
            ["MailAccounts:0:AccountId=primary", "MailAccounts:0:Host=mail.example.test"],
            edits.Select(edit => $"{edit.Path}={edit.Value}"));
    }

    /// <summary>Whichever of the two sections an operator had been writing in, the same keys come out.</summary>
    [Fact]
    public void AdoptionEditsFor_AUserDeclaringTheirOwnMailboxes_RerootsEveryKeyAtTheSameCollection()
    {
        // Arrange
        var reading = Reading(
            DeclaredUserPair(),
            Serving(Alex, MailUserAccountSource.UserDeclaration),
            Serving(Morgan, MailUserAccountSource.UserDeclaration));

        // Act
        var edits = reading.AdoptionEditsFor(Morgan);

        // Assert
        Assert.Equal(["MailAccounts:0:AccountId=morgan-work"], edits.Select(edit => $"{edit.Path}={edit.Value}"));
    }

    /// <summary>
    /// A key survives a property the binder does not know about, a value written in a shape the type would have
    /// normalized, and a setting a later release adds — which is what makes an adoption a move rather than a rewrite.
    /// </summary>
    [Fact]
    public void AdoptionEditsFor_ASettingNothingBinds_CarriesItThroughAsTheOperatorWroteIt()
    {
        // Arrange
        var reading = Reading(
            new Dictionary<string, string?>
            {
                ["MailSynchronization:Accounts:0:AccountId"] = "primary",
                ["MailSynchronization:Accounts:0:SettingALaterReleaseAdds"] = "kept",
            },
            Serving(Alex, MailUserAccountSource.DeploymentSection));

        // Act
        var edits = reading.AdoptionEditsFor(Alex);

        // Assert
        Assert.Contains(edits, edit => edit.Path == "MailAccounts:0:SettingALaterReleaseAdds" && edit.Value == "kept");
    }

    /// <summary>A section enumerates itself under the empty key and a key whose value is null is a section rather than a setting; an edit composed from either would address nothing.</summary>
    [Fact]
    public void AdoptionEditsFor_ASectionCarryingSettings_StatesOneChangePerSettingAndNoneForTheSectionsThemselves()
    {
        // Arrange
        var reading = Reading(
            new Dictionary<string, string?>
            {
                ["MailSynchronization:Accounts:0:AccountId"] = "primary",
            },
            Serving(Alex, MailUserAccountSource.DeploymentSection));

        // Act
        var edits = reading.AdoptionEditsFor(Alex);

        // Assert
        var edit = Assert.Single(edits);
        Assert.Equal("MailAccounts:0:AccountId", edit.Path);
    }

    /// <summary>
    /// An adoption is a move rather than a rewrite, so the posture the deployment's section classified this user's mail
    /// under travels into the record with their mailboxes — otherwise a handover about where somebody's settings live
    /// would switch their spam protection off.
    /// </summary>
    [Fact]
    public void AdoptionEditsFor_ADeploymentClassifyingTheirMail_CarriesThatPostureIntoTheRecord()
    {
        // Arrange
        var reading = Reading(
            new Dictionary<string, string?>
            {
                ["MailSynchronization:Accounts:0:AccountId"] = "primary",
                ["SpamClassification:Enabled"] = "true",
                ["SpamClassification:ScannedFolders:0"] = "inbox",
                ["SpamClassification:Actions:MoveToJunkFolder"] = "true",
            },
            Serving(Alex, MailUserAccountSource.DeploymentSection));

        // Act
        var edits = reading.AdoptionEditsFor(Alex);

        // Assert
        Assert.Equal(
            [
                "MailAccounts:0:AccountId=primary",
                "SpamClassification:Actions:MoveToJunkFolder=true",
                "SpamClassification:Enabled=true",
                "SpamClassification:ScannedFolders:0=inbox",
            ],
            edits.Select(edit => $"{edit.Path}={edit.Value}"));
    }

    /// <summary>What the section states about the engine costs the deployment rather than the user, and a record may not hold it.</summary>
    [Fact]
    public void AdoptionEditsFor_ASectionStatingTheDeploymentsOwnEngineSettings_LeavesThemBehind()
    {
        // Arrange
        var reading = Reading(
            new Dictionary<string, string?>
            {
                ["MailSynchronization:Accounts:0:AccountId"] = "primary",
                ["SpamClassification:Enabled"] = "true",
                ["SpamClassification:ClassificationWait"] = "02:00:00",
                ["SpamClassification:ScanConcurrency"] = "4",
                ["SpamClassification:Scanner:Host"] = "spamd.example.test",
            },
            Serving(Alex, MailUserAccountSource.DeploymentSection));

        // Act
        var edits = reading.AdoptionEditsFor(Alex);

        // Assert
        Assert.Equal(
            ["MailAccounts:0:AccountId=primary", "SpamClassification:Enabled=true"],
            edits.Select(edit => $"{edit.Path}={edit.Value}"));
    }

    /// <summary>
    /// The scanning block moves with the mailboxes for the reason the classification posture does, and with more at
    /// stake: a handover that left it behind would stop scanning that user's mail — permanently, since no
    /// configuration source reaches an adopted user afterwards — on the strength of an administrative act about where
    /// their settings live, with nothing having said so.
    /// </summary>
    [Fact]
    public void AdoptionEditsFor_ADeclarationScanningTheirMail_CarriesThatBlockIntoTheRecord()
    {
        // Arrange
        var declared = DeclaredUserPair();
        declared["Accounts:0:SensitiveContent:Secrets:Enabled"] = "true";
        declared["Accounts:0:SensitiveContent:ScreenOutgoingMailFor:0"] = "Secrets";

        var reading = Reading(declared, Serving(Alex, MailUserAccountSource.UserDeclaration));

        // Act
        var edits = reading.AdoptionEditsFor(Alex);

        // Assert
        Assert.Equal(
            [
                "MailAccounts:0:AccountId=alex-work",
                "SensitiveContent:ScreenOutgoingMailFor:0=Secrets",
                "SensitiveContent:Secrets:Enabled=true",
            ],
            edits.Select(edit => $"{edit.Path}={edit.Value}"));
    }

    /// <summary>The block another user declared is theirs, and an adoption that carried it would scan one person's mail on another's answer.</summary>
    [Fact]
    public void SensitiveContentAdoptionFor_AnotherUsersDeclaredBlock_IsNotCarried()
    {
        // Arrange
        var declared = DeclaredUserPair();
        declared["Accounts:1:SensitiveContent:Secrets:Enabled"] = "true";

        var reading = Reading(declared, Serving(Alex, MailUserAccountSource.UserDeclaration));

        // Act
        var carried = reading.SensitiveContentAdoptionFor(Alex);

        // Assert
        Assert.Empty(carried);
    }

    [Fact]
    public void AdoptionEditsFor_AUserNoConfigurationSourceReaches_StatesNoChanges()
    {
        // Arrange
        var reading = Reading(
            DeclaredUserPair(),
            Serving(Alex, MailUserAccountSource.UserDocument));

        // Act
        var edits = reading.AdoptionEditsFor(Alex);

        // Assert
        Assert.Empty(edits);
    }

    private static Dictionary<string, string?> DeclaredUserPair() => new()
    {
        ["Accounts:0:Id"] = Alex.Value.ToString("D"),
        ["Accounts:0:DisplayName"] = "alex",
        ["Accounts:0:MailAccounts:0:AccountId"] = "alex-work",
        ["Accounts:1:Id"] = Morgan.Value.ToString("D"),
        ["Accounts:1:DisplayName"] = "morgan",
        ["Accounts:1:MailAccounts:0:AccountId"] = "morgan-work",
    };

    private static ServedMailUser Serving(MailUserId user, MailUserAccountSource source) =>
        new(user, $"user-{user.Value:D}", source, []);

    private static ConfiguredUserSettings Reading(
        IEnumerable<KeyValuePair<string, string?>> values,
        params ServedMailUser[] served)
    {
        var servedUsers = new ServedMailUsers();

        servedUsers.Resolved(served);

        return new(new ConfigurationBuilder().AddInMemoryCollection(values).Build(), servedUsers);
    }
}
