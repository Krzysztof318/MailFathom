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

/// <summary>Covers the one place the owner axis enters a mailbox read.</summary>
/// <remarks>
/// Everything a caller may reach is composed from this answer, so the outcomes worth stating are all here: an owner owns
/// the accounts served under their own name, another owner owns none of them, a deployment serving two owners answers
/// each with their own half rather than refusing both, and a principal acting for no owner is refused rather than
/// answered with an empty set.
/// </remarks>
public sealed class OwnedMailAccountCatalogTests
{
    private static readonly ServedMailAccount ServedAccount = new(
        SyntheticMailOwner.Deployment,
        MailAccountId.Create("personal"),
        MailAccountDisplayName.Create("Personal mail"),
        MailSynchronizationMode.Polling);

    private static readonly ServedMailAccount AnotherOwnersAccount = new(
        SyntheticMailOwner.Another,
        MailAccountId.Create("work"),
        MailAccountDisplayName.Create("Work mail"),
        MailSynchronizationMode.Polling);

    [Fact]
    public void OwnedAccounts_TheOwnerTheDeploymentServes_OwnsEveryAccountItServes()
    {
        // Arrange
        var catalog = CatalogFor(AccessAuthorizations.ForOwnerGranted(SyntheticMailOwner.Deployment));

        // Act
        var owned = catalog.OwnedAccounts;

        // Assert
        Assert.Equal([ServedAccount], owned);
    }

    /// <summary>
    /// The refusal a caller sees for another owner's account has to be the one they see for an account nobody
    /// configured, which is what an empty catalog produces: resolution then narrows the scope rather than reporting
    /// that the account exists and belongs to somebody else.
    /// </summary>
    [Fact]
    public void OwnedAccounts_AnotherOwner_OwnsNothingThisDeploymentServes()
    {
        // Arrange
        var catalog = CatalogFor(AccessAuthorizations.ForOwnerGranted(SyntheticMailOwner.Another));

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
    [MemberData(nameof(PrincipalsActingForNoOwner))]
    public void OwnedAccounts_APrincipalActingForNoOwner_IsRefused(AuthorizedPrincipal principal)
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
    /// The deployment this change exists to enable serves several owners, and every owner-facing read runs through here.
    /// Each of them is answered with the accounts served under their own name rather than with a refusal that no sole
    /// owner could be named, which is what asking the deployment for one would have produced.
    /// </summary>
    [Fact]
    public void OwnedAccounts_ADeploymentServingTwoOwners_AnswersEachWithTheirOwnAccounts()
    {
        // Arrange
        var deploymentOwner = CatalogFor(
            AccessAuthorizations.ForOwnerGranted(SyntheticMailOwner.Deployment),
            servedAccounts: [ServedAccount, AnotherOwnersAccount]);

        var anotherOwner = CatalogFor(
            AccessAuthorizations.ForOwnerGranted(SyntheticMailOwner.Another),
            servedAccounts: [ServedAccount, AnotherOwnersAccount]);

        // Act
        var deploymentOwnersAccounts = deploymentOwner.OwnedAccounts;
        var anotherOwnersAccounts = anotherOwner.OwnedAccounts;

        // Assert
        Assert.Equal([ServedAccount], deploymentOwnersAccounts);
        Assert.Equal([AnotherOwnersAccount], anotherOwnersAccounts);
    }

    /// <summary>Whether synchronization runs is a deployment fact rather than a caller's, so it is reported unchanged.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SynchronizationEnabled_WhateverTheDeploymentDecided_IsReportedUnchanged(bool synchronizationEnabled)
    {
        // Arrange
        var catalog = CatalogFor(
            AccessAuthorizations.ForOwnerGranted(SyntheticMailOwner.Deployment),
            synchronizationEnabled);

        // Act & Assert
        Assert.Equal(synchronizationEnabled, catalog.SynchronizationEnabled);
    }

    public static TheoryData<AuthorizedPrincipal> PrincipalsActingForNoOwner() =>
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
