// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.Access;

/// <summary>Covers the question a use case asks once it has read the record it acts on, which reads nothing.</summary>
public sealed class AccessAuthorizationScopeTests
{
    private static readonly Guid Organization = new("0198f0aa-0000-7000-8000-0000000000d1");

    private static readonly Guid OtherOrganization = new("0198f0aa-0000-7000-8000-0000000000d2");

    [Fact]
    public void PermitsOver_ATargetInsideAndOneOutsideTheCallersOrganization_ReachesOnlyTheOneInside()
    {
        // Arrange
        var authorization = OrganizationAdministrator(MailFathomPermission.AdminRead);

        // Act
        var inside = authorization.PermitsOver(MailFathomPermission.AdminRead, AdministrativeTarget.MailAccount(Organization, []));
        var outside = authorization.PermitsOver(MailFathomPermission.AdminRead, AdministrativeTarget.MailAccount(OtherOrganization, []));
        var unplaced = authorization.PermitsOver(MailFathomPermission.AdminRead, AdministrativeTarget.Unplaced);

        // Assert
        Assert.True(inside);
        Assert.False(outside);
        Assert.False(unplaced);
    }

    /// <summary>A permission held at one scope says nothing about another name, so a grant over the organization for reading does not reach an erasure there.</summary>
    [Fact]
    public void PermitsOver_APermissionTheCallerHoldsNowhere_ReachesNothingEvenInsideTheirOrganization()
    {
        // Arrange
        var authorization = OrganizationAdministrator(MailFathomPermission.AdminRead);

        // Act
        var reached = authorization.PermitsOver(MailFathomPermission.AdminErase, AdministrativeTarget.MailAccount(Organization, []));

        // Assert
        Assert.False(reached);
    }

    [Fact]
    public void PermitsOver_TheProcessIdentity_ReachesNothing()
    {
        // Arrange
        var authorization = AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Process);

        // Act
        var reached = authorization.PermitsOver(MailFathomPermission.AdminRead, AdministrativeTarget.Unplaced);

        // Assert
        Assert.False(reached);
    }

    private static AccessAuthorization OrganizationAdministrator(MailFathomPermission permission) =>
        AccessAuthorizations.ForAdministratorScoped(
            ScopedGrant.Of([(permission, AssignmentScope.Organization(Organization))]),
            new StatedAdministrativeTargets());
}
