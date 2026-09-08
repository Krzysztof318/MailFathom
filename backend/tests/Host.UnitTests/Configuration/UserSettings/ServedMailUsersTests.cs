// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Failures;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Spam;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.UserSettings;

/// <summary>Covers the roster every admitted caller and every synchronization run is composed against.</summary>
public sealed class ServedMailUsersTests
{
    private static readonly MailUserId SecondUser =
        MailUserId.Create(new Guid("4a4f1cc2-9d0e-4f1a-9b2f-6c9e2d4a7b31"));

    [Fact]
    public void User_AfterTheGateResolvedASoleUser_ReportsTheUserTheDeploymentServes()
    {
        // Arrange
        var servedUsers = new ServedMailUsers();

        // Act
        servedUsers.Resolved([Serving(SyntheticMailUser.Deployment, "user")]);

        // Assert
        Assert.Equal(SyntheticMailUser.Deployment, servedUsers.User);
    }

    /// <summary>
    /// Reading it before the gate has settled it is a wiring defect rather than a deployment's problem, so it fails as
    /// one instead of answering with the identity that names nobody — which every unresolved holder would agree on.
    /// </summary>
    [Fact]
    public void User_BeforeTheGateResolvedIt_FailsRatherThanNamingNobody()
    {
        // Arrange
        var servedUsers = new ServedMailUsers();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => servedUsers.User);
    }

    /// <summary>
    /// Attributing a caller to whichever user came first is how one person is handed another person's mail. The
    /// failure is a classified one rather than the wiring defect above, because a roster of several is a deployment an
    /// operator composed and a start admitted: what reaches this is one administrative act by a credential naming
    /// nobody, and it is answered rather than reported as a fault.
    /// </summary>
    [Fact]
    public void User_WhenSeveralUsersAreServed_FailsAsAClassifiedRefusalRatherThanPickingOne()
    {
        // Arrange
        var servedUsers = new ServedMailUsers();

        // Act
        servedUsers.Resolved(
        [
            Serving(SyntheticMailUser.Deployment, "alex"),
            Serving(SecondUser, "morgan"),
        ]);

        // Assert
        var refusal = Assert.Throws<DeploymentMailUserUnresolvedException>(() => servedUsers.User);

        Assert.Equal(MailFathomErrorCode.DeploymentMailUserUnresolved, refusal.ErrorCode);
    }

    /// <summary>The empty roster is what an unresolved holder would look like, and neither is a deployment.</summary>
    [Fact]
    public void Resolved_ARosterServingNobody_IsRejected()
    {
        // Arrange
        var servedUsers = new ServedMailUsers();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => servedUsers.Resolved([]));
    }

    [Fact]
    public void FindAccount_AnAccountOneUserDeclares_ReportsThatUserAndTheirDeclaration()
    {
        // Arrange
        var servedUsers = new ServedMailUsers();
        var account = new MailSynchronizationAccountOptions { AccountId = "shared-name" };

        servedUsers.Resolved(
        [
            Serving(SyntheticMailUser.Deployment, "alex"),
            Serving(SecondUser, "morgan", account),
        ]);

        // Act
        var found = servedUsers.FindAccount(MailAccountId.Create("shared-name"));

        // Assert
        Assert.Equal(SecondUser, found?.User);
        Assert.Same(account, found?.Account);
    }

    /// <summary>The deployment's own section is the reloadable snapshot's, which is where the lookup calling this looks first.</summary>
    [Fact]
    public void FindAccount_AnIdentifierNoUserOfTheRosterDeclares_ReportsNothing()
    {
        // Arrange
        var servedUsers = new ServedMailUsers();

        servedUsers.Resolved([Serving(SyntheticMailUser.Deployment, "user")]);

        // Act
        var found = servedUsers.FindAccount(MailAccountId.Create("primary"));

        // Assert
        Assert.Null(found);
    }

    /// <summary>A slow publisher cannot put an older committed document back after a newer one reached the roster.</summary>
    [Fact]
    public void UserDocumentPublished_AnOlderCommittedVersionArrivesLast_KeepsTheNewerDocument()
    {
        // Arrange
        var servedUsers = new ServedMailUsers();
        var older = new MailSynchronizationAccountOptions { AccountId = "older" };
        var newer = new MailSynchronizationAccountOptions { AccountId = "newer" };

        servedUsers.Resolved([Serving(SyntheticMailUser.Deployment, "user")]);

        // Act
        servedUsers.UserDocumentPublished(SyntheticMailUser.Deployment, "user", new UserAccountOptions { MailAccounts = [newer] }, 3);
        servedUsers.UserDocumentPublished(SyntheticMailUser.Deployment, "user", new UserAccountOptions { MailAccounts = [older] }, 2);

        // Assert
        Assert.Same(newer, Assert.Single(servedUsers.Users).MailAccounts.Single());
    }

    /// <summary>A committed record decides how that user's mail is classified, so the roster carries the block beside the mailboxes.</summary>
    /// <remarks>
    /// The whole of what makes a document actually take over: a row still answering with no block reads as classification
    /// off for that user, which is not what a record stating a posture says either — a commit switching the scanner on
    /// would go on classifying nothing.
    /// </remarks>
    [Fact]
    public void UserDocumentPublished_ARecordCarryingAClassificationBlock_ServesThatUserFromIt()
    {
        // Arrange
        var servedUsers = new ServedMailUsers();
        var classification = new UserSpamClassificationOptions { Enabled = true, UseScanner = true };

        servedUsers.Resolved([Serving(SyntheticMailUser.Deployment, "user")]);

        // Act
        servedUsers.UserDocumentPublished(
            SyntheticMailUser.Deployment,
            "user",
            new UserAccountOptions { SpamClassification = classification },
            2);

        // Assert
        var served = Assert.Single(servedUsers.Users);

        Assert.False(served.ReadFromConfiguration);
        Assert.Same(classification, served.SpamClassification);
    }

    /// <summary>Two user-document writes cannot validate and publish against the same runtime roster.</summary>
    [Fact]
    public async Task WaitForRosterPublicationAsync_AnotherWriterHoldsThePublicationGate_WaitsForItsRelease()
    {
        // Arrange
        var servedUsers = new ServedMailUsers();
        await servedUsers.WaitForRosterPublicationAsync(TestContext.Current.CancellationToken);

        // Act
        var secondWriter = servedUsers.WaitForRosterPublicationAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(secondWriter.IsCompleted);
        servedUsers.ReleaseRosterPublication();
        await secondWriter;
        servedUsers.ReleaseRosterPublication();
    }

    private static ServedMailUser Serving(
        MailUserId user,
        string displayName,
        params MailSynchronizationAccountOptions[] mailAccounts) =>
        new(
            user,
            displayName,
            mailAccounts.Length == 0
                ? MailUserAccountSource.DeploymentSection
                : MailUserAccountSource.UserDeclaration,
            mailAccounts);
}
