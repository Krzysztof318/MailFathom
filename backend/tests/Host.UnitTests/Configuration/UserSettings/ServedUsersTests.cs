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
using MailFathom.Host.Observability.ClientTelemetry;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.UserSettings;

/// <summary>Covers the roster every admitted caller and every synchronization run is composed against.</summary>
public sealed class ServedUsersTests
{
    private static readonly UserId SecondUser =
        UserId.Create(new Guid("4a4f1cc2-9d0e-4f1a-9b2f-6c9e2d4a7b31"));

    [Fact]
    public void User_AfterTheGateResolvedASoleUser_ReportsTheUserTheDeploymentServes()
    {
        // Arrange
        var servedUsers = new ServedUsers();

        // Act
        servedUsers.Resolved([Serving(SyntheticUser.Deployment, "user")]);

        // Assert
        Assert.Equal(SyntheticUser.Deployment, servedUsers.User);
    }

    /// <summary>
    /// Reading it before the gate has settled it is a wiring defect rather than a deployment's problem, so it fails as
    /// one instead of answering with the identity that names nobody — which every unresolved holder would agree on.
    /// </summary>
    [Fact]
    public void User_BeforeTheGateResolvedIt_FailsRatherThanNamingNobody()
    {
        // Arrange
        var servedUsers = new ServedUsers();

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
        var servedUsers = new ServedUsers();

        // Act
        servedUsers.Resolved(
        [
            Serving(SyntheticUser.Deployment, "alex"),
            Serving(SecondUser, "morgan"),
        ]);

        // Assert
        var refusal = Assert.Throws<DeploymentUserUnresolvedException>(() => servedUsers.User);

        Assert.Equal(MailFathomErrorCode.DeploymentUserUnresolved, refusal.ErrorCode);
    }

    /// <summary>A deployment holding no user serves nobody, which is where one whose every user was erased stands.</summary>
    [Fact]
    public void Resolved_ARosterServingNobody_ServesNobodyRatherThanRefusing()
    {
        // Arrange
        var servedUsers = new ServedUsers();

        // Act
        servedUsers.Resolved([]);

        // Assert
        Assert.Empty(servedUsers.Users);
        Assert.Throws<DeploymentUserUnresolvedException>(() => servedUsers.User);
    }

