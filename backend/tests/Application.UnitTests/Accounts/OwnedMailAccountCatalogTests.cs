// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Synchronization;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Accounts;

/// <summary>Covers the one place the user axis enters a mailbox read.</summary>
/// <remarks>
/// Everything a caller may reach is composed from this answer, so the outcomes worth stating are all here: a user owns
/// the accounts served under their own name, another user owns none of them, a deployment serving two users answers
/// each with their own half rather than refusing both, and a principal acting for no user is refused rather than
/// answered with an empty set.
/// </remarks>
public sealed class OwnedMailAccountCatalogTests
{
    private static readonly ServedMailAccount ServedAccount = new(
        SyntheticMailUser.Deployment,
        MailAccountId.Create("personal"),
        MailAccountDisplayName.Create("Personal mail"),
        MailSynchronizationMode.Polling);

    private static readonly ServedMailAccount AnotherUsersAccount = new(
        SyntheticMailUser.Another,
        MailAccountId.Create("work"),
        MailAccountDisplayName.Create("Work mail"),
        MailSynchronizationMode.Polling);

    [Fact]
    public void OwnedAccounts_TheUserTheDeploymentServes_OwnsEveryAccountItServes()
    {
        // Arrange
        var catalog = CatalogFor(AccessAuthorizations.ForUserGranted(SyntheticMailUser.Deployment));

        // Act
        var owned = catalog.OwnedAccounts;

        // Assert
        Assert.Equal([ServedAccount], owned);
    }

    /// <summary>
    /// The refusal a caller sees for another user's account has to be the one they see for an account nobody
    /// configured, which is what an empty catalog produces: resolution then narrows the scope rather than reporting
    /// that the account exists and belongs to somebody else.
    /// </summary>
    [Fact]
    public void OwnedAccounts_AnotherUser_OwnsNothingThisDeploymentServes()
    {
        // Arrange
        var catalog = CatalogFor(AccessAuthorizations.ForUserGranted(SyntheticMailUser.Another));

        // Act
        var owned = catalog.OwnedAccounts;

        // Assert
        Assert.Empty(owned);
    }

    /// <summary>
    /// The deployment administrator and this process's own identity act for nobody. Answering either with an empty set
    /// would publish a caller-facing read to them in the shape of an answer, so the port refuses instead.
    /// </summary>
    [Theory]
    [MemberData(nameof(PrincipalsActingForNoUser))]
    public void OwnedAccounts_APrincipalActingForNoUser_IsRefused(AuthorizedPrincipal principal)
    {
        // Arrange
        var catalog = CatalogFor(AccessAuthorizations.ForPrincipal(principal));

        // Act
        var refusal = Record.Exception(() => catalog.OwnedAccounts);

        // Assert
        Assert.IsType<PrincipalNotAuthorizedException>(refusal);
    }

    /// <summary>An entrypoint that stated no principal at all is refused by the same requirement rather than answered.</summary>
    [Fact]
    public void OwnedAccounts_ReachedUnderNoPrincipal_IsRefused()
    {
        // Arrange
        var catalog = CatalogFor(AccessAuthorizations.ForPrincipal(principal: null));

        // Act
        var refusal = Record.Exception(() => catalog.OwnedAccounts);

        // Assert
        Assert.IsType<PrincipalNotAuthorizedException>(refusal);
    }

    /// <summary>
    /// The deployment this change exists to enable serves several users, and every user-facing read runs through here.
    /// Each of them is answered with the accounts served under their own name rather than with a refusal that no sole
    /// user could be named, which is what asking the deployment for one would have produced.
    /// </summary>
    [Fact]
    public void OwnedAccounts_ADeploymentServingTwoUsers_AnswersEachWithTheirOwnAccounts()
    {
        // Arrange
        var deploymentUser = CatalogFor(
            AccessAuthorizations.ForUserGranted(SyntheticMailUser.Deployment),
            servedAccounts: [ServedAccount, AnotherUsersAccount]);

        var anotherUser = CatalogFor(
            AccessAuthorizations.ForUserGranted(SyntheticMailUser.Another),
            servedAccounts: [ServedAccount, AnotherUsersAccount]);

        // Act
        var deploymentUsersAccounts = deploymentUser.OwnedAccounts;
        var anotherUsersAccounts = anotherUser.OwnedAccounts;

        // Assert
        Assert.Equal([ServedAccount], deploymentUsersAccounts);
        Assert.Equal([AnotherUsersAccount], anotherUsersAccounts);
    }

    /// <summary>Whether synchronization runs is a deployment fact rather than a caller's, so it is reported unchanged.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SynchronizationEnabled_WhateverTheDeploymentDecided_IsReportedUnchanged(bool synchronizationEnabled)
    {
        // Arrange
        var catalog = CatalogFor(
            AccessAuthorizations.ForUserGranted(SyntheticMailUser.Deployment),
            synchronizationEnabled);

        // Act & Assert
        Assert.Equal(synchronizationEnabled, catalog.SynchronizationEnabled);
    }

    public static TheoryData<AuthorizedPrincipal> PrincipalsActingForNoUser() =>
    [
        AuthorizedPrincipal.Caller("deployment-administrator", [MailFathomPermission.AdminRead]),
        AuthorizedPrincipal.Process,
    ];

    private static OwnedMailAccountCatalog CatalogFor(
        AccessAuthorization authorization,
        bool synchronizationEnabled = true,
        IReadOnlyList<ServedMailAccount>? servedAccounts = null)
    {
        var deploymentAccounts = Substitute.For<IDeploymentMailAccountCatalog>();
        deploymentAccounts.ServedAccounts.Returns(servedAccounts ?? [ServedAccount]);
        deploymentAccounts.SynchronizationEnabled.Returns(synchronizationEnabled);

        return new OwnedMailAccountCatalog(deploymentAccounts, authorization);
    }
}
