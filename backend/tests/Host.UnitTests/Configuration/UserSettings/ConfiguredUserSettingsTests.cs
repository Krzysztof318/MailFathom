// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.UserSettings;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.UserSettings;

/// <summary>
/// Covers which users a configuration source still reaches. One section is left that reaches anybody — the
/// deployment's own, which names no user and belongs to whichever sole user such a deployment holds — and what is read
/// is what the files say now rather than what the roster copied at the start.
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

    /// <summary>
    /// A user whose record is their own answers with nothing because their record decides their mailboxes, and a user
    /// this process's roster does not hold answers with nothing because no source has ever named them. Neither is a
    /// failure: both are users an ordinary write reaches.
    /// </summary>
    /// <param name="onTheRoster">Whether the roster holds the user asked about.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DeclaredFor_AUserNoConfigurationSourceReaches_ReadsNoMailbox(bool onTheRoster)
    {
        // Arrange
        var reading = onTheRoster
            ? Reading(DeploymentMailbox(), Serving(Alex, MailUserAccountSource.UserDocument))
            : Reading(DeploymentMailbox(), Serving(Morgan, MailUserAccountSource.DeploymentSection));

        // Act
        var declared = reading.DeclaredFor(Alex);

        // Assert
        Assert.Empty(declared);
    }

    /// <summary>The roster is what says which users a source reaches, and only the deployment's own section reaches one at all.</summary>
    [Fact]
    public void UsersAConfigurationSourceDeclares_ARosterOfBothKinds_NamesTheUserOfTheDeploymentSectionAlone()
    {
        // Arrange
        var reading = Reading(
            DeploymentMailbox(),
            Serving(Alex, MailUserAccountSource.DeploymentSection),
            Serving(Morgan, MailUserAccountSource.UserDocument));

        // Act
        var declared = reading.UsersAConfigurationSourceDeclares();

        // Assert
        Assert.Equal([Alex], declared);
    }

    /// <summary>The same question asked of one user, which is what an act a start would undo is refused by.</summary>
    [Fact]
    public void DeclaredByAConfigurationSource_AUserOnTheRoster_AnswersFromTheirSource()
    {
        // Arrange
        var section = Reading(DeploymentMailbox(), Serving(Alex, MailUserAccountSource.DeploymentSection));
        var record = Reading(DeploymentMailbox(), Serving(Alex, MailUserAccountSource.UserDocument));

        // Act
        var answers = new[] { section.DeclaredByAConfigurationSource(Alex), record.DeclaredByAConfigurationSource(Alex) };

        // Assert
        Assert.Equal([true, false], answers);
    }

    [Fact]
    public void DeclaredFor_AUserNamingNobody_IsRefused()
    {
        // Arrange
        var reading = Reading(new Dictionary<string, string?>(), Serving(Alex, MailUserAccountSource.DeploymentSection));

        // Act & Assert
        Assert.Throws<ArgumentException>(() => reading.DeclaredFor(default));
    }

    private static Dictionary<string, string?> DeploymentMailbox() => new()
    {
        ["MailSynchronization:Accounts:0:AccountId"] = "primary",
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
