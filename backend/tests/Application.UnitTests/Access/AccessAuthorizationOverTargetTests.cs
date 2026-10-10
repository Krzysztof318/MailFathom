// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Access;

/// <summary>Covers how an administrative permission held at a scope reaches the user or the mail account an operation names.</summary>
/// <remarks>
/// The placement port is substituted, because where a target sits is a fact the records hold and the question here is
/// only what a scope does with it: the deployment reaches everything without asking, an organization reaches what
/// belongs to it, and a user scope reaches that user and a mailbox assigned to them alone.
/// </remarks>
public sealed class AccessAuthorizationOverTargetTests
{
    private static readonly Guid Organization = new("0198f0aa-0000-7000-8000-0000000000e1");

    private static readonly Guid OtherOrganization = new("0198f0aa-0000-7000-8000-0000000000e2");

    private static readonly UserId Person = UserId.Create(new Guid("0198f0aa-0000-7000-8000-0000000000e3"));

    private static readonly UserId Colleague = UserId.Create(new Guid("0198f0aa-0000-7000-8000-0000000000e4"));

    private static readonly MailAccountId Account = MailAccountId.Create("0198f0aa-0000-7000-8000-0000000000e5");

    private readonly IAdministrativeTargets targets = Substitute.For<IAdministrativeTargets>();

