// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
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
    /// An ordinary caller acts for the deployment's owner, and asserting it here is what the owner-scoped suites are
    /// entitled to assume. A helper that stopped stating an owner would turn every "a caller reads their own accounts"
    /// test into a refusal that still passed for the wrong reason.
    /// </summary>
    [Fact]
    public void ForCallerGranted_ActsForTheDeploymentsOwner()
    {
        // Act
        var authorization = AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead);

        // Assert
        Assert.Equal(SyntheticMailOwner.Deployment, authorization.RequireOwner());
    }

    /// <summary>The owner a test names is the owner the use case is told about, which is what an isolation test asserts against.</summary>
    [Fact]
    public void ForOwnerGranted_TheOwnerNamed_IsTheOwnerTheWorkActsFor()
    {
        // Act
        var authorization = AccessAuthorizations.ForOwnerGranted(
            SyntheticMailOwner.Another,
            MailFathomPermission.MailRead);

        // Assert
        Assert.Equal(SyntheticMailOwner.Another, authorization.RequireOwner());
    }

    /// <summary>
    /// The deployment administrator acts for nobody however broad the grant, and that is the whole of what separates it
    /// from a caller here. A helper routed through the owner-carrying factory would make every "the administrative
    /// surface cannot read a mailbox" test pass by reading one.
    /// </summary>
    [Fact]
    public void ForAdministratorGranted_HoldsTheGrantAndActsForNoOwner()
    {
        // Act
        var authorization = AccessAuthorizations.ForAdministratorGranted(MailFathomPermission.AdminCredentialsWrite);

        // Assert
        Assert.True(authorization.Permits(MailFathomPermission.AdminCredentialsWrite));
        Assert.Throws<PrincipalNotAuthorizedException>(() => authorization.RequireOwner());
    }
}
