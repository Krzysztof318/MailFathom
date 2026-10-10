// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using Xunit;

namespace MailFathom.Domain.UnitTests.Access;

/// <summary>Covers which scopes reach a user and a mail account, as ADR 0012 places each of them.</summary>
public sealed class AdministrativeTargetTests
{
    private static readonly Guid Organization = new("0198f0aa-0000-7000-8000-0000000000d1");

    private static readonly Guid OtherOrganization = new("0198f0aa-0000-7000-8000-0000000000d2");

    private static readonly UserId Person = UserId.Create(new Guid("0198f0aa-0000-7000-8000-0000000000d3"));

    private static readonly UserId Colleague = UserId.Create(new Guid("0198f0aa-0000-7000-8000-0000000000d4"));

    [Fact]
    public void IsCoveredBy_TheDeployment_CoversATargetInNoOrganization()
    {
        // Act
        var covered = AdministrativeTarget.Unplaced.IsCoveredBy(AssignmentScope.Deployment);

        // Assert
        Assert.True(covered);
    }

    [Fact]
    public void IsCoveredBy_AUsersOrganization_CoversTheUserAndNoOtherOrganizationDoes()
    {
        // Arrange
        var target = AdministrativeTarget.User(Person, Organization);

        // Act
        var byOwnOrganization = target.IsCoveredBy(AssignmentScope.Organization(Organization));
        var byOtherOrganization = target.IsCoveredBy(AssignmentScope.Organization(OtherOrganization));

        // Assert
        Assert.True(byOwnOrganization);
        Assert.False(byOtherOrganization);
    }

    [Fact]
    public void IsCoveredBy_AScopeNamingTheUser_CoversThatUserAndNobodyElse()
    {
        // Arrange
        var target = AdministrativeTarget.User(Person, organization: null);

        // Act
        var byOwnScope = target.IsCoveredBy(AssignmentScope.User(Person));
        var byColleaguesScope = target.IsCoveredBy(AssignmentScope.User(Colleague));

        // Assert
        Assert.True(byOwnScope);
        Assert.False(byColleaguesScope);
    }

    [Fact]
    public void IsCoveredBy_AnOrganization_CoversNoTargetInNone()
    {
        // Arrange
        var target = AdministrativeTarget.MailAccount(organization: null, [Person]);

        // Act
        var covered = target.IsCoveredBy(AssignmentScope.Organization(Organization));

        // Assert
        Assert.False(covered);
    }

    [Fact]
    public void IsCoveredBy_TheUserOfAnAccountAssignedToThemAlone_CoversTheAccount()
    {
        // Arrange
        var target = AdministrativeTarget.MailAccount(Organization, [Person]);

        // Act
        var covered = target.IsCoveredBy(AssignmentScope.User(Person));

        // Assert
        Assert.True(covered);
    }

    /// <summary>A mailbox two people read is not one of them's to administer, whichever of them the scope names.</summary>
    [Fact]
    public void IsCoveredBy_AUserOfAnAccountAssignedToTwo_DoesNotCoverTheAccount()
    {
        // Arrange
        var target = AdministrativeTarget.MailAccount(Organization, [Person, Colleague]);

        // Act
        var covered = target.IsCoveredBy(AssignmentScope.User(Person));

        // Assert
        Assert.False(covered);
        Assert.True(target.IsCoveredBy(AssignmentScope.Organization(Organization)));
    }

    /// <summary>A mail account in no organization is the deployment's alone, even to the one user it is assigned to.</summary>
    [Fact]
    public void IsCoveredBy_TheUserOfAnAccountInNoOrganizationAssignedToThemAlone_DoesNotCoverTheAccount()
    {
        // Arrange
        var target = AdministrativeTarget.MailAccount(organization: null, [Person]);

        // Act
        var covered = target.IsCoveredBy(AssignmentScope.User(Person));

        // Assert
        Assert.False(covered);
        Assert.True(target.IsCoveredBy(AssignmentScope.Deployment));
    }

    [Fact]
    public void IsCoveredBy_AUserScope_DoesNotCoverAnAccountAssignedToNobody()
    {
        // Arrange
        var target = AdministrativeTarget.MailAccount(Organization, []);

        // Act
        var covered = target.IsCoveredBy(AssignmentScope.User(Person));

        // Assert
        Assert.False(covered);
    }

    [Fact]
    public void IsCoveredBy_TheDeploymentAndTheOrganizationItself_CoverAnOrganizationAndNoOtherOrganizationDoes()
    {
        // Arrange
        var target = AdministrativeTarget.OrganizationItself(Organization);

        // Act
        var byTheDeployment = target.IsCoveredBy(AssignmentScope.Deployment);
        var byItself = target.IsCoveredBy(AssignmentScope.Organization(Organization));
        var byOtherOrganization = target.IsCoveredBy(AssignmentScope.Organization(OtherOrganization));

        // Assert
        Assert.True(byTheDeployment);
        Assert.True(byItself);
        Assert.False(byOtherOrganization);
    }

    /// <summary>An organization is nobody's alone, so a scope naming one of its members does not reach it.</summary>
    [Fact]
    public void IsCoveredBy_AUserScope_DoesNotCoverAnOrganizationItself()
    {
        // Arrange
        var target = AdministrativeTarget.OrganizationItself(Organization);

        // Act
        var covered = target.IsCoveredBy(AssignmentScope.User(Person));

        // Assert
        Assert.False(covered);
    }

    [Fact]
    public void OrganizationItself_AnIdentifierNamingNoOrganization_IsRefused()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => AdministrativeTarget.OrganizationItself(Guid.Empty));
    }

    [Fact]
    public void Covers_APermissionHeldAtTheTargetsOrganization_CoversItAndNotAnotherPermission()
    {
        // Arrange
        var grant = ScopedGrant.Of([(MailFathomPermission.AdminAuditRead, AssignmentScope.Organization(Organization))]);
        var target = AdministrativeTarget.MailAccount(Organization, [Person, Colleague]);

        // Act
        var auditRead = grant.Covers(MailFathomPermission.AdminAuditRead, target);
        var operate = grant.Covers(MailFathomPermission.AdminOperate, target);

        // Assert
        Assert.True(auditRead);
        Assert.False(operate);
    }

    [Fact]
    public void Covers_APermissionHeldOnlyOverAnotherOrganization_DoesNotCoverTheTarget()
    {
        // Arrange
        var grant = ScopedGrant.Of([(MailFathomPermission.AdminAuditRead, AssignmentScope.Organization(OtherOrganization))]);

        // Act
        var covered = grant.Covers(MailFathomPermission.AdminAuditRead, AdministrativeTarget.User(Person, Organization));

        // Assert
        Assert.False(covered);
    }
}