    [Fact]
    public async Task RequirePermissionOverAsync_AnOrganizationsAdministratorOverAnAccountInIt_Permits()
    {
        // Arrange
        this.PlaceAccount(AdministrativeTarget.MailAccount(Organization, [Person, Colleague]));
        var authorization = this.AuthorizationFor(AssignmentScope.Organization(Organization));

        // Act
        var admitted = await Record.ExceptionAsync(() => authorization.RequirePermissionOverAsync(
            MailFathomPermission.AdminAuditRead,
            Account,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Null(admitted);
    }

    [Fact]
    public async Task RequirePermissionOverAsync_AnOrganizationsAdministratorOverAnotherOrganizationsAccount_RefusesNamingThePermission()
    {
        // Arrange
        this.PlaceAccount(AdministrativeTarget.MailAccount(OtherOrganization, [Person]));
        var authorization = this.AuthorizationFor(AssignmentScope.Organization(Organization));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() => authorization.RequirePermissionOverAsync(
            MailFathomPermission.AdminAuditRead,
            Account,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminAuditRead, refusal.RequiredPermission);
    }

    [Fact]
    public async Task RequirePermissionOverAsync_AUserScopeOverTheirOwnRecord_PermitsAndRefusesAColleagues()
    {
        // Arrange
        this.targets.PlaceUserAsync(Person, Arg.Any<CancellationToken>())
            .Returns(AdministrativeTarget.User(Person, Organization));
        this.targets.PlaceUserAsync(Colleague, Arg.Any<CancellationToken>())
            .Returns(AdministrativeTarget.User(Colleague, Organization));
        var authorization = this.AuthorizationFor(AssignmentScope.User(Person));

        // Act
        var ownRecord = await Record.ExceptionAsync(() => authorization.RequirePermissionOverAsync(
            MailFathomPermission.AdminAuditRead,
            Person,
            TestContext.Current.CancellationToken));
        var colleaguesRecord = await Record.ExceptionAsync(() => authorization.RequirePermissionOverAsync(
            MailFathomPermission.AdminAuditRead,
            Colleague,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Null(ownRecord);
        Assert.IsType<PrincipalNotAuthorizedException>(colleaguesRecord);
    }

    [Fact]
    public async Task PermitsOverAsync_AUserScopeOverAnAccount_ReachesItOnlyWhileTheUserHoldsItAlone()
    {
        // Arrange
        var authorization = this.AuthorizationFor(AssignmentScope.User(Person));
        this.PlaceAccount(AdministrativeTarget.MailAccount(Organization, [Person]));
        var alone = await authorization.PermitsOverAsync(
            MailFathomPermission.AdminAuditRead,
            Account,
            TestContext.Current.CancellationToken);
        this.PlaceAccount(AdministrativeTarget.MailAccount(Organization, [Person, Colleague]));

        // Act
        var shared = await authorization.PermitsOverAsync(
            MailFathomPermission.AdminAuditRead,
            Account,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(alone);
        Assert.False(shared);
    }

    /// <summary>An operator's grant costs no read, and reaches an account the records no longer hold exactly as it did before a grant had a scope.</summary>
    [Fact]
    public async Task RequirePermissionOverAsync_TheDeploymentsAdministrator_IsAdmittedWithoutPlacingTheTarget()
    {
        // Arrange
        var authorization = this.AuthorizationFor(AssignmentScope.Deployment);

        // Act
        await authorization.RequirePermissionOverAsync(
            MailFathomPermission.AdminAuditRead,
            Account,
            TestContext.Current.CancellationToken);

        // Assert
        await this.targets.DidNotReceive().PlaceMailAccountsAsync(Arg.Any<IReadOnlyCollection<MailAccountId>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PermitsOverAsync_ACallerHoldingThePermissionNowhere_ReportsNothingWithoutPlacingTheTarget()
    {
        // Arrange
        var authorization = new AccessAuthorization(
            StatedPrincipal(AuthorizedPrincipal.Caller(
                "administrator",
                ScopedGrant.Of([(MailFathomPermission.AdminRead, AssignmentScope.Organization(Organization))]))),
            this.targets);

        // Act
        var permitted = await authorization.PermitsOverAsync(
            MailFathomPermission.AdminAuditRead,
            Account,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(permitted);
        await this.targets.DidNotReceive().PlaceMailAccountsAsync(Arg.Any<IReadOnlyCollection<MailAccountId>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequirePermissionOverAsync_TheProcessIdentity_RefusesWhateverItsTarget()
    {
        // Arrange
        var authorization = new AccessAuthorization(StatedPrincipal(AuthorizedPrincipal.Process), this.targets);

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() => authorization.RequirePermissionOverAsync(
            MailFathomPermission.AdminAuditRead,
            Person,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.False(refusal.RequiredPermission.IsSpecified);
    }

    /// <summary>The transport cannot place a target, so for a route naming one it refuses only a caller holding the permission nowhere.</summary>
    [Fact]
    public void PermitsAtAnyScope_APermissionHeldOverOneOrganization_IsHeldThereAndNotOverTheDeployment()
    {
        // Arrange
        var authorization = this.AuthorizationFor(AssignmentScope.Organization(Organization));

        // Act
        var atAnyScope = authorization.PermitsAtAnyScope(MailFathomPermission.AdminAuditRead);
        var overTheDeployment = authorization.Permits(MailFathomPermission.AdminAuditRead);

        // Assert
        Assert.True(atAnyScope);
        Assert.False(overTheDeployment);
    }

    [Fact]
    public void RequirePermissionAtAnyScope_ACallerHoldingItNowhere_RefusesNamingIt()
    {
        // Arrange
        var authorization = this.AuthorizationFor(AssignmentScope.Organization(Organization));

        // Act
        var refusal = Assert.Throws<PrincipalNotAuthorizedException>(() =>
            authorization.RequirePermissionAtAnyScope(MailFathomPermission.AdminOperate));

        // Assert
        Assert.Equal(MailFathomPermission.AdminOperate, refusal.RequiredPermission);
    }

    /// <summary>Without a placement source every target is the deployment's alone, so a narrower grant reaches nothing rather than everything.</summary>
    [Fact]
    public async Task RequirePermissionOverAsync_AnAuthorizationPlacingNoTarget_RefusesAScopedAdministrator()
    {
        // Arrange
        var authorization = new AccessAuthorization(StatedPrincipal(ScopedAdministrator(AssignmentScope.User(Person))));

        // Act
        var refusal = await Record.ExceptionAsync(() => authorization.RequirePermissionOverAsync(
            MailFathomPermission.AdminAuditRead,
            Person,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<PrincipalNotAuthorizedException>(refusal);
    }

    /// <summary>Several accounts are placed in one read, and only the ones the scope covers are answered.</summary>
    [Fact]
    public async Task CoveredMailAccountsAsync_AnOrganizationsAdministrator_KeepsTheAccountsInItFromOneRead()
    {
        // Arrange
        var elsewhere = MailAccountId.Create("0198f0aa-0000-7000-8000-0000000000e6");
        this.targets.PlaceMailAccountsAsync(Arg.Any<IReadOnlyCollection<MailAccountId>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<MailAccountId, AdministrativeTarget>
            {
                [Account] = AdministrativeTarget.MailAccount(Organization, [Person]),
                [elsewhere] = AdministrativeTarget.MailAccount(OtherOrganization, [Person]),
            });
        var authorization = this.AuthorizationFor(AssignmentScope.Organization(Organization));

        // Act
        var covered = await authorization.CoveredMailAccountsAsync(
            MailFathomPermission.AdminAuditRead,
            [Account, elsewhere],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([Account], covered);
        await this.targets.Received(1).PlaceMailAccountsAsync(Arg.Any<IReadOnlyCollection<MailAccountId>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CoveredMailAccountsAsync_TheDeploymentsAdministrator_KeepsEveryAccountWithoutPlacingAny()
    {
        // Arrange
        var authorization = this.AuthorizationFor(AssignmentScope.Deployment);

        // Act
        var covered = await authorization.CoveredMailAccountsAsync(
            MailFathomPermission.AdminAuditRead,
            [Account],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([Account], covered);
        await this.targets.DidNotReceive().PlaceMailAccountsAsync(Arg.Any<IReadOnlyCollection<MailAccountId>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CoveredMailAccountsAsync_ACallerHoldingThePermissionNowhere_KeepsNoneWithoutPlacingAny()
    {
        // Arrange
        var authorization = new AccessAuthorization(
            StatedPrincipal(AuthorizedPrincipal.Caller(
                "administrator",
                ScopedGrant.Of([(MailFathomPermission.AdminRead, AssignmentScope.Deployment)]))),
            this.targets);

        // Act
        var covered = await authorization.CoveredMailAccountsAsync(
            MailFathomPermission.AdminAuditRead,
            [Account],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(covered);
        await this.targets.DidNotReceive().PlaceMailAccountsAsync(Arg.Any<IReadOnlyCollection<MailAccountId>>(), Arg.Any<CancellationToken>());
    }

    private static AuthorizedPrincipal ScopedAdministrator(AssignmentScope scope) =>
        AuthorizedPrincipal.Caller("administrator", ScopedGrant.Of([(MailFathomPermission.AdminAuditRead, scope)]));

    private static IAuthorizedPrincipalSource StatedPrincipal(AuthorizedPrincipal principal)
    {
        var principals = Substitute.For<IAuthorizedPrincipalSource>();
        principals.Current.Returns(principal);

        return principals;
    }

    private AccessAuthorization AuthorizationFor(AssignmentScope scope) =>
        new(StatedPrincipal(ScopedAdministrator(scope)), this.targets);

    private void PlaceAccount(AdministrativeTarget placement) =>
        this.targets.PlaceMailAccountsAsync(Arg.Any<IReadOnlyCollection<MailAccountId>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<MailAccountId, AdministrativeTarget> { [Account] = placement });
}
