// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using Xunit;

namespace MailFathom.Domain.UnitTests.Access;

/// <summary>Covers how a grant is put together from what several assignments give, and how a narrowing takes from it.</summary>
/// <remarks>
/// The union is the whole evaluation rule: no deny, no order, and a permission held at several scopes is held at each.
/// A narrowing keeps names and never scopes, so what it can do is take a permission away and nothing else.
/// </remarks>
public sealed class ScopedGrantTests
{
    private static readonly AssignmentScope OneOrganization =
        AssignmentScope.Organization(new Guid("0198f0aa-0000-7000-8000-0000000000c1"));

    private static readonly AssignmentScope OneUser =
        AssignmentScope.User(UserId.Create(new Guid("0198f0aa-0000-7000-8000-0000000000c2")));

    private static readonly AssignmentScope AnotherOrganization =
        AssignmentScope.Organization(new Guid("0198f0aa-0000-7000-8000-0000000000c3"));

    private static readonly Guid AnotherUserId = new("0198f0aa-0000-7000-8000-0000000000c4");

    [Fact]
    public void Of_OnePermissionGivenAtTwoScopes_HoldsItAtBoth()
    {
        // Act
        var grant = ScopedGrant.Of([
            (MailFathomPermission.AdminRead, OneOrganization),
            (MailFathomPermission.AdminRead, OneUser),
        ]);

        // Assert
        Assert.Equal([MailFathomPermission.AdminRead], grant.Permissions);
        Assert.Equal(new HashSet<AssignmentScope> { OneOrganization, OneUser }, grant.ScopesOf(MailFathomPermission.AdminRead));
    }

    /// <summary>Two assignments giving the same name at the same scope — the user's own and a group's — are one holding rather than two.</summary>
    [Fact]
    public void Of_ThePermissionGivenTwiceAtOneScope_HoldsItOnceThere()
    {
        // Act
        var grant = ScopedGrant.Of([
            (MailFathomPermission.MailRead, OneUser),
            (MailFathomPermission.MailRead, AssignmentScope.User(UserId.Create(OneUser.Target))),
        ]);

        // Assert
        Assert.Equal([OneUser], grant.ScopesOf(MailFathomPermission.MailRead));
    }

    /// <summary>The union of several roles is every name any of them lists, each where its own assignment put it.</summary>
    [Fact]
    public void Of_SeveralPermissionsFromSeveralAssignments_HoldsEveryOneAtItsOwnScope()
    {
        // Act
        var grant = ScopedGrant.Of([
            (MailFathomPermission.MailRead, OneUser),
            (MailFathomPermission.AdminOperate, OneOrganization),
            (MailFathomPermission.AdminSpend, AssignmentScope.Deployment),
        ]);

        // Assert
        Assert.Equal(
            new HashSet<MailFathomPermission>
            {
                MailFathomPermission.MailRead,
                MailFathomPermission.AdminOperate,
                MailFathomPermission.AdminSpend,
            },
            grant.Permissions);
        Assert.Equal([OneOrganization], grant.ScopesOf(MailFathomPermission.AdminOperate));
        Assert.Equal([AssignmentScope.Deployment], grant.ScopesOf(MailFathomPermission.AdminSpend));
    }

    /// <summary>The struct default names no capability, which is what a stored name this build does not publish reads as, so it is held by nobody.</summary>
    [Fact]
    public void Of_AnUnspecifiedPermission_IsNotHeld()
    {
        // Act
        var grant = ScopedGrant.Of([(default(MailFathomPermission), AssignmentScope.Deployment)]);

        // Assert
        Assert.Empty(grant.Permissions);
    }

