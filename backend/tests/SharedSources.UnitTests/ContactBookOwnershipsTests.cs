// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the resolution every suite arranges a contact book's owner through.</summary>
/// <remarks>
/// A fault here would point every book test at one owner whatever principal it arranged, so a suite asserting that one
/// person's book is not another's would pass without the scoping ever being exercised.
/// </remarks>
public sealed class ContactBookOwnershipsTests
{
    [Fact]
    public void For_ACallerActingForAnOwner_ResolvesToThatOwner()
    {
        // Arrange
        var authorization = AccessAuthorizations.ForOwnerGranted(
            SyntheticMailOwner.Another,
            MailFathomPermission.MailContactsRead);

        // Act
        var ownership = ContactBookOwnerships.For(authorization);

        // Assert
        Assert.Equal(SyntheticMailOwner.Another, ownership.Owner);
    }

    /// <summary>The overload every suite whose subject is something else reaches for, and the one it never states an owner to.</summary>
    /// <remarks>
    /// A book arranged under one owner and a caller acting for another would leave those suites consistently mismatched
    /// rather than failing, so the two halves of that default are asserted to be the same owner here.
    /// </remarks>
    [Fact]
    public void ForTheServedOwner_TheOrdinaryCaller_ResolvesToTheOwnerEveryBookIsArrangedUnder()
    {
        // Act
        var ownership = ContactBookOwnerships.ForTheServedOwner();

        // Assert
        Assert.Equal(SyntheticMailOwner.Deployment, ownership.Owner);
    }

    [Fact]
    public void For_TheDeploymentAdministrator_ResolvesToTheOwnerTheDeploymentServes()
    {
        // Arrange
        var authorization = AccessAuthorizations.ForAdministratorGranted(MailFathomPermission.AdminAuditRead);

        // Act
        var ownership = ContactBookOwnerships.For(authorization, SyntheticMailOwner.Another);

        // Assert
        Assert.Equal(SyntheticMailOwner.Another, ownership.Owner);
    }

    [Fact]
    public void For_TheProcessIdentity_ResolvesToTheOwnerTheDeploymentServes()
    {
        // Arrange
        var authorization = AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Process);

        // Act
        var ownership = ContactBookOwnerships.For(authorization);

        // Assert
        Assert.Equal(SyntheticMailOwner.Deployment, ownership.Owner);
    }

    [Fact]
    public void For_WorkReachedUnderNoPrincipal_RefusesRatherThanNamingTheDeploymentsOwner()
    {
        // Arrange
        var ownership = ContactBookOwnerships.For(AccessAuthorizations.ForPrincipal(principal: null));

        // Act
        var refusal = Record.Exception(() => ownership.Owner);

        // Assert
        Assert.IsType<PrincipalNotAuthorizedException>(refusal);
    }
}
