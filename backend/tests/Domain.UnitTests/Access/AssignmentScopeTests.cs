// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using Xunit;

namespace MailFathom.Domain.UnitTests.Access;

/// <summary>Covers what a role assignment may name as its scope and as its principal.</summary>
/// <remarks>
/// The empty identifier is what an unset column and an unread value both look like, so neither type accepts one as a
/// target: a scope or a principal that names nothing must never be mistaken for one that names somebody.
/// </remarks>
public sealed class AssignmentScopeTests
{
    private static readonly Guid Organization = new("0198f0aa-0000-7000-8000-0000000000b1");

    private static readonly UserId User = UserId.Create(new Guid("0198f0aa-0000-7000-8000-0000000000b2"));

    [Fact]
    public void Deployment_NamesNoTarget()
    {
        // Act
        var scope = AssignmentScope.Deployment;

        // Assert
        Assert.Equal(AssignmentScopeKind.Deployment, scope.Kind);
        Assert.Equal(Guid.Empty, scope.Target);
    }

    [Fact]
    public void Organization_AnOrganization_IsTheScopeNamingIt()
    {
        // Act
        var scope = AssignmentScope.Organization(Organization);

        // Assert
        Assert.Equal(AssignmentScopeKind.Organization, scope.Kind);
        Assert.Equal(Organization, scope.Target);
    }

    [Fact]
    public void User_AUser_IsTheScopeNamingThem()
    {
        // Act
        var scope = AssignmentScope.User(User);

        // Assert
        Assert.Equal(AssignmentScopeKind.User, scope.Kind);
        Assert.Equal(User.Value, scope.Target);
    }

    [Fact]
    public void Organization_TheEmptyIdentifier_IsRefused()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => AssignmentScope.Organization(Guid.Empty));
    }

    [Fact]
    public void User_NobodyNamed_IsRefused()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => AssignmentScope.User(default));
    }

    [Fact]
    public void PrincipalUser_AUser_IsThePrincipalNamingThem()
    {
        // Act
        var principal = AssignmentPrincipal.User(User);

        // Assert
        Assert.Equal(AssignmentPrincipalKind.User, principal.Kind);
        Assert.Equal(User.Value, principal.Id);
    }

    [Fact]
    public void PrincipalUser_NobodyNamed_IsRefused()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => AssignmentPrincipal.User(default));
    }

    [Fact]
    public void PrincipalGroup_TheEmptyIdentifier_IsRefused()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => AssignmentPrincipal.Group(Guid.Empty));
    }
}
