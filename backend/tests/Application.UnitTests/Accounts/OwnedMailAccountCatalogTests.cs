// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
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
/// Everything a caller may reach is composed from this answer, so the three outcomes worth stating are all here: the
/// owner the deployment serves owns what it serves, another owner owns none of it, and a principal acting for no owner
/// is refused rather than answered with an empty set.
/// </remarks>
public sealed class OwnedMailAccountCatalogTests
{
    private static readonly ServedMailAccount ServedAccount = new(
        SyntheticMailOwner.Deployment,
        MailAccountId.Create("personal"),
        MailAccountDisplayName.Create("Personal mail"),
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
        bool synchronizationEnabled = true)
    {
        var servedAccounts = Substitute.For<IDeploymentMailAccountCatalog>();
        servedAccounts.ServedAccounts.Returns([ServedAccount]);
        servedAccounts.SynchronizationEnabled.Returns(synchronizationEnabled);

        var deploymentOwner = Substitute.For<IDeploymentMailOwnerSource>();
        deploymentOwner.Owner.Returns(SyntheticMailOwner.Deployment);

        return new OwnedMailAccountCatalog(servedAccounts, deploymentOwner, authorization);
    }
}
