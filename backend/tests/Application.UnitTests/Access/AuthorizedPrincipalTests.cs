// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.Access;

/// <summary>Covers what the application layer learns about whoever a unit of work is running for.</summary>
public sealed class AuthorizedPrincipalTests
{
    [Fact]
    public void Caller_AGrantAnEntryResolvedTo_IsHeldUnderTheConfiguredIdentity()
    {
        // Arrange & Act
        var caller = AuthorizedPrincipal.Caller("admin-key", [MailFathomPermission.AdminRead]);

        // Assert
        Assert.Equal(AuthorizedPrincipalKind.Caller, caller.Kind);
        Assert.Equal("admin-key", caller.Identity);
        Assert.True(caller.Holds(MailFathomPermission.AdminRead));
        Assert.False(caller.Holds(MailFathomPermission.AdminSpend));
    }

    /// <summary>The struct default names no capability, so carrying one would mean holding nothing under a name that reads like something.</summary>
    [Fact]
    public void Caller_AnUnspecifiedPermissionInTheGrant_IsNotCarried()
    {
        // Arrange & Act
        var caller = AuthorizedPrincipal.Caller("admin-key", [default, MailFathomPermission.AdminRead]);

        // Assert
        Assert.Equal([MailFathomPermission.AdminRead], caller.Permissions);
    }

    /// <summary>A mail permission's scope is never read: it reaches its holder's own mail whichever scope the assignment granting it named.</summary>
    [Fact]
    public void Holds_AMailPermissionHeldAtTheUsersOwnScope_IsHeld()
    {
        // Arrange
        var caller = AuthorizedPrincipal.CallerActingFor(
            SyntheticUser.Deployment,
            "user-key",
            ScopedGrant.Of([(MailFathomPermission.MailRead, AssignmentScope.User(SyntheticUser.Deployment))]));

        // Act & Assert
        Assert.True(caller.Holds(MailFathomPermission.MailRead));
    }

    /// <summary>A principal acting for no user has no mail of its own, so a mail name its grant carries reaches nothing — and is still carried, as a ceiling.</summary>
    [Fact]
    public void Holds_AMailPermissionOfACallerActingForNoUser_IsNotHeld()
    {
        // Arrange
        var caller = AuthorizedPrincipal.Caller("administrator", [MailFathomPermission.MailRead]);

        // Act & Assert
        Assert.False(caller.Holds(MailFathomPermission.MailRead));
        Assert.Contains(MailFathomPermission.MailRead, caller.Permissions);
    }

    /// <summary>A mail name reads no scope, so naming a target changes nothing about who holds it: the user's caller does, and the caller acting for nobody does not.</summary>
    [Fact]
    public void HoldsOver_AMailPermission_IsAnsweredAsHoldsAnswersItWhateverTheTarget()
    {
        // Arrange
        var actingForNobody = AuthorizedPrincipal.Caller("administrator", [MailFathomPermission.MailRead]);
        var actingForAUser = AuthorizedPrincipal.CallerActingFor(
            SyntheticUser.Deployment,
            "user-key",
            [MailFathomPermission.MailRead]);

        // Act & Assert
        Assert.False(actingForNobody.HoldsOver(MailFathomPermission.MailRead, AdministrativeTarget.Unplaced));
        Assert.True(actingForAUser.HoldsOver(MailFathomPermission.MailRead, AdministrativeTarget.Unplaced));
    }

    /// <summary>
    /// A question naming no target is the deployment's, so an administrative permission held only over one organization
    /// answers it no — and is still carried, with its scope, for a check that does name a target inside it.
    /// </summary>
    [Fact]
    public void Holds_AnAdministrativePermissionHeldOnlyOverOneOrganization_IsNotHeldForTheDeployment()
    {
        // Arrange
        var organization = AssignmentScope.Organization(new Guid("0198f0aa-0000-7000-8000-0000000000d1"));
        var caller = AuthorizedPrincipal.Caller(
            "organization-administrator",
            ScopedGrant.Of([(MailFathomPermission.AdminRead, organization)]));

        // Act & Assert
        Assert.False(caller.Holds(MailFathomPermission.AdminRead));
        Assert.Equal([organization], caller.Grant.ScopesOf(MailFathomPermission.AdminRead));
    }

    [Fact]
    public void Holds_AnAdministrativePermissionHeldOverTheDeployment_IsHeld()
    {
        // Arrange
        var caller = AuthorizedPrincipal.Caller(
            "administrator",
            ScopedGrant.Of([
                (MailFathomPermission.AdminRead, AssignmentScope.Organization(new Guid("0198f0aa-0000-7000-8000-0000000000d2"))),
                (MailFathomPermission.AdminRead, AssignmentScope.Deployment),
            ]));

        // Act & Assert
        Assert.True(caller.Holds(MailFathomPermission.AdminRead));
    }

