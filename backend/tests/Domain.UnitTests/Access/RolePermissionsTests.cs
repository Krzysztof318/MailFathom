// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using Xunit;

namespace MailFathom.Domain.UnitTests.Access;

/// <summary>Covers the two halves of a role's list: writing refuses a name nothing publishes, reading keeps one apart.</summary>
public sealed class RolePermissionsTests
{
    /// <summary>A name nothing publishes is a grant nobody enforces, which an operator would believe they had made.</summary>
    [Fact]
    public void TryCreate_ANameNothingPublishes_IsRefusedNamingIt()
    {
        // Arrange
        string[] written = ["mailfathom.mail.read", "mailfathom.mail.everything", "mailfathom.admin.*"];

        // Act
        var created = RolePermissions.TryCreate(written, out var permissions, out var unpublished);

        // Assert
        Assert.False(created);
        Assert.Null(permissions);
        Assert.Equal(["mailfathom.admin.*", "mailfathom.mail.everything"], unpublished);
    }

    [Fact]
    public void TryCreate_PublishedNamesWrittenTwiceAndOutOfOrder_ListsEachOnceInPublishedOrder()
    {
        // Arrange
        string[] written = ["mailfathom.admin.read", "mailfathom.mail.read", "mailfathom.admin.read"];

        // Act
        var created = RolePermissions.TryCreate(written, out var permissions, out var unpublished);

        // Assert
        Assert.True(created);
        Assert.Empty(unpublished);
        Assert.Equal([MailFathomPermission.MailRead, MailFathomPermission.AdminRead], permissions!.Granted);
    }

    /// <summary>
    /// A stored name a later build stopped publishing must not take the rest of the role with it, or every holder
    /// would lose every other name the role lists; it is reported and granted to nobody.
    /// </summary>
    [Fact]
    public void Read_AStoredNameThisBuildDoesNotPublish_IsReportedRatherThanGranted()
    {
        // Arrange
        string[] stored = ["mailfathom.mail.ask", "mailfathom.admin.retired"];

        // Act
        var permissions = RolePermissions.Read(stored);

        // Assert
        Assert.Equal([MailFathomPermission.MailAsk], permissions.Granted);
        Assert.Equal(["mailfathom.admin.retired"], permissions.Unpublished);
    }

    [Fact]
    public void Of_TheUnspecifiedDefault_IsRefused()
    {
        // Arrange
        MailFathomPermission[] written = [MailFathomPermission.MailRead, default];

        // Act & Assert
        Assert.Throws<ArgumentException>(() => RolePermissions.Of(written));
    }

    [Fact]
    public void Of_APermissionWrittenTwice_IsListedOnce()
    {
        // Arrange
        MailFathomPermission[] written = [MailFathomPermission.AdminSpend, MailFathomPermission.AdminSpend];

        // Act
        var permissions = RolePermissions.Of(written);

        // Assert
        Assert.Equal([MailFathomPermission.AdminSpend], permissions.Granted);
        Assert.Empty(permissions.Unpublished);
    }
}
