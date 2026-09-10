// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.UserSettings;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.UserSettings;

/// <summary>
/// Covers where one user's configured mailboxes are read from. The two sections are not interchangeable — the
/// deployment's own names no user and belongs to whichever sole user such a deployment holds, while a declared user's
/// is a numbered entry of the user collection — and what is read is what the files say now rather than what the roster
/// copied at the start.
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
    /// section nobody wrote, which is one user's declared mailboxes reading as somebody else's.
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
    /// A user whose record is their own answers with nothing because their record decides their mailboxes from now on,
    /// and a user this process's roster does not hold answers with nothing because no file has ever named them. Neither
    /// is a failure: both are users an ordinary write reaches.
    /// </summary>
    /// <param name="onTheRoster">Whether the roster holds the user asked about.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DeclaredFor_AUserNoConfigurationSourceReaches_ReadsNoMailbox(bool onTheRoster)
    {
        // Arrange
        var reading = onTheRoster
            ? Reading(DeclaredUserPair(), Serving(Alex, MailUserAccountSource.UserDocument))
            : Reading(DeclaredUserPair(), Serving(Morgan, MailUserAccountSource.UserDeclaration));

        // Act
        var declared = reading.DeclaredFor(Alex);

        // Assert
        Assert.Empty(declared);
    }

    /// <summary>A declaration the file no longer carries is a file edited between the start that reconciled the roster and this read.</summary>
    [Fact]
    public void DeclaredFor_AUserTheRosterHoldsAndTheFileNoLongerDeclares_ReadsNoMailbox()
    {
        // Arrange
        var reading = Reading(
            new Dictionary<string, string?>(),
            Serving(Alex, MailUserAccountSource.UserDeclaration));

        // Act
        var declared = reading.DeclaredFor(Alex);

        // Assert
        Assert.Empty(declared);
    }

    [Fact]
    public void DeclaredFor_AUserNamingNobody_IsRefused()
    {
        // Arrange
        var reading = Reading(new Dictionary<string, string?>(), Serving(Alex, MailUserAccountSource.DeploymentSection));

        // Act & Assert
        Assert.Throws<ArgumentException>(() => reading.DeclaredFor(default));
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
