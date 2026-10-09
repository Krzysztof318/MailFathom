// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Grants;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Grants;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Grants;

/// <summary>Covers what the store makes of a stored assignment and of a foreign key refusing one.</summary>
public sealed class PersistedGrantsTests
{
    private static readonly Guid Role = new("0198f0aa-0000-7000-8000-0000000000c1");

    private static readonly Guid User = new("0198f0aa-0000-7000-8000-0000000000c2");

    private static readonly Guid Group = new("0198f0aa-0000-7000-8000-0000000000c3");

    private static readonly Guid Organization = new("0198f0aa-0000-7000-8000-0000000000c4");

    /// <summary>An assignment naming something that does not exist is answered by which thing it was, so an administrator is told what to fix.</summary>
    [Theory]
    [InlineData(PersistenceConstraintNames.RoleAssignmentRoleForeignKeyName, GrantWriteOutcome.UnknownRole)]
    [InlineData(PersistenceConstraintNames.RoleAssignmentPrincipalGroupForeignKeyName, GrantWriteOutcome.UnknownGroup)]
    [InlineData(PersistenceConstraintNames.RoleAssignmentPrincipalUserForeignKeyName, GrantWriteOutcome.UnknownUser)]
    [InlineData(PersistenceConstraintNames.RoleAssignmentScopeUserForeignKeyName, GrantWriteOutcome.UnknownUser)]
    [InlineData(PersistenceConstraintNames.RoleAssignmentScopeOrganizationForeignKeyName, GrantWriteOutcome.UnknownOrganization)]
    public void MissingReferenceOf_TheForeignKeyThatRefusedAnAssignment_NamesWhatWasMissing(
        string constraintName,
        GrantWriteOutcome expected)
    {
        // Act
        var outcome = PersistedGrants.MissingReferenceOf(constraintName);

        // Assert
        Assert.Equal(expected, outcome);
    }

    [Fact]
    public void MissingReferenceOf_AConstraintNoAssignmentDeclares_IsADefectRatherThanAnAnswer()
    {
        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => PersistedGrants.MissingReferenceOf("fk_somewhere_else"));
    }

    [Fact]
    public void AssignmentOf_AUserAtTheDeployment_ReadsNeitherScopeColumnAsTheDeployment()
    {
        // Arrange
        var stored = Stored(principalUser: User);

        // Act
        var assignment = PersistedGrants.AssignmentOf(stored);

        // Assert
        Assert.Equal(AssignmentPrincipal.User(UserId.Create(User)), assignment.Principal);
        Assert.Equal(AssignmentScope.Deployment, assignment.Scope);
        Assert.Equal(Role, assignment.RoleId);
    }

    [Fact]
    public void AssignmentOf_AGroupAtAnOrganization_ReadsTheGroupAndTheOrganization()
    {
        // Arrange
        var stored = Stored(principalGroup: Group, scopeOrganization: Organization);

        // Act
        var assignment = PersistedGrants.AssignmentOf(stored);

        // Assert
        Assert.Equal(AssignmentPrincipal.Group(Group), assignment.Principal);
        Assert.Equal(AssignmentScope.Organization(Organization), assignment.Scope);
    }

    [Fact]
    public void AssignmentOf_AUserAtTheirOwnScope_ReadsTheUserScope()
    {
        // Arrange
        var stored = Stored(principalUser: User, scopeUser: User);

        // Act
        var assignment = PersistedGrants.AssignmentOf(stored);

        // Assert
        Assert.Equal(AssignmentScope.User(UserId.Create(User)), assignment.Scope);
    }

    private static RoleAssignmentEntity Stored(
        Guid? principalUser = null,
        Guid? principalGroup = null,
        Guid? scopeOrganization = null,
        Guid? scopeUser = null) => new()
        {
            Id = new Guid("0198f0aa-0000-7000-8000-0000000000c5"),
            RoleId = Role,
            PrincipalUserId = principalUser,
            PrincipalGroupId = principalGroup,
            ScopeOrganizationId = scopeOrganization,
            ScopeUserId = scopeUser,
            AssignedAt = new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.Zero),
        };
}
