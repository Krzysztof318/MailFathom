// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the authorization every suite arranges a use case's principal through.</summary>
/// <remarks>
/// A fault here would make a refusal test pass because the caller was never granted what it was meant to hold, in every
/// suite at once, so the helper is asserted where it is compiled rather than where it is used.
/// </remarks>
public sealed class AccessAuthorizationsTests
{
    [Fact]
    public void ForCallerGranted_ThePermissionsNamed_ArePermitted()
    {
        // Act
        var authorization = AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead);

        // Assert
        Assert.True(authorization.Permits(MailFathomPermission.MailRead));
    }

    [Fact]
    public void ForCallerGranted_APermissionNotNamed_IsRefused()
    {
        // Act
        var authorization = AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead);

        // Assert
        Assert.False(authorization.Permits(MailFathomPermission.MailAsk));
    }

    [Fact]
    public void ForCallerGranted_NoPermissionAtAll_PermitsNothing()
    {
        // Act
        var authorization = AccessAuthorizations.ForCallerGranted();

        // Assert
        Assert.All(
            MailFathomPermission.All,
            permission => Assert.False(authorization.Permits(permission)));
    }

    [Fact]
    public void ForPrincipal_TheProcessIdentity_IsRefusedEveryCallerPermission()
    {
        // Act
        var authorization = AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Process);

        // Assert
        Assert.False(authorization.Permits(MailFathomPermission.MailRead));
        authorization.RequireProcessIdentity();
    }

    [Fact]
    public void ForPrincipal_NoPrincipalAtAll_RefusesEveryUseCase()
    {
        // Act
        var authorization = AccessAuthorizations.ForPrincipal(principal: null);

        // Assert
        Assert.Throws<PrincipalNotAuthorizedException>(
            () => authorization.RequirePermission(MailFathomPermission.MailRead));
    }

    /// <summary>
    /// An ordinary caller acts for the deployment's user, and asserting it here is what the user-scoped suites are
    /// entitled to assume. A helper that stopped stating a user would turn every "a caller reads their own accounts"
    /// test into a refusal that still passed for the wrong reason.
    /// </summary>
    [Fact]
    public void ForCallerGranted_ActsForTheDeploymentsUser()
    {
        // Act
        var authorization = AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead);

        // Assert
        Assert.Equal(SyntheticUser.Deployment, authorization.RequireUser());
    }

    /// <summary>The user a test names is the user the use case is told about, which is what an isolation test asserts against.</summary>
    [Fact]
    public void ForUserGranted_TheUserNamed_IsTheUserTheWorkActsFor()
    {
        // Act
        var authorization = AccessAuthorizations.ForUserGranted(
            SyntheticUser.Another,
            MailFathomPermission.MailRead);

        // Assert
        Assert.Equal(SyntheticUser.Another, authorization.RequireUser());
    }

    /// <summary>
    /// The deployment administrator acts for nobody however broad the grant, and that is the whole of what separates it
    /// from a caller here. A helper routed through the user-carrying factory would make every "the administrative
    /// surface cannot read a mailbox" test pass by reading one.
    /// </summary>
    [Fact]
    public void ForAdministratorGranted_HoldsTheGrantAndActsForNoUser()
    {
        // Act
        var authorization = AccessAuthorizations.ForAdministratorGranted(MailFathomPermission.AdminCredentialsWrite);

        // Assert
        Assert.True(authorization.Permits(MailFathomPermission.AdminCredentialsWrite));
        Assert.Throws<PrincipalNotAuthorizedException>(() => authorization.RequireUser());
    }

    /// <summary>
    /// The scope lists are what every "at each scope kind" theory reads, so a covering scope that covered nothing would
    /// turn each of those refusal tests into a pass for the wrong reason.
    /// </summary>
    [Fact]
    public async Task ForAdministratorScopedAt_TheScopesListed_CoverTheHolderAndTheirAccountsExactlyAsNamed()
    {
        // Arrange
        var account = AccessAuthorizations.ScopedAccount;
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var covering = await Task.WhenAll(AccessAuthorizations.ScopesCoveringTheirTarget.Select(scope =>
            ReachesBothAsync(AccessAuthorizations.ForAdministratorScopedAt(scope, MailFathomPermission.AdminAuditRead))));
        var outside = await Task.WhenAll(AccessAuthorizations.ScopesOutsideTheirTarget.Select(scope =>
            ReachesBothAsync(AccessAuthorizations.ForAdministratorScopedAt(scope, MailFathomPermission.AdminAuditRead))));

        // Assert
        Assert.All(covering, Assert.True);
        Assert.All(outside, Assert.False);
        Assert.Equal([AssignmentScopeKind.Deployment, AssignmentScopeKind.Organization, AssignmentScopeKind.User], AccessAuthorizations.ScopesCoveringTheirTarget.Select(scope => scope.Kind));

        async Task<bool> ReachesBothAsync(AccessAuthorization authorization) =>
            await authorization.PermitsOverAsync(MailFathomPermission.AdminAuditRead, account, cancellationToken)
            && await authorization.PermitsOverAsync(MailFathomPermission.AdminAuditRead, AccessAuthorizations.ScopedHolder, cancellationToken);
    }

    /// <summary>
    /// Only the arranged account and holder are placed, so a use case that checked some other target than the one it was
    /// asked about is refused by every scope narrower than the deployment rather than served by the fake.
    /// </summary>
    [Fact]
    public async Task ForAdministratorScopedAt_AnyOtherAccountOrUser_IsCoveredByTheDeploymentAlone()
    {
        // Arrange
        var otherAccount = MailAccountId.Create("0198f0aa-0000-7000-8000-00000000f0ab");
        var otherUser = UserId.Create(new Guid("0198f0aa-0000-7000-8000-00000000f0ac"));
        var cancellationToken = TestContext.Current.CancellationToken;
        var narrower = AccessAuthorizations.ForAdministratorScopedAt(
            AssignmentScope.Organization(AccessAuthorizations.ScopedOrganization),
            MailFathomPermission.AdminAuditRead);
        var deployment = AccessAuthorizations.ForAdministratorScopedAt(AssignmentScope.Deployment, MailFathomPermission.AdminAuditRead);

        // Act
        var narrowerReachesAccount = await narrower.PermitsOverAsync(MailFathomPermission.AdminAuditRead, otherAccount, cancellationToken);
        var narrowerReachesUser = await narrower.PermitsOverAsync(MailFathomPermission.AdminAuditRead, otherUser, cancellationToken);
        var deploymentReachesAccount = await deployment.PermitsOverAsync(MailFathomPermission.AdminAuditRead, otherAccount, cancellationToken);

        // Assert
        Assert.False(narrowerReachesAccount);
        Assert.False(narrowerReachesUser);
        Assert.True(deploymentReachesAccount);
    }
}
