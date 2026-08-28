// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.OwnerSettings;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.OwnerSettings;

/// <summary>Covers the roster every admitted caller and every synchronization run is composed against.</summary>
public sealed class ServedMailOwnersTests
{
    private static readonly MailOwnerId SecondOwner =
        MailOwnerId.Create(new Guid("4a4f1cc2-9d0e-4f1a-9b2f-6c9e2d4a7b31"));

    [Fact]
    public void Owner_AfterTheGateResolvedASoleOwner_ReportsTheOwnerTheDeploymentServes()
    {
        // Arrange
        var servedOwners = new ServedMailOwners();

        // Act
        servedOwners.Resolved([Serving(SyntheticMailOwner.Deployment, "owner")]);

        // Assert
        Assert.Equal(SyntheticMailOwner.Deployment, servedOwners.Owner);
    }

    /// <summary>
    /// Reading it before the gate has settled it is a wiring defect rather than a deployment's problem, so it fails as
    /// one instead of answering with the identity that names nobody — which every unresolved holder would agree on.
    /// </summary>
    [Fact]
    public void Owner_BeforeTheGateResolvedIt_FailsRatherThanNamingNobody()
    {
        // Arrange
        var servedOwners = new ServedMailOwners();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => servedOwners.Owner);
    }

    /// <summary>Attributing a caller to whichever owner came first is how one person is handed another person's mail.</summary>
    [Fact]
    public void Owner_WhenSeveralOwnersAreServed_FailsRatherThanPickingOne()
    {
        // Arrange
        var servedOwners = new ServedMailOwners();

        // Act
        servedOwners.Resolved(
        [
            Serving(SyntheticMailOwner.Deployment, "alex"),
            Serving(SecondOwner, "morgan"),
        ]);

        // Assert
        Assert.Throws<InvalidOperationException>(() => servedOwners.Owner);
    }

    /// <summary>The empty roster is what an unresolved holder would look like, and neither is a deployment.</summary>
    [Fact]
    public void Resolved_ARosterServingNobody_IsRejected()
    {
        // Arrange
        var servedOwners = new ServedMailOwners();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => servedOwners.Resolved([]));
    }

    [Fact]
    public void FindAccount_AnAccountOneOwnerDeclares_ReportsThatOwnerAndTheirDeclaration()
    {
        // Arrange
        var servedOwners = new ServedMailOwners();
        var account = new MailSynchronizationAccountOptions { AccountId = "shared-name" };

        servedOwners.Resolved(
        [
            Serving(SyntheticMailOwner.Deployment, "alex"),
            Serving(SecondOwner, "morgan", account),
        ]);

        // Act
        var found = servedOwners.FindAccount(MailAccountId.Create("shared-name"));

        // Assert
        Assert.Equal(SecondOwner, found?.Owner);
        Assert.Same(account, found?.Account);
    }

    /// <summary>The deployment's own section is the reloadable snapshot's, which is where the lookup calling this looks first.</summary>
    [Fact]
    public void FindAccount_AnIdentifierNoOwnerOfTheRosterDeclares_ReportsNothing()
    {
        // Arrange
        var servedOwners = new ServedMailOwners();

        servedOwners.Resolved([Serving(SyntheticMailOwner.Deployment, "owner")]);

        // Act
        var found = servedOwners.FindAccount(MailAccountId.Create("primary"));

        // Assert
        Assert.Null(found);
    }

    private static ServedMailOwner Serving(
        MailOwnerId owner,
        string displayName,
        params MailSynchronizationAccountOptions[] mailAccounts) =>
        new(
            owner,
            displayName,
            mailAccounts.Length == 0
                ? MailOwnerAccountSource.DeploymentSection
                : MailOwnerAccountSource.OwnerDeclaration,
            mailAccounts);
}
