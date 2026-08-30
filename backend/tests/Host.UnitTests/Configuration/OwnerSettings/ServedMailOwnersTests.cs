// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Failures;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.OwnerSettings;
using MailFathom.Host.Configuration.Spam;
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

    /// <summary>
    /// Attributing a caller to whichever owner came first is how one person is handed another person's mail. The
    /// failure is a classified one rather than the wiring defect above, because a roster of several is a deployment an
    /// operator composed and a start admitted: what reaches this is one administrative act by a credential naming
    /// nobody, and it is answered rather than reported as a fault.
    /// </summary>
    [Fact]
    public void Owner_WhenSeveralOwnersAreServed_FailsAsAClassifiedRefusalRatherThanPickingOne()
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
        var refusal = Assert.Throws<DeploymentMailOwnerUnresolvedException>(() => servedOwners.Owner);

        Assert.Equal(MailFathomErrorCode.DeploymentMailOwnerUnresolved, refusal.ErrorCode);
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

    /// <summary>A slow publisher cannot put an older committed document back after a newer one reached the roster.</summary>
    [Fact]
    public void OwnerDocumentPublished_AnOlderCommittedVersionArrivesLast_KeepsTheNewerDocument()
    {
        // Arrange
        var servedOwners = new ServedMailOwners();
        var older = new MailSynchronizationAccountOptions { AccountId = "older" };
        var newer = new MailSynchronizationAccountOptions { AccountId = "newer" };

        servedOwners.Resolved([Serving(SyntheticMailOwner.Deployment, "owner")]);

        // Act
        servedOwners.OwnerDocumentPublished(SyntheticMailOwner.Deployment, "owner", new OwnerAccountOptions { MailAccounts = [newer] }, 3);
        servedOwners.OwnerDocumentPublished(SyntheticMailOwner.Deployment, "owner", new OwnerAccountOptions { MailAccounts = [older] }, 2);

        // Assert
        Assert.Same(newer, Assert.Single(servedOwners.Owners).MailAccounts.Single());
    }

    /// <summary>A committed record decides how that owner's mail is classified, so the roster carries the block beside the mailboxes.</summary>
    /// <remarks>
    /// The whole of what makes a document actually take over: a row still answering with no block would be read from the
    /// deployment's section, so a commit switching classification off would go on classifying that owner's mail.
    /// </remarks>
    [Fact]
    public void OwnerDocumentPublished_ARecordCarryingAClassificationBlock_ServesThatOwnerFromIt()
    {
        // Arrange
        var servedOwners = new ServedMailOwners();
        var classification = new OwnerSpamClassificationOptions { Enabled = true, UseScanner = true };

        servedOwners.Resolved([Serving(SyntheticMailOwner.Deployment, "owner")]);

        // Act
        servedOwners.OwnerDocumentPublished(
            SyntheticMailOwner.Deployment,
            "owner",
            new OwnerAccountOptions { SpamClassification = classification },
            2);

        // Assert
        var served = Assert.Single(servedOwners.Owners);

        Assert.False(served.ReadFromConfiguration);
        Assert.Same(classification, served.SpamClassification);
    }

    /// <summary>Two owner-document writes cannot validate and publish against the same runtime roster.</summary>
    [Fact]
    public async Task WaitForRosterPublicationAsync_AnotherWriterHoldsThePublicationGate_WaitsForItsRelease()
    {
        // Arrange
        var servedOwners = new ServedMailOwners();
        await servedOwners.WaitForRosterPublicationAsync(TestContext.Current.CancellationToken);

        // Act
        var secondWriter = servedOwners.WaitForRosterPublicationAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(secondWriter.IsCompleted);
        servedOwners.ReleaseRosterPublication();
        await secondWriter;
        servedOwners.ReleaseRosterPublication();
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
