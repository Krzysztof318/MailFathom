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

    /// <summary>No outcome is answered for a foreign key the assignment does not declare, so the caller raises it with the violation attached instead of naming the wrong thing as missing.</summary>
    [Fact]
    public void MissingReferenceOf_AConstraintNoAssignmentDeclares_AnswersNoOutcome()
    {
        // Act
        var outcome = PersistedGrants.MissingReferenceOf("fk_somewhere_else");

        // Assert
        Assert.Null(outcome);
    }

    /// <summary>A group in an organization holds only that organization's members, and a group in none is the deployment's and holds anybody.</summary>
    [Theory]
    [InlineData("0198f0aa-0000-7000-8000-0000000000c4", "0198f0aa-0000-7000-8000-0000000000c4", true)]
    [InlineData("0198f0aa-0000-7000-8000-0000000000c4", "0198f0aa-0000-7000-8000-0000000000c6", false)]
    [InlineData("0198f0aa-0000-7000-8000-0000000000c4", null, false)]
    [InlineData(null, "0198f0aa-0000-7000-8000-0000000000c4", true)]
    [InlineData(null, null, true)]
    public void AdmitsMember_TheGroupsAndTheUsersOrganization_AdmitOnlyAnOrganizationsOwnMembersIntoItsGroups(
        string? groupOrganization,
        string? memberOrganization,
        bool admitted)
    {
        // Act
        var admits = PersistedGrants.AdmitsMember(
            groupOrganization is null ? null : Guid.Parse(groupOrganization),
            memberOrganization is null ? null : Guid.Parse(memberOrganization));

        // Assert
        Assert.Equal(admitted, admits);
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

    [Fact]
    public void SourcesOf_ANameGivenThroughAGroupAtAnOrganization_NamesTheRoleTheGroupAndTheScope()
    {
        // Arrange
        var assignment = new Guid("0198f0aa-0000-7000-8000-0000000000c7");
        GrantSourceRow row = new("mailfathom.admin.read", Role, "Readers", assignment, Group, "Operators", Organization, ScopeUserId: null);

        // Act
        var sources = PersistedGrants.SourcesOf(row);

        // Assert
        Assert.Equal(
            [
                new GrantSource(
                    MailFathomPermission.AdminRead,
                    Pattern: null,
                    Role,
                    "Readers",
                    assignment,
                    Group,
                    "Operators",
                    AssignmentScope.Organization(Organization)),
            ],
            sources);
    }

    /// <summary>A name nobody wrote out is traced to the entry that reaches it: one source per permission the stored pattern reaches, each naming the pattern as written.</summary>
    [Fact]
    public void SourcesOf_AStoredPattern_NamesThePatternBesideEveryPermissionItReaches()
    {
        // Arrange
        var assignment = new Guid("0198f0aa-0000-7000-8000-0000000000c8");
        GrantSourceRow row = new("mailfathom.mail.contacts.*", Role, "Contacts", assignment, GroupId: null, GroupName: null, ScopeOrganizationId: null, User);

        // Act
        var sources = PersistedGrants.SourcesOf(row);

        // Assert
        Assert.Equal(
            [MailFathomPermission.MailContactsRead, MailFathomPermission.MailContactsWrite],
            sources.Select(source => source.Permission));
        Assert.All(sources, source =>
        {
            Assert.Equal("mailfathom.mail.contacts.*", source.Pattern);
            Assert.Equal(assignment, source.AssignmentId);
            Assert.Equal(AssignmentScope.User(UserId.Create(User)), source.Scope);
        });
    }

    /// <summary>A role may list an entry that grants nothing in this build — a name it does not publish, a pattern reaching nothing — which explains nothing.</summary>
    [Theory]
    [InlineData("mailfathom.admin.everything")]
    [InlineData("mailfathom.retired.*")]
    [InlineData("mailfathom.mail.c*")]
    public void SourcesOf_AnEntryThatGrantsNothingInThisBuild_IsNoSource(string storedEntry)
    {
        // Arrange
        GrantSourceRow row = new(storedEntry, Role, "Readers", Guid.NewGuid(), GroupId: null, GroupName: null, ScopeOrganizationId: null, ScopeUserId: null);

        // Act
        var sources = PersistedGrants.SourcesOf(row);

        // Assert
        Assert.Empty(sources);
    }

    /// <summary>
    /// What a user's grant is computed from, one stored row at a time: a pattern gives everything this build publishes
    /// in its reach at the assignment's scope, which is how a holder comes to hold a newly published permission without
    /// anybody writing to the role.
    /// </summary>
    [Fact]
    public void GivenBy_AStoredPatternAtAnOrganization_GivesEveryPermissionItReachesAtThatScope()
    {
        // Act
        var given = PersistedGrants.GivenBy("mailfathom.admin.*.write", Organization, scopeUserId: null).ToArray();

        // Assert
        Assert.Equal(
            MailFathomPermission.All
                .Where(permission => permission.Name.StartsWith("mailfathom.admin.", StringComparison.Ordinal)
                    && permission.Name.EndsWith(".write", StringComparison.Ordinal))
                .Select(permission => (permission, AssignmentScope.Organization(Organization))),
            given);
        Assert.Contains((MailFathomPermission.AdminRolesWrite, AssignmentScope.Organization(Organization)), given);
    }

    [Theory]
    [InlineData("mailfathom.admin.everything")]
    [InlineData("mailfathom.retired.*")]
    public void GivenBy_AStoredEntryThatGrantsNothingInThisBuild_GivesNothing(string storedEntry)
    {
        // Act
        var given = PersistedGrants.GivenBy(storedEntry, scopeOrganizationId: null, scopeUserId: null);

        // Assert
        Assert.Empty(given);
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
