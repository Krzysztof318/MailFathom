// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using Xunit;

namespace MailFathom.Domain.UnitTests.Access;

/// <summary>Covers the two halves of a role's list — writing refuses an entry that grants nothing, reading keeps one apart — and what a pattern on the list resolves to.</summary>
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
        Assert.Equal(["mailfathom.mail.everything"], unpublished);
    }

    /// <summary>A pattern is well formed whatever it reaches, so one reaching nothing is refused for that rather than accepted as a grant that means nothing.</summary>
    [Theory]
    [InlineData("mailfathom.billing.*")]
    [InlineData("mailfathom.admin.roles.write.*")]
    [InlineData("*.*.*.*.*.*")]
    public void TryCreate_APatternReachingNothingPublished_IsRefusedNamingIt(string pattern)
    {
        // Arrange
        string[] written = ["mailfathom.mail.read", pattern];

        // Act
        var created = RolePermissions.TryCreate(written, out var permissions, out var unpublished);

        // Assert
        Assert.False(created);
        Assert.Null(permissions);
        Assert.Equal([pattern], unpublished);
    }

    /// <summary>A wildcard is a whole segment or the value is not a pattern, so it is refused as the unpublished name it then is.</summary>
    [Theory]
    [InlineData("mailfathom.mail.c*")]
    [InlineData("mailfathom.*read")]
    [InlineData("mailfathom..*")]
    public void TryCreate_AWildcardInsideASegment_IsRefusedAsANameNothingPublishes(string written)
    {
        // Act
        var created = RolePermissions.TryCreate([written], out var permissions, out var unpublished);

        // Assert
        Assert.False(created);
        Assert.Null(permissions);
        Assert.Equal([written], unpublished);
    }

    /// <summary>The syntax a role accepts: a wildcard at the end, in the middle, and before the last segment, beside a name written out.</summary>
    [Fact]
    public void TryCreate_PatternsBesideAName_GrantsTheNameAndEverythingEachPatternReaches()
    {
        // Arrange
        string[] written = ["mailfathom.mail.read", "mailfathom.admin.*.write", "mailfathom.*.read"];

        // Act
        var created = RolePermissions.TryCreate(written, out var permissions, out var unpublished);

        // Assert
        Assert.True(created);
        Assert.Empty(unpublished);
        Assert.Equal([MailFathomPermission.MailRead], permissions!.Names);
        Assert.Equal(["mailfathom.*.read", "mailfathom.admin.*.write"], permissions.Patterns.Select(pattern => pattern.Written));
        Assert.Equal(
            MailFathomPermission.All.Where(permission =>
                permission == MailFathomPermission.MailRead
                || permission.Name.EndsWith(".read", StringComparison.Ordinal)
                || (permission.Name.StartsWith("mailfathom.admin.", StringComparison.Ordinal)
                    && permission.Name.EndsWith(".write", StringComparison.Ordinal)
                    && permission.Name.Split('.').Length > 3)),
            permissions.Granted);
        Assert.Contains(MailFathomPermission.AdminRolesWrite, permissions.Granted);
        Assert.DoesNotContain(MailFathomPermission.AdminOperate, permissions.Granted);
    }

    /// <summary>The whole published set, both halves, is one entry, which is what the seeded administrator's role lists.</summary>
    [Fact]
    public void TryCreate_TheWildcardAlone_GrantsEveryPublishedPermissionOfBothHalves()
    {
        // Act
        var created = RolePermissions.TryCreate(["*"], out var permissions, out _);

        // Assert
        Assert.True(created);
        Assert.Empty(permissions!.Names);
        Assert.Equal(MailFathomPermission.All, permissions.Granted);
        Assert.Equal(["*"], permissions.Written);
    }

    /// <summary>What is stored is what was written, so a pattern still means its reach after a release adds to it and is never frozen into the names it reached the day it was saved.</summary>
    [Fact]
    public void Written_NamesAndPatternsWrittenTwiceAndOutOfOrder_ListsEachNameThenEachPatternOnceAsWritten()
    {
        // Arrange
        string[] written = ["mailfathom.admin.*", "mailfathom.admin.read", "mailfathom.mail.read", "mailfathom.admin.*"];

        // Act
        RolePermissions.TryCreate(written, out var permissions, out _);

        // Assert
        Assert.Equal(["mailfathom.mail.read", "mailfathom.admin.read", "mailfathom.admin.*"], permissions!.Written);
    }

    [Theory]
    [InlineData("mailfathom.admin.*", true)]
    [InlineData("mailfathom.admin.read", false)]
    public void WidensOnUpgrade_AListWrittenWithOrWithoutAPattern_IsTrueOnlyOfThePattern(string written, bool widens)
    {
        // Act
        RolePermissions.TryCreate([written], out var permissions, out _);

        // Assert
        Assert.Equal(widens, permissions!.WidensOnUpgrade);
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

    /// <summary>
    /// A stored pattern that came to reach nothing is reported as an unpublished name is and left out of what the role
    /// is written back as, but it still makes the role one that widens: the next release may publish beneath it.
    /// </summary>
    [Fact]
    public void Read_AStoredPatternReachingNothing_IsReportedGrantsNothingAndStillWidens()
    {
        // Arrange
        string[] stored = ["mailfathom.mail.ask", "mailfathom.retired.*"];

        // Act
        var permissions = RolePermissions.Read(stored);

        // Assert
        Assert.Equal([MailFathomPermission.MailAsk], permissions.Granted);
        Assert.Equal(["mailfathom.retired.*"], permissions.Unpublished);
        Assert.Equal(["mailfathom.mail.ask"], permissions.Written);
        Assert.True(permissions.WidensOnUpgrade);
    }

    [Theory]
    [InlineData("mailfathom.mail.ask", new[] { "mailfathom.mail.ask" })]
    [InlineData("mailfathom.mail.contacts.*", new[] { "mailfathom.mail.contacts.read", "mailfathom.mail.contacts.write" })]
    [InlineData("mailfathom.retired.*", new string[0])]
    [InlineData("mailfathom.admin.retired", new string[0])]
    [InlineData("mailfathom.mail.c*", new string[0])]
    [InlineData(null, new string[0])]
    public void GrantedBy_OneStoredEntry_IsTheNameItIsWhatThePatternReachesOrNothing(string? stored, string[] granted)
    {
        // Act
        var permissions = RolePermissions.GrantedBy(stored);

        // Assert
        Assert.Equal(granted, permissions.Select(permission => permission.Name));
    }

    /// <summary>What the last-root refusal compares a stored entry against, so a role listing a pattern reaching the root counts as holding it.</summary>
    [Fact]
    public void EntriesGranting_ThePermissionTheRootIs_ListsItsNameAndEveryPatternReachingIt()
    {
        // Act
        var entries = RolePermissions.EntriesGranting(MailFathomPermission.AdminRolesWrite);

        // Assert
        Assert.Equal("mailfathom.admin.roles.write", entries[0]);
        Assert.Equal(PermissionSubtree.WritingsReaching(MailFathomPermission.AdminRolesWrite), entries.Skip(1));
        Assert.All(entries, entry => Assert.Contains(MailFathomPermission.AdminRolesWrite, RolePermissions.GrantedBy(entry)));
        Assert.DoesNotContain("mailfathom.mail.*", entries);
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
