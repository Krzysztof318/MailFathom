// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the resolution every suite arranges a contact book's user through.</summary>
/// <remarks>
/// A fault here would point every book test at one user whatever principal it arranged, so a suite asserting that one
/// person's book is not another's would pass without the scoping ever being exercised.
/// </remarks>
public sealed class ContactBookOwnershipsTests
{
    [Fact]
    public void For_ACallerActingForAnUser_ResolvesToThatUser()
    {
        // Arrange
        var authorization = AccessAuthorizations.ForUserGranted(
            SyntheticMailUser.Another,
            MailFathomPermission.MailContactsRead);

        // Act
        var ownership = ContactBookOwnerships.For(authorization);

        // Assert
        Assert.Equal(SyntheticMailUser.Another, ownership.User);
    }

    /// <summary>The overload every suite whose subject is something else reaches for, and the one it never states a user to.</summary>
    /// <remarks>
    /// A book arranged under one user and a caller acting for another would leave those suites consistently mismatched
    /// rather than failing, so the two halves of that default are asserted to be the same user here.
    /// </remarks>
    [Fact]
    public void ForTheServedUser_TheOrdinaryCaller_ResolvesToTheUserEveryBookIsArrangedUnder()
    {
        // Act
        var ownership = ContactBookOwnerships.ForTheServedUser();

        // Assert
        Assert.Equal(SyntheticMailUser.Deployment, ownership.User);
    }

    [Fact]
    public void For_TheDeploymentAdministrator_ResolvesToTheUserTheDeploymentServes()
    {
        // Arrange
        var authorization = AccessAuthorizations.ForAdministratorGranted(MailFathomPermission.AdminAuditRead);

        // Act
        var ownership = ContactBookOwnerships.For(authorization, SyntheticMailUser.Another);

        // Assert
        Assert.Equal(SyntheticMailUser.Another, ownership.User);
    }

    [Fact]
    public void For_TheProcessIdentity_ResolvesToTheUserTheDeploymentServes()
    {
        // Arrange
        var authorization = AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Process);

        // Act
        var ownership = ContactBookOwnerships.For(authorization);

        // Assert
        Assert.Equal(SyntheticMailUser.Deployment, ownership.User);
    }

    [Fact]
    public void For_WorkReachedUnderNoPrincipal_RefusesRatherThanNamingTheDeploymentsUser()
    {
        // Arrange
        var ownership = ContactBookOwnerships.For(AccessAuthorizations.ForPrincipal(principal: null));

        // Act
        var refusal = Record.Exception(() => ownership.User);

        // Assert
        Assert.IsType<PrincipalNotAuthorizedException>(refusal);
    }
}
