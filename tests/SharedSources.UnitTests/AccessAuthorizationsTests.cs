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
}