    /// <summary>What tells a refusal of an operation that is the deployment's alone apart from a refusal for want of the grant.</summary>
    [Fact]
    public void HoldsOnlyBelowDeployment_AnAdministrativePermissionHeldOnlyOverOneOrganization_IsHeldOnlyBelow()
    {
        // Arrange
        var caller = AuthorizedPrincipal.Caller(
            "organization-administrator",
            ScopedGrant.Of([
                (MailFathomPermission.AdminRead, AssignmentScope.Organization(new Guid("0198f0aa-0000-7000-8000-0000000000d3"))),
            ]));

        // Act & Assert
        Assert.True(caller.HoldsOnlyBelowDeployment(MailFathomPermission.AdminRead));
    }

    /// <summary>Held over the deployment as well, or not held at all, is not held only below it — the refusal then is for want of the grant.</summary>
    [Fact]
    public void HoldsOnlyBelowDeployment_HeldOverTheDeploymentOrNotAtAll_IsNotHeldOnlyBelow()
    {
        // Arrange
        var caller = AuthorizedPrincipal.Caller(
            "administrator",
            ScopedGrant.Of([
                (MailFathomPermission.AdminRead, AssignmentScope.Organization(new Guid("0198f0aa-0000-7000-8000-0000000000d4"))),
                (MailFathomPermission.AdminRead, AssignmentScope.Deployment),
            ]));

        // Act & Assert
        Assert.False(caller.HoldsOnlyBelowDeployment(MailFathomPermission.AdminRead));
        Assert.False(caller.HoldsOnlyBelowDeployment(MailFathomPermission.AdminOperate));
    }

    /// <summary>A mail permission's scope is never read, so it is never held only below the deployment.</summary>
    [Fact]
    public void HoldsOnlyBelowDeployment_AMailPermissionHeldAtTheUsersOwnScope_IsNotHeldOnlyBelow()
    {
        // Arrange
        var caller = AuthorizedPrincipal.CallerActingFor(
            SyntheticUser.Deployment,
            "user-key",
            ScopedGrant.Of([(MailFathomPermission.MailRead, AssignmentScope.User(SyntheticUser.Deployment))]));

        // Act & Assert
        Assert.False(caller.HoldsOnlyBelowDeployment(MailFathomPermission.MailRead));
    }

    /// <summary>A refusal has to name something an operator can act on, so an entry with no name is a defect rather than an anonymous caller.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Caller_AnIdentityThatNamesNothing_IsRejected(string identity)
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentException>(() => AuthorizedPrincipal.Caller(identity, []));
    }

    /// <summary>
    /// The process identity is a kind rather than a caller holding everything. Nothing composes a grant onto it, so no
    /// permission check can ever be what admits it.
    /// </summary>
    [Fact]
    public void Process_TheIdentityWorkNoCallerRequestedRunsUnder_HoldsNothing()
    {
        // Arrange & Act
        var process = AuthorizedPrincipal.Process;

        // Assert
        Assert.Equal(AuthorizedPrincipalKind.ProcessIdentity, process.Kind);
        Assert.Equal(AuthorizedPrincipal.ProcessIdentityName, process.Identity);
        Assert.Empty(process.Permissions);
        Assert.All(MailFathomPermission.All, permission => Assert.False(process.Holds(permission)));
    }

    /// <summary>A capability is the authorization in full, bounded to the object it names, so it carries no grant over a surface either.</summary>
    [Fact]
    public void SignedCapability_AVerifiedTicket_NamesItsObjectAndHoldsNothing()
    {
        // Arrange & Act
        var capability = AuthorizedPrincipal.SignedCapability(SyntheticUser.Deployment, "/mcp/attachments/an-object/0");

        // Assert
        Assert.Equal(AuthorizedPrincipalKind.SignedCapability, capability.Kind);
        Assert.Equal("/mcp/attachments/an-object/0", capability.Identity);
        Assert.Empty(capability.Permissions);
    }

    /// <summary>
    /// Both factories that carry a user refuse one that names nobody, and the guard is what stops the struct default
    /// from being minted into a principal: a use case reading such a principal's user would scope a mail query to a
    /// user no row belongs to, which is a query that answers rather than one that refuses.
    /// </summary>
    [Fact]
    public void EveryFactoryCarryingAUser_AUserThatNamesNobody_IsRejected()
    {
        // Arrange
        Action[] factories =
        [
            () => AuthorizedPrincipal.CallerActingFor(default, "mcp-key", [MailFathomPermission.MailRead]),
            () => AuthorizedPrincipal.SignedCapability(default, "/mcp/attachments/an-object/0"),
        ];

        // Act
        Exception?[] refusals = [.. factories.Select(Record.Exception)];

        // Assert
        Assert.All(refusals, refusal => Assert.IsType<ArgumentException>(refusal));
    }
}