    /// <summary>
    /// A caller naming no user on a deployment holding nobody is told so, and what records somebody — not the sentence
    /// meant for several users, whose remedy would be a credential for a person who does not exist.
    /// </summary>
    [Fact]
    public void User_WhenNobodyIsServed_NamesTheCommandThatRecordsSomebody()
    {
        // Arrange
        var servedUsers = new ServedUsers();
        servedUsers.Resolved([]);

        // Act
        var refusal = Assert.Throws<DeploymentUserUnresolvedException>(() => servedUsers.User);

        // Assert
        Assert.Equal(MailFathomErrorCode.DeploymentUserUnresolved, refusal.ErrorCode);
        Assert.Contains("mfctl user add", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FindAccount_AnAccountOneUserDeclares_ReportsThatUserAndTheirDeclaration()
    {
        // Arrange
        var servedUsers = new ServedUsers();
        var account = new MailSynchronizationAccountOptions { AccountId = "shared-name" };

        servedUsers.Resolved(
        [
            Serving(SyntheticUser.Deployment, "alex"),
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
        var servedUsers = new ServedUsers();

        servedUsers.Resolved([Serving(SyntheticUser.Deployment, "user")]);

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
        var servedUsers = new ServedUsers();
        var older = new MailSynchronizationAccountOptions { AccountId = "older" };
        var newer = new MailSynchronizationAccountOptions { AccountId = "newer" };

        servedUsers.Resolved([Serving(SyntheticUser.Deployment, "user")]);

        // Act
        servedUsers.UserDocumentPublished(SyntheticUser.Deployment, "user", new UserAccountOptions { MailAccounts = [newer] }, 3);
        servedUsers.UserDocumentPublished(SyntheticUser.Deployment, "user", new UserAccountOptions { MailAccounts = [older] }, 2);

        // Assert
        Assert.Same(newer, Assert.Single(servedUsers.Users).MailAccounts.Single());
    }

    /// <summary>A committed record decides how one mailbox's mail is classified, so the roster carries the block on the account.</summary>
    /// <remarks>
    /// The whole of what makes a document actually take over: an account still answering with no block reads as
    /// classification off for that mailbox, which is not what a record stating a posture says either — a commit
    /// switching the scanner on would go on classifying nothing.
    /// </remarks>
    [Fact]
    public void UserDocumentPublished_ARecordCarryingAClassificationBlock_ServesThatAccountFromIt()
    {
        // Arrange
        var servedUsers = new ServedUsers();
        var classification = new MailAccountSpamClassificationOptions { Enabled = true, UseScanner = true };
        var account = new MailSynchronizationAccountOptions { AccountId = "primary", SpamClassification = classification };

        servedUsers.Resolved([Serving(SyntheticUser.Deployment, "user")]);

        // Act
        servedUsers.UserDocumentPublished(
            SyntheticUser.Deployment,
            "user",
            new UserAccountOptions { MailAccounts = [account] },
            2);

        // Assert
        var served = Assert.Single(servedUsers.Users);

        Assert.Same(classification, served.MailAccounts.Single().SpamClassification);
    }

    /// <summary>A committed record decides the level this person's client is asked to record at, so the roster carries it without the process restarting.</summary>
    /// <remarks>
    /// This is the whole of what makes the raise take effect: the session route reads the level off the roster, so a
    /// republication that dropped it would leave an operator's <c>mfctl user edit</c> waiting for a restart it was
    /// written to avoid.
    /// </remarks>
    [Fact]
    public void UserDocumentPublished_ARecordStatingAClientTelemetryLevel_ServesThatUserFromIt()
    {
        // Arrange
        var servedUsers = new ServedUsers();

        servedUsers.Resolved([Serving(SyntheticUser.Deployment, "user")]);

        // Act
        servedUsers.UserDocumentPublished(
            SyntheticUser.Deployment,
            "user",
            new UserAccountOptions { ClientTelemetryLevel = "Debug" },
            2);

        // Assert
        Assert.Equal(ClientTelemetryLevel.Debug, Assert.Single(servedUsers.Users).ClientTelemetryLevel);
    }

    /// <summary>Two user-document writes cannot validate and publish against the same runtime roster.</summary>
    [Fact]
    public async Task WaitForRosterPublicationAsync_AnotherWriterHoldsThePublicationGate_WaitsForItsRelease()
    {
        // Arrange
        var servedUsers = new ServedUsers();
        await servedUsers.WaitForRosterPublicationAsync(TestContext.Current.CancellationToken);

        // Act
        var secondWriter = servedUsers.WaitForRosterPublicationAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(secondWriter.IsCompleted);
        servedUsers.ReleaseRosterPublication();
        await secondWriter;
        servedUsers.ReleaseRosterPublication();
    }

    /// <summary>An erasure has to stop this replica serving the user before it deletes anything, which is what this is.</summary>
    [Fact]
    public void Withhold_AUserThisDeploymentServes_TakesThemOffTheRosterAndSignalsAReload()
    {
        // Arrange
        var servedUsers = new ServedUsers();
        servedUsers.Resolved([Serving(SyntheticUser.Deployment, "user"), Serving(SecondUser, "second")]);
        var reloaded = false;
        servedUsers.GetReloadToken().RegisterChangeCallback(_ => reloaded = true, state: null);

        // Act
        using var withheld = servedUsers.Withhold(SyntheticUser.Deployment);

        // Assert
        Assert.Equal([SecondUser], [.. servedUsers.Users.Select(static served => served.User)]);
        Assert.True(reloaded);
    }

    /// <summary>A refused erasure removed nothing, so the person it was refused for goes on being served.</summary>
    [Fact]
    public void Withhold_DisposedWithoutTheUserBeingErased_PutsThemBackWhereTheyWere()
    {
        // Arrange
        var servedUsers = new ServedUsers();
        servedUsers.Resolved([Serving(SyntheticUser.Deployment, "user"), Serving(SecondUser, "second")]);

        // Act
        servedUsers.Withhold(SyntheticUser.Deployment).Dispose();

        // Assert
        Assert.Equal(
            [SyntheticUser.Deployment, SecondUser],
            [.. servedUsers.Users.Select(static served => served.User)]);
    }

    /// <summary>An erasure that committed leaves nobody to put back, whatever the withholding is disposed of after.</summary>
    [Fact]
    public void Withhold_TheUserWasErased_LeavesThemOffTheRoster()
    {
        // Arrange
        var servedUsers = new ServedUsers();
        servedUsers.Resolved([Serving(SyntheticUser.Deployment, "user"), Serving(SecondUser, "second")]);

        // Act
        using (var withheld = servedUsers.Withhold(SyntheticUser.Deployment))
        {
            withheld.Erased();
        }

        // Assert
        Assert.Equal([SecondUser], [.. servedUsers.Users.Select(static served => served.User)]);
    }

    /// <summary>
    /// A withholding shrinks the roster before anything is deleted, and an erasure can still be refused. Letting that
    /// shrink reach the sole-user reading would admit a caller naming nobody as the remaining person — for the whole
    /// of the wait, the deletion, and the entire length of a refusal — where a moment earlier it had no answer at all.
    /// </summary>
    [Fact]
    public void User_WhileAUserIsWithheldFromARosterOfSeveral_GoesOnRefusingToNameASoleUser()
    {
        // Arrange
        var servedUsers = new ServedUsers();
        servedUsers.Resolved([Serving(SyntheticUser.Deployment, "alex"), Serving(SecondUser, "morgan")]);

        // Act
        using var withheld = servedUsers.Withhold(SyntheticUser.Deployment);

        // Assert
        var refusal = Assert.Throws<DeploymentUserUnresolvedException>(() => servedUsers.User);

        Assert.Equal(MailFathomErrorCode.DeploymentUserUnresolved, refusal.ErrorCode);
    }

    /// <summary>Once the deletion has committed the deployment really does serve one user, and names them.</summary>
    [Fact]
    public void User_AfterAWithheldUserWasErased_NamesTheOneTheDeploymentIsLeftServing()
    {
        // Arrange
        var servedUsers = new ServedUsers();
        servedUsers.Resolved([Serving(SyntheticUser.Deployment, "alex"), Serving(SecondUser, "morgan")]);

        // Act
        using (var withheld = servedUsers.Withhold(SyntheticUser.Deployment))
        {
            withheld.Erased();
        }

        // Assert
        Assert.Equal(SecondUser, servedUsers.User);
    }

    private static ServedUser Serving(
        UserId user,
        string displayName,
        params MailSynchronizationAccountOptions[] mailAccounts) =>
        new(user, displayName, mailAccounts);
}
