// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
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
    public void For_ACallerActingForAUser_ResolvesToThatUser()
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

    /// <summary>The accounts a user is assigned decide which collected books they read, and in which order.</summary>
    /// <remarks>
    /// The order is what the precedence between two books rests on, so a helper that handed the accounts over in the
    /// order a test happened to name them would leave every suite asserting a precedence its own arrangement decided.
    /// </remarks>
    [Fact]
    public void For_AUserAssignedTwoAccounts_ReadsTheirOwnBookFirstAndTheAccountsInTheDocumentedOrder()
    {
        // Arrange
        var authorization = AccessAuthorizations.ForUserGranted(
            SyntheticMailUser.Another,
            MailFathomPermission.MailContactsRead);

        // Act
        var ownership = ContactBookOwnerships.For(
            authorization,
            MailAccountId.Create("second-account"),
            MailAccountId.Create("first-account"));

        // Assert
        Assert.Equal(
            [
                SyntheticMailUser.Another.Value.ToString("D"),
                "first-account",
                "second-account",
            ],
            ownership.Scope.Keys);
    }

    /// <summary>The two principals that reach the books by naming one, which is why neither may be attributed to a user.</summary>
    /// <remarks>
    /// Both once resolved to the single user a deployment served, and a deployment now holds several. What replaced
    /// that is a refusal rather than a choice: the administrative surface names the user or the account whose book it
    /// reaches, and collection names the account it is synchronizing, so a principal arriving here acting for nobody
    /// is a caller that lost its user rather than one to attribute.
    /// </remarks>
    [Theory]
    [MemberData(nameof(PrincipalsActingForNobody))]
    public void For_APrincipalActingForNoUser_RefusesRatherThanNamingOne(AuthorizedPrincipal principal)
    {
        // Arrange
        var ownership = ContactBookOwnerships.For(AccessAuthorizations.ForPrincipal(principal));

        // Act
        var refusal = Record.Exception(() => ownership.User);

        // Assert
        Assert.IsType<PrincipalNotAuthorizedException>(refusal);
    }

    /// <summary>The principals the theory above states, each of which reaches a use case acting for no user.</summary>
    public static TheoryData<AuthorizedPrincipal> PrincipalsActingForNobody() =>
    [
        AuthorizedPrincipal.Caller("test-administrator", [MailFathomPermission.AdminAuditRead]),
        AuthorizedPrincipal.Process,
    ];

    /// <summary>An entrypoint that stated no principal at all, which is the case the refusal above was written for.</summary>
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