    [Fact]
    public void Of_APermissionWithNoScope_IsRefused()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => ScopedGrant.Of([(MailFathomPermission.MailRead, null!)]));
    }

    [Fact]
    public void AtDeployment_SomePermissions_HoldsEachOverTheWholeDeployment()
    {
        // Act
        var grant = ScopedGrant.AtDeployment([MailFathomPermission.MailRead, MailFathomPermission.AdminRead]);

        // Assert
        Assert.Equal([AssignmentScope.Deployment], grant.ScopesOf(MailFathomPermission.MailRead));
        Assert.Equal([AssignmentScope.Deployment], grant.ScopesOf(MailFathomPermission.AdminRead));
    }

    [Fact]
    public void ScopesOf_APermissionNotHeld_IsEmpty()
    {
        // Act & Assert
        Assert.Empty(ScopedGrant.None.ScopesOf(MailFathomPermission.MailRead));
    }

    /// <summary>
    /// A credential's list keeps the names it shares with the grant: a name the grant holds and the list omits is taken
    /// away, a name the list carries and the grant does not hold grants nothing, and every name kept stays at each scope
    /// it was held at.
    /// </summary>
    [Fact]
    public void NarrowedTo_ANarrowingOverlappingTheGrant_KeepsOnlyTheSharedNamesAtTheirOwnScopes()
    {
        // Arrange
        var grant = ScopedGrant.Of([
            (MailFathomPermission.AdminRead, OneOrganization),
            (MailFathomPermission.AdminRead, OneUser),
            (MailFathomPermission.AdminOperate, OneOrganization),
        ]);

        // Act
        var narrowed = grant.NarrowedTo([MailFathomPermission.AdminRead, MailFathomPermission.AdminSpend]);

        // Assert
        Assert.Equal([MailFathomPermission.AdminRead], narrowed.Permissions);
        Assert.Equal(new HashSet<AssignmentScope> { OneOrganization, OneUser }, narrowed.ScopesOf(MailFathomPermission.AdminRead));
    }

    [Fact]
    public void Covers_APermissionHeldOverTheDeployment_CoversAUserInNoOrganization()
    {
        // Arrange
        var grant = ScopedGrant.AtDeployment([MailFathomPermission.AdminRead]);

        // Act
        var covered = grant.Covers(MailFathomPermission.AdminRead, AdministrativeTarget.User(UserId.Create(OneUser.Target), organization: null));

        // Assert
        Assert.True(covered);
    }

    [Fact]
    public void Covers_APermissionHeldOverAnOrganization_CoversItsMemberAndNobodyElse()
    {
        // Arrange
        var grant = ScopedGrant.Of([(MailFathomPermission.AdminRead, OneOrganization)]);
        var user = UserId.Create(OneUser.Target);

        // Act
        bool[] covered =
        [
            grant.Covers(MailFathomPermission.AdminRead, AdministrativeTarget.User(user, OneOrganization.Target)),
            grant.Covers(MailFathomPermission.AdminRead, AdministrativeTarget.User(user, AnotherOrganization.Target)),
            grant.Covers(MailFathomPermission.AdminRead, AdministrativeTarget.User(user, organization: null)),
        ];

        // Assert
        Assert.Equal([true, false, false], covered);
    }

    [Fact]
    public void Covers_APermissionHeldOverAnOrganization_CoversThatOrganizationButNotTheDeployment()
    {
        // Arrange
        var grant = ScopedGrant.Of([(MailFathomPermission.AdminConfigurationWrite, OneOrganization)]);

        // Act
        bool[] covered =
        [
            grant.Covers(MailFathomPermission.AdminConfigurationWrite, AdministrativeTarget.OrganizationItself(OneOrganization.Target)),
            grant.Covers(MailFathomPermission.AdminConfigurationWrite, AdministrativeTarget.OrganizationItself(AnotherOrganization.Target)),
            grant.Covers(MailFathomPermission.AdminConfigurationWrite, AdministrativeTarget.Unplaced),
        ];

        // Assert
        Assert.Equal([true, false, false], covered);
    }

    /// <summary>A user scope reaches its user wherever they belong, and reaches no organization.</summary>
    [Fact]
    public void Covers_APermissionHeldOverOneUser_CoversThatUserAlone()
    {
        // Arrange
        var grant = ScopedGrant.Of([(MailFathomPermission.AdminRead, OneUser)]);
        var user = UserId.Create(OneUser.Target);

        // Act
        bool[] covered =
        [
            grant.Covers(MailFathomPermission.AdminRead, AdministrativeTarget.User(user, OneOrganization.Target)),
            grant.Covers(MailFathomPermission.AdminRead, AdministrativeTarget.User(user, organization: null)),
            grant.Covers(MailFathomPermission.AdminRead, AdministrativeTarget.User(UserId.Create(AnotherUserId), OneOrganization.Target)),
            grant.Covers(MailFathomPermission.AdminRead, AdministrativeTarget.OrganizationItself(OneOrganization.Target)),
        ];

        // Assert
        Assert.Equal([true, true, false, false], covered);
    }

    [Fact]
    public void Covers_APermissionNotHeld_CoversNothing()
    {
        // Arrange
        var grant = ScopedGrant.AtDeployment([MailFathomPermission.AdminRead]);

        // Act
        var covered = grant.Covers(MailFathomPermission.AdminErase, AdministrativeTarget.Unplaced);

        // Assert
        Assert.False(covered);
    }

    /// <summary>An empty narrowing — a credential written with an empty list, or a token carrying no permission scope — leaves nothing.</summary>
    [Fact]
    public void NarrowedTo_NothingNamed_HoldsNothing()
    {
        // Arrange
        var grant = ScopedGrant.AtDeployment(MailFathomPermission.All);

        // Act
        var narrowed = grant.NarrowedTo([]);

        // Assert
        Assert.Empty(narrowed.Permissions);
    }
}
