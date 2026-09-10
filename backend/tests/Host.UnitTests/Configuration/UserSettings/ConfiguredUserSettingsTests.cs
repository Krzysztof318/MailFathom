// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.UserSettings;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.UserSettings;

/// <summary>
/// Covers which users a configuration source still reaches, which is nobody: the collection that declared them and the
/// deployment's own mail section are both withdrawn, so every user is a record and every mailbox is in it. The reading
/// is asked anyway because three refusals are written against it, and it answers the same way for every user.
/// </summary>
public sealed class ConfiguredUserSettingsTests
{
    private static readonly MailUserId Alex =
        MailUserId.Create(new Guid("1a7f6b1c-2d3e-4f50-8a91-b2c3d4e5f601"));

    /// <summary>No source declares a mailbox any more, so the answer is the same for a user this deployment holds and for one it does not.</summary>
    [Fact]
    public void DeclaredFor_AnyUser_ReadsNoMailbox()
    {
        // Arrange
        var reading = new ConfiguredUserSettings();

        // Act
        var declared = reading.DeclaredFor(Alex);

        // Assert
        Assert.Empty(declared);
    }

    /// <summary>The two questions a refusal is written against answer that no source names anybody.</summary>
    [Fact]
    public void UsersAConfigurationSourceDeclares_ADeploymentServingAnybody_NamesNobody()
    {
        // Arrange
        var reading = new ConfiguredUserSettings();

        // Act
        var declared = reading.UsersAConfigurationSourceDeclares();

        // Assert
        Assert.Empty(declared);
        Assert.False(reading.DeclaredByAConfigurationSource(Alex));
    }

    /// <summary>Both readings that take a user still refuse one naming nobody, because a caller asking about no user has a defect rather than an answer.</summary>
    [Fact]
    public void DeclaredFor_AUserNamingNobody_IsRefused()
    {
        // Arrange
        var reading = new ConfiguredUserSettings();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => reading.DeclaredFor(default));
        Assert.Throws<ArgumentException>(() => reading.DeclaredByAConfigurationSource(default));
    }
}
