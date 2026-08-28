// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.OwnerSettings;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.OwnerSettings;

/// <summary>
/// Covers where one owner's configured mailboxes are read from and what adopting them would write. The two sections are
/// not interchangeable — the deployment's own names no owner and belongs to whichever sole owner such a deployment
/// holds, while a declared owner's is a numbered entry of the owner collection — and what an adoption moves is what the
/// files say now rather than what the roster copied at the start.
/// </summary>
public sealed class ConfiguredOwnerMailAccountsTests
{
    private static readonly MailOwnerId Alex =
        MailOwnerId.Create(new Guid("1a7f6b1c-2d3e-4f50-8a91-b2c3d4e5f601"));

    private static readonly MailOwnerId Morgan =
        MailOwnerId.Create(new Guid("2b8f7c2d-3e4f-4a61-9b02-c3d4e5f6a712"));

    /// <summary>A deployment holding one owner declares their mailboxes in the section that names nobody.</summary>
    [Fact]
    public void DeclaredFor_AnOwnerServedFromTheDeploymentSection_ReadsThatSectionsMailboxes()
    {
        // Arrange
        var reading = Reading(
            new Dictionary<string, string?>
            {
                ["MailSynchronization:Accounts:0:AccountId"] = "primary",
                ["MailSynchronization:Accounts:1:AccountId"] = "archive",
            },
            Serving(Alex, MailOwnerAccountSource.DeploymentSection));

        // Act
        var declared = reading.DeclaredFor(Alex);

        // Assert
        Assert.Equal(["primary", "archive"], declared.Select(account => account.AccountId));
    }

    /// <summary>A declared owner's mailboxes are addressed by the position their declaration occupies, which is how a configuration key names an element.</summary>
    [Fact]
    public void DeclaredFor_AnOwnerDeclaringTheirOwnMailboxes_ReadsTheEntryTheyAreDeclaredIn()
    {
        // Arrange
        var reading = Reading(
            DeclaredOwnerPair(),
            Serving(Alex, MailOwnerAccountSource.OwnerDeclaration),
            Serving(Morgan, MailOwnerAccountSource.OwnerDeclaration));

        // Act
        var declared = reading.DeclaredFor(Morgan);

        // Assert
        Assert.Equal(["morgan-work"], declared.Select(account => account.AccountId));
    }

    /// <summary>
    /// An owner who has adopted answers with nothing because their record is their own from now on, and an owner this
    /// process's roster does not hold answers with nothing because no file has ever named them. Neither is a failure:
    /// both are owners an ordinary write reaches.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SectionFor_AnOwnerNoConfigurationSourceReaches_ReportsNothing(bool onTheRoster)
    {
        // Arrange
        var reading = onTheRoster
            ? Reading(DeclaredOwnerPair(), Serving(Alex, MailOwnerAccountSource.OwnerDocument))
            : Reading(DeclaredOwnerPair(), Serving(Morgan, MailOwnerAccountSource.OwnerDeclaration));

        // Act
        var section = reading.SectionFor(Alex);

        // Assert
        Assert.Null(section);
    }

    /// <summary>A declaration the file no longer carries is a file edited between the start that reconciled the roster and this read.</summary>
    [Fact]
    public void SectionFor_AnOwnerTheRosterHoldsAndTheFileNoLongerDeclares_ReportsNothing()
    {
        // Arrange
        var reading = Reading(
            new Dictionary<string, string?>(),
            Serving(Alex, MailOwnerAccountSource.OwnerDeclaration));

        // Act
        var section = reading.SectionFor(Alex);

        // Assert
        Assert.Null(section);
    }

    [Fact]
    public void SectionFor_AnOwnerNamingNobody_IsRefused()
    {
        // Arrange
        var reading = Reading(new Dictionary<string, string?>(), Serving(Alex, MailOwnerAccountSource.DeploymentSection));

        // Act & Assert
        Assert.Throws<ArgumentException>(() => reading.SectionFor(default));
    }

    /// <summary>
    /// The keys are taken relative to the section and re-rooted at the record's own collection, so a deployment section
    /// and a declared owner's both land on the one property an owner's record holds mailboxes under.
    /// </summary>
    [Fact]
    public void AdoptionEditsFor_AnOwnerServedFromTheDeploymentSection_RerootsEveryKeyAtTheRecordsOwnCollection()
    {
        // Arrange
        var reading = Reading(
            new Dictionary<string, string?>
            {
                ["MailSynchronization:Accounts:0:AccountId"] = "primary",
                ["MailSynchronization:Accounts:0:Host"] = "mail.example.test",
            },
            Serving(Alex, MailOwnerAccountSource.DeploymentSection));

        // Act
        var edits = reading.AdoptionEditsFor(Alex);

        // Assert
        Assert.Equal(
            ["MailAccounts:0:AccountId=primary", "MailAccounts:0:Host=mail.example.test"],
            edits.Select(edit => $"{edit.Path}={edit.Value}"));
    }

    /// <summary>Whichever of the two sections an operator had been writing in, the same keys come out.</summary>
    [Fact]
    public void AdoptionEditsFor_AnOwnerDeclaringTheirOwnMailboxes_RerootsEveryKeyAtTheSameCollection()
    {
        // Arrange
        var reading = Reading(
            DeclaredOwnerPair(),
            Serving(Alex, MailOwnerAccountSource.OwnerDeclaration),
            Serving(Morgan, MailOwnerAccountSource.OwnerDeclaration));

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
            Serving(Alex, MailOwnerAccountSource.DeploymentSection));

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
            Serving(Alex, MailOwnerAccountSource.DeploymentSection));

        // Act
        var edits = reading.AdoptionEditsFor(Alex);

        // Assert
        var edit = Assert.Single(edits);
        Assert.Equal("MailAccounts:0:AccountId", edit.Path);
    }

    [Fact]
    public void AdoptionEditsFor_AnOwnerNoConfigurationSourceReaches_StatesNoChanges()
    {
        // Arrange
        var reading = Reading(
            DeclaredOwnerPair(),
            Serving(Alex, MailOwnerAccountSource.OwnerDocument));

        // Act
        var edits = reading.AdoptionEditsFor(Alex);

        // Assert
        Assert.Empty(edits);
    }

    private static Dictionary<string, string?> DeclaredOwnerPair() => new()
    {
        ["Accounts:0:Id"] = Alex.Value.ToString("D"),
        ["Accounts:0:DisplayName"] = "alex",
        ["Accounts:0:MailAccounts:0:AccountId"] = "alex-work",
        ["Accounts:1:Id"] = Morgan.Value.ToString("D"),
        ["Accounts:1:DisplayName"] = "morgan",
        ["Accounts:1:MailAccounts:0:AccountId"] = "morgan-work",
    };

    private static ServedMailOwner Serving(MailOwnerId owner, MailOwnerAccountSource source) =>
        new(owner, $"owner-{owner.Value:D}", source, []);

    private static ConfiguredOwnerMailAccounts Reading(
        IEnumerable<KeyValuePair<string, string?>> values,
        params ServedMailOwner[] served)
    {
        var servedOwners = new ServedMailOwners();

        servedOwners.Resolved(served);

        return new(new ConfigurationBuilder().AddInMemoryCollection(values).Build(), servedOwners);
    }
}
