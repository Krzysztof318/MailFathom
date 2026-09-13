// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.StoredFiles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Failures;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Administration;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.SensitiveContent;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.Configuration.UserSettings.Administration;
using MailFathom.Host.Signals;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.UserSettings.Administration;

/// <summary>
/// Covers what is done to one user's own record. Two callers reach it — an administrator naming the user, and that
/// user naming nobody — and the pairs of entry points delegate to the same work, so the cases that matter are the
/// rules the work holds: that a user a file still supplies is refused rather than quietly emptied, that a stale
/// version is refused before a candidate is composed, that a user reads back a redacted record, and that a user
/// naming somebody else has no argument to name them with.
/// </summary>
public sealed class UserRecordAdministrationTests
{
    private const string AdministratorIdentity = "operations";

    /// <summary>A record of a user's language and nothing else, which is what a provisioning leaves behind.</summary>
    private const string LanguageOnlyRecord = """{"Language":"English"}""";

    private static readonly DateTimeOffset Today = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReadRecordAsync_AUserThisDeploymentHolds_ReportsTheirRecordAndTheVersionAChangeIsComposedOver()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminRead);
        harness.Holding(SyntheticMailUser.Deployment, Declaring("primary"), version: 4);

        // Act
        var reading = await harness.Records.ReadRecordAsync(
            SyntheticMailUser.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(4, reading!.Version);
        Assert.Equal(SyntheticMailUser.Deployment, reading.User);
    }

    /// <summary>
    /// The record is what a caller edits and hands back, and what it carries under a secret-bearing setting is a
    /// reference the deployment resolves — so the reading replaces it rather than publishing what an operator wrote.
    /// </summary>
    [Fact]
    public async Task ReadRecordAsync_ARecordCarryingASecretBearingValue_ReplacesItWithTheRedactionMarker()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminRead);
        harness.Holding(SyntheticMailUser.Deployment, Declaring("primary"), version: 1);

        // Act
        var reading = await harness.Records.ReadRecordAsync(
            SyntheticMailUser.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain("/run/secrets/primary-password", reading!.Json, StringComparison.Ordinal);
    }

    /// <summary>A link to somebody else's file would serve their octets as this user's picture, and only the database can say whose a file is.</summary>
    [Fact]
    public async Task ApplyOwnRecordAsync_ARecordLinkingAFileThatIsNotTheUsers_IsRefusedAndNothingIsCommitted()
    {
        // Arrange
        var foreign = Guid.Parse("0197a3c0-0000-7000-8000-00000000f00d");
        var harness = new RecordHarness(MailFathomPermission.MailAccountsWrite, actingFor: SyntheticMailUser.Deployment);
        harness.Holding(SyntheticMailUser.Deployment, LanguageOnlyRecord, version: 3);
        harness.Files.HoldsAsync(SyntheticMailUser.Deployment, StoredFileId.Create(foreign), Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        var outcome = await harness.Records.ApplyOwnRecordAsync(
            $$"""{"Language":"English","Portrait":"{{foreign:D}}"}""",
            expectedVersion: 3,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
        Assert.Contains("Portrait", Assert.Single(outcome.Messages), StringComparison.Ordinal);
        await harness.Store.DidNotReceive().CommitAsync(
            Arg.Any<MailUserId>(),
            Arg.Any<string>(),
            Arg.Any<MailUserEndpointAccess>(),
            Arg.Any<long>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyOwnRecordAsync_ARecordLinkingAFileTheUserHolds_IsCommitted()
    {
        // Arrange
        var own = Guid.Parse("0197a3c0-0000-7000-8000-000000000001");
        var harness = new RecordHarness(MailFathomPermission.MailAccountsWrite, actingFor: SyntheticMailUser.Deployment);
        harness.Holding(SyntheticMailUser.Deployment, LanguageOnlyRecord, version: 3);
        harness.Files.HoldsAsync(SyntheticMailUser.Deployment, StoredFileId.Create(own), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var outcome = await harness.Records.ApplyOwnRecordAsync(
            $$"""{"Language":"English","Portrait":"{{own:D}}"}""",
            expectedVersion: 3,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
    }

    /// <summary>
    /// A committed record is announced so a replica that did not commit it reads it at once, and only once the roster is
    /// released, so a backplane slow to answer holds no other roster write behind it.
    /// </summary>
    [Fact]
    public async Task ApplyOwnRecordAsync_ACommittedRecord_AnnouncesTheChangeOnceTheRosterIsReleased()
    {
        // Arrange
        var own = Guid.Parse("0197a3c0-0000-7000-8000-000000000001");
        var harness = new RecordHarness(MailFathomPermission.MailAccountsWrite, actingFor: SyntheticMailUser.Deployment);
        harness.Holding(SyntheticMailUser.Deployment, LanguageOnlyRecord, version: 3);
        harness.Files.HoldsAsync(SyntheticMailUser.Deployment, StoredFileId.Create(own), Arg.Any<CancellationToken>())
            .Returns(true);
        var heard = await RosterAnnouncementListener.ListenAsync(harness.Backplane, harness.ServedUsers);

        // Act
        await harness.Records.ApplyOwnRecordAsync(
            $$"""{"Language":"English","Portrait":"{{own:D}}"}""",
            expectedVersion: 3,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([true], heard);
    }

    /// <summary>A refused record changed nothing, so no replica is asked to read anything again.</summary>
    [Fact]
    public async Task ApplyOwnRecordAsync_ARefusedRecord_AnnouncesNothing()
    {
        // Arrange
        var foreign = Guid.Parse("0197a3c0-0000-7000-8000-00000000f00d");
        var harness = new RecordHarness(MailFathomPermission.MailAccountsWrite, actingFor: SyntheticMailUser.Deployment);
        harness.Holding(SyntheticMailUser.Deployment, LanguageOnlyRecord, version: 3);
        harness.Files.HoldsAsync(SyntheticMailUser.Deployment, StoredFileId.Create(foreign), Arg.Any<CancellationToken>())
            .Returns(false);
        var heard = await RosterAnnouncementListener.ListenAsync(harness.Backplane, harness.ServedUsers);

        // Act
        await harness.Records.ApplyOwnRecordAsync(
            $$"""{"Language":"English","Portrait":"{{foreign:D}}"}""",
            expectedVersion: 3,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(heard);
    }

    /// <summary>What the use case removes afterwards is the file the link displaced, so the relink has to name it.</summary>
    [Fact]
    public async Task RelinkOwnPortraitAsync_ARecordLinkingAnEarlierPortrait_LinksTheNewFileAndNamesTheOneItDisplaced()
    {
        // Arrange
        var earlier = StoredFileId.Create(Guid.Parse("0197a3c0-0000-7000-8000-000000000001"));
        var written = StoredFileId.Create(Guid.Parse("0197a3c0-0000-7000-8000-000000000002"));
        var harness = new RecordHarness(MailFathomPermission.MailRead, actingFor: SyntheticMailUser.Deployment);
        harness.Holding(SyntheticMailUser.Deployment, $$"""{"Language":"English","Portrait":"{{earlier}}"}""", version: 5);
        harness.Files.HoldsAsync(SyntheticMailUser.Deployment, written, Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var relinked = await harness.Records.RelinkOwnPortraitAsync(written, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new PortraitRelinking(UserHeld: true, Replaced: earlier), relinked);
        await harness.Store.Received(1).CommitAsync(
            SyntheticMailUser.Deployment,
            Arg.Is<string>(json => json!.Contains(written.ToString(), StringComparison.Ordinal)),
            Arg.Any<MailUserEndpointAccess>(),
            5,
            Arg.Any<CancellationToken>());
    }

    /// <summary>A record committed before a language was required still takes a portrait, because the link is all that changes.</summary>
    [Fact]
    public async Task RelinkOwnPortraitAsync_ARecordHeldFromBeforeALanguageWasRequired_IsStillLinked()
    {
        // Arrange
        var written = StoredFileId.Create(Guid.Parse("0197a3c0-0000-7000-8000-000000000002"));
        var harness = new RecordHarness(MailFathomPermission.MailRead, actingFor: SyntheticMailUser.Deployment);
        harness.Holding(SyntheticMailUser.Deployment, "{}", version: 1);
        harness.Files.HoldsAsync(SyntheticMailUser.Deployment, written, Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var relinked = await harness.Records.RelinkOwnPortraitAsync(written, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new PortraitRelinking(UserHeld: true, Replaced: null), relinked);
    }

    /// <summary>The relink is an entry point of its own, so it holds the grant itself rather than trusting whoever called it to have checked.</summary>
    [Fact]
    public async Task RelinkOwnPortraitAsync_ACallerNotGrantedTheirOwnMail_IsRefusedBeforeTheRecordIsRead()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.MailAccountsWrite, actingFor: SyntheticMailUser.Deployment);

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => harness.Records.RelinkOwnPortraitAsync(null, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailRead, refusal.RequiredPermission);
        await harness.Documents.DidNotReceive().ReadAsync(Arg.Any<MailUserId>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Whose record is rewritten is the caller's to say only by being that user, so a caller acting for nobody rewrites nothing.</summary>
    [Fact]
    public async Task RelinkOwnPortraitAsync_ACallerActingForNoUser_IsRefusedBeforeTheRecordIsRead()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.MailRead);

        // Act
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => harness.Records.RelinkOwnPortraitAsync(null, TestContext.Current.CancellationToken));

        // Assert
        await harness.Documents.DidNotReceive().ReadAsync(Arg.Any<MailUserId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadRecordAsync_AUserThisDeploymentDoesNotHold_ReportsNothing()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminRead);

        // Act
        var reading = await harness.Records.ReadRecordAsync(
            SyntheticMailUser.Another,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(reading);
    }

    [Fact]
    public async Task ReadRecordAsync_ACallerHoldingNoAdministrativeRead_IsRefused()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.MailRead);
        harness.Holding(SyntheticMailUser.Deployment, LanguageOnlyRecord, version: 1);

        // Act & Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => harness.Records.ReadRecordAsync(SyntheticMailUser.Deployment, TestContext.Current.CancellationToken));
    }

    /// <summary>A user's own entry point resolves the user from whoever was admitted, so no request can name another.</summary>
    [Fact]
    public async Task ReadOwnRecordAsync_AUserSignedIn_ReadsTheRecordOfWhoeverWasAdmittedRatherThanOneNamedInARequest()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.MailRead, actingFor: SyntheticMailUser.Deployment);
        harness.Holding(SyntheticMailUser.Deployment, LanguageOnlyRecord, version: 2);

        // Act
        var reading = await harness.Records.ReadOwnRecordAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SyntheticMailUser.Deployment, reading!.User);
    }

    /// <summary>A caller acting for nobody's mail on a user-facing route is an entrypoint that never said whose record it wanted.</summary>
    [Fact]
    public async Task ReadOwnRecordAsync_ACallerActingForNobody_IsRefused()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.MailRead);

        // Act & Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => harness.Records.ReadOwnRecordAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadRecordAsync_AUserNamingNobody_IsRefusedWithoutReachingTheStore()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminRead);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => harness.Records.ReadRecordAsync(default, TestContext.Current.CancellationToken));

        await harness.Documents.DidNotReceiveWithAnyArgs().ReadAsync(default, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A user the roster does not hold has no configuration section a write could be replacing, which is what makes
    /// a user an administrator has just recorded writable at once rather than after a restart.
    /// </summary>
    [Fact]
    public async Task AddMailAccountAsync_AUserProvisionedAfterTheRosterWasSettled_IsAnOrdinaryWrite()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(SyntheticMailUser.Another, LanguageOnlyRecord, version: 1);
        harness.Roster(Serving(SyntheticMailUser.Deployment));

        // Act
        var outcome = await harness.Records.AddMailAccountAsync(
            SyntheticMailUser.Another,
            Account("archive"),
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
    }

    /// <summary>The version is checked before a candidate is composed and bound rather than after, so nothing is judged against a record somebody else replaced.</summary>
    [Fact]
    public async Task AddMailAccountAsync_AVersionSomebodyElseHasMovedPast_IsRefusedReportingTheVersionNowInForce()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(SyntheticMailUser.Deployment, LanguageOnlyRecord, version: 7);

        // Act
        var outcome = await harness.Records.AddMailAccountAsync(
            SyntheticMailUser.Deployment,
            Account("archive"),
            expectedVersion: 4,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationVersionSuperseded, outcome!.Refusal);
        Assert.Equal(7, outcome.Version);
        await harness.Store.DidNotReceiveWithAnyArgs().CommitAsync(default, default!, default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>The candidate is put through the same binder a start reads a record with, so what a write accepts is what the next start would read.</summary>
    [Fact]
    public async Task AddMailAccountAsync_ADeclarationTheBinderRefuses_IsRefusedWithWhatHasToChange()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(SyntheticMailUser.Deployment, LanguageOnlyRecord, version: 1);

        // Act
        var outcome = await harness.Records.AddMailAccountAsync(
            SyntheticMailUser.Deployment,
            """{"AccountId":"archive"}""",
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
        Assert.NotEmpty(outcome.Messages);
        await harness.Store.DidNotReceiveWithAnyArgs().CommitAsync(default, default!, default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>Anything but an object of that account's settings is a caller sending the wrong thing, and the parser's own message names which — and names no value.</summary>
    [Fact]
    public async Task AddMailAccountAsync_ADeclarationThatIsNotAJsonObject_IsRefusedRatherThanRaised()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(SyntheticMailUser.Deployment, LanguageOnlyRecord, version: 1);

        // Act
        var outcome = await harness.Records.AddMailAccountAsync(
            SyntheticMailUser.Deployment,
            "not json",
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
    }

    /// <summary>A user's record is theirs, so a name that user already answers to is a collision the naming rules refuse rather than a deployment-wide one.</summary>
    [Fact]
    public async Task AddMailAccountAsync_AnAccountNameTheSameUserAlreadyDeclares_IsRefusedAsTheirOwnCollision()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(SyntheticMailUser.Deployment, Declaring("primary"), version: 1);

        // Act
        var outcome = await harness.Records.AddMailAccountAsync(
            SyntheticMailUser.Deployment,
            Account("primary"),
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
    }

    [Fact]
    public async Task AddMailAccountAsync_ADeclarationTheRecordAccepts_CommitsItOverTheVersionItWasComposedOn()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(SyntheticMailUser.Deployment, LanguageOnlyRecord, version: 3);

        // Act
        var outcome = await harness.Records.AddMailAccountAsync(
            SyntheticMailUser.Deployment,
            Account("archive"),
            expectedVersion: 3,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
        await harness.Store.Received(1).CommitAsync(
            SyntheticMailUser.Deployment,
            Arg.Is<string>(candidate => candidate!.Contains("archive", StringComparison.Ordinal)),
            MailUserEndpointAccess.Everywhere,
            3,
            Arg.Any<CancellationToken>());
    }

    /// <summary>The committed document becomes the account source for this process, not only for its next start.</summary>
    [Fact]
    public async Task AddMailAccountAsync_ACommittedDocument_PublishesItsAccountToTheRunningDeployment()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(SyntheticMailUser.Deployment, LanguageOnlyRecord, version: 3);

        // Act
        var outcome = await harness.Records.AddMailAccountAsync(
            SyntheticMailUser.Deployment,
            Account("archive"),
            expectedVersion: 3,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
        Assert.Equal(
            SyntheticMailUser.Deployment,
            harness.ServedUsers.FindAccount(MailAccountId.Create("archive"))?.User);
    }

    /// <summary>
    /// A secret reference is a path into whatever this deployment can read — a mounted file, a credential, an
    /// environment variable — and the server the account names is the user's own. So a reference a user wrote would
    /// hand them whatever stands behind it, on a mailbox they control, and the only caller who may introduce one is
    /// whoever administers the deployment.
    /// </summary>
    [Fact]
    public async Task AddOwnMailAccountAsync_AMailboxNamingACredentialTheirRecordDoesNotCarry_IsRefused()
    {
        // Arrange
        var harness = new RecordHarness(
            MailFathomPermission.MailAccountsWrite,
            actingFor: SyntheticMailUser.Deployment);
        harness.Holding(SyntheticMailUser.Deployment, Declaring("primary"), version: 1);

        // Act
        var outcome = await harness.Records.AddOwnMailAccountAsync(
            Account("archive"),
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
        Assert.Contains(
            "mfctl user account add",
            Assert.Single(outcome.Messages),
            StringComparison.Ordinal);
        await harness.Store.DidNotReceiveWithAnyArgs()
            .CommitAsync(default, default!, default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// What a user may name is material this deployment provisioned for them, and the operator says which that is by
    /// naming it after the person it belongs to. Without this the rule would refuse every mailbox a user declares,
    /// which is the whole of what the client's own record surface is for.
    /// </summary>
    [Fact]
    public async Task AddOwnMailAccountAsync_AMailboxNamingMaterialProvisionedForThem_IsAnOrdinaryWrite()
    {
        // Arrange
        var harness = new RecordHarness(
            MailFathomPermission.MailAccountsWrite,
            actingFor: SyntheticMailUser.Deployment);
        harness.Holding(SyntheticMailUser.Deployment, LanguageOnlyRecord, version: 1);

        // Act
        var outcome = await harness.Records.AddOwnMailAccountAsync(
            AccountProvisionedFor(SyntheticMailUser.Deployment, "archive"),
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
    }

    /// <summary>
    /// The bound is the name of the material rather than where it is kept, because that is the part a path written in
    /// front of it cannot rewrite — so a user naming another user's credential is refused however they spell the
    /// way to it.
    /// </summary>
    [Fact]
    public async Task AddOwnMailAccountAsync_AMailboxNamingAnotherUsersCredential_IsRefused()
    {
        // Arrange
        var harness = new RecordHarness(
            MailFathomPermission.MailAccountsWrite,
            actingFor: SyntheticMailUser.Deployment);
        harness.Holding(SyntheticMailUser.Deployment, LanguageOnlyRecord, version: 1);

        // Act
        var outcome = await harness.Records.AddOwnMailAccountAsync(
            AccountProvisionedFor(SyntheticMailUser.Another, "archive"),
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
        await harness.Store.DidNotReceiveWithAnyArgs()
            .CommitAsync(default, default!, default, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AddOwnMailAccountAsync_AMailboxNamingAnArbitraryDatabaseSecret_IsRefusedBeforeResolution()
    {
        // Arrange
        var harness = new RecordHarness(
            MailFathomPermission.MailAccountsWrite,
            actingFor: SyntheticMailUser.Deployment);
        harness.Holding(SyntheticMailUser.Deployment, LanguageOnlyRecord, version: 1);

        // Act
        var outcome = await harness.Records.AddOwnMailAccountAsync(
            AccountSaved(
                "archive",
                "imap.example.test",
                "database:019925df-96f4-7c6d-8f91-b9f6cf27f5b2"),
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
        Assert.Contains("not provisioned for you", Assert.Single(outcome.Messages), StringComparison.Ordinal);
        await harness.Store.DidNotReceiveWithAnyArgs()
            .CommitAsync(default, default!, default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The rule reads which references a record carries rather than which path each sits at, so a user withdrawing
    /// the first of two mailboxes is not refused over the credential that moved up an index behind it. Comparing per
    /// path would refuse every withdrawal but the last one.
    /// </summary>
    [Fact]
    public async Task RemoveOwnMailAccountAsync_TheFirstOfTwoMailboxes_IsNotRefusedOverTheCredentialThatMovedUp()
    {
        // Arrange
        var harness = new RecordHarness(
            MailFathomPermission.MailAccountsWrite,
            actingFor: SyntheticMailUser.Deployment);
        harness.Holding(SyntheticMailUser.Deployment, Declaring("primary", "archive"), version: 1);

        // Act
        var outcome = await harness.Records.RemoveOwnMailAccountAsync(
            "primary",
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
    }

    /// <summary>
    /// The binder proves a record is a record; whether the credentials in it reach anything is a second question, and
    /// one only the walk every start runs answers. Without it a write is accepted and the mailbox then fails one
    /// connection at a time, which is the state the identical declaration in a file cannot reach.
    /// </summary>
    [Fact]
    public async Task AddMailAccountAsync_AMailboxWhoseCredentialsThisDeploymentCannotUse_IsRefusedBeforeItIsCommitted()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(SyntheticMailUser.Deployment, Declaring("primary"), version: 1);

        // Act
        var outcome = await harness.Records.AddMailAccountAsync(
            SyntheticMailUser.Deployment,
            AccountWhoseSecretReachesNothing("archive"),
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
        Assert.Contains(
            outcome.Messages,
            message => message.Contains(
                nameof(SecretResolutionFailure.MaterialNotFound),
                StringComparison.Ordinal));
        await harness.Store.DidNotReceiveWithAnyArgs()
            .CommitAsync(default, default!, default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A secret name is what a record's own settings address material by, so two accounts declaring one name leave the
    /// second unaddressable. It is a separate rule from whether a reference resolves, and this is the test that proves
    /// it rather than reaching the same refusal by a different route.
    /// </summary>
    [Fact]
    public async Task AddMailAccountAsync_AMailboxNamingASecretTheRecordAlreadyDeclares_IsRefusedBeforeItIsCommitted()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(SyntheticMailUser.Deployment, Declaring("primary"), version: 1);

        // Act
        var outcome = await harness.Records.AddMailAccountAsync(
            SyntheticMailUser.Deployment,
            AccountNamingTheSecret("archive", "primary-password"),
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
        Assert.Contains(
            outcome.Messages,
            message => message.Contains("already carries this name", StringComparison.Ordinal));
        await harness.Store.DidNotReceiveWithAnyArgs()
            .CommitAsync(default, default!, default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The record moved while the candidate was being judged. Which of that and an erasure it was is settled by reading
    /// rather than assumed, because the statement distinguishes neither.
    /// </summary>
    [Fact]
    public async Task AddMailAccountAsync_ARecordThatMovedWhileTheCandidateWasJudged_IsRefusedWithTheVersionNowInForce()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(SyntheticMailUser.Deployment, LanguageOnlyRecord, version: 1);
        harness.Store.CommitAsync(
                SyntheticMailUser.Deployment,
                Arg.Any<string>(),
                Arg.Any<MailUserEndpointAccess>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>())
            .Returns((long?)null);

        // Act
        var outcome = await harness.Records.AddMailAccountAsync(
            SyntheticMailUser.Deployment,
            Account("archive"),
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationVersionSuperseded, outcome!.Refusal);
    }

    /// <summary>
    /// Reported as a refusal rather than as nothing to change, because answering that the record is fine would leave
    /// somebody believing a mailbox had stopped being synchronized.
    /// </summary>
    [Fact]
    public async Task RemoveMailAccountAsync_AnIdentifierTheRecordDoesNotDeclare_IsRefusedRatherThanReportedAsSettled()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(SyntheticMailUser.Deployment, Declaring("primary"), version: 1);

        // Act
        var outcome = await harness.Records.RemoveMailAccountAsync(
            SyntheticMailUser.Deployment,
            "archive",
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
        Assert.False(outcome.IsSettled);
    }

    [Fact]
    public async Task RemoveMailAccountAsync_AnIdentifierTheRecordDeclares_CommitsTheRecordWithoutIt()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(SyntheticMailUser.Deployment, Declaring("primary", "archive"), version: 1);

        // Act
        var outcome = await harness.Records.RemoveMailAccountAsync(
            SyntheticMailUser.Deployment,
            "archive",
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
        await harness.Store.Received(1).CommitAsync(
            SyntheticMailUser.Deployment,
            Arg.Is<string>(candidate => !candidate!.Contains("archive", StringComparison.Ordinal)),
            Arg.Any<MailUserEndpointAccess>(),
            1,
            Arg.Any<CancellationToken>());
    }

    /// <summary>A user's own write resolves them from the principal, so a user may withdraw one of their own mailboxes and nobody else's.</summary>
    [Fact]
    public async Task RemoveOwnMailAccountAsync_AUserSignedIn_ComposesTheChangeOverTheirOwnRecord()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.MailAccountsWrite, actingFor: SyntheticMailUser.Deployment);
        harness.Holding(SyntheticMailUser.Deployment, Declaring("primary", "archive"), version: 1);

        // Act
        var outcome = await harness.Records.RemoveOwnMailAccountAsync(
            "archive",
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
        await harness.Store.Received(1).CommitAsync(
            SyntheticMailUser.Deployment,
            Arg.Any<string>(),
            Arg.Any<MailUserEndpointAccess>(),
            1,
            Arg.Any<CancellationToken>());
    }

    /// <summary>The switch is a key of the record, so writing one lands in the document and the commit carries both onto the row — the one left out as the record already stated it.</summary>
    [Fact]
    public async Task SetEndpointAccessAsync_OneSwitchNamed_WritesItIntoTheRecordAndCarriesBothOntoTheRow()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(SyntheticMailUser.Deployment, LanguageOnlyRecord, version: 3);

        // Act
        var written = await harness.Records.SetEndpointAccessAsync(
            SyntheticMailUser.Deployment,
            mcpEndpoint: false,
            clientEndpoint: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(written!.Outcome.IsCommitted);
        Assert.Equal(new MailUserEndpointAccess(McpEndpoint: false, ClientEndpoint: true), written.EndpointAccess);
        await harness.Store.Received(1).CommitAsync(
            SyntheticMailUser.Deployment,
            Arg.Is<string>(candidate =>
                candidate!.Contains("\"McpEndpoint\":\"false\"", StringComparison.Ordinal)
                && !candidate.Contains("ClientEndpoint", StringComparison.Ordinal)),
            new MailUserEndpointAccess(McpEndpoint: false, ClientEndpoint: true),
            3,
            Arg.Any<CancellationToken>());
    }

    /// <summary>A switch already where it was asked to be spends no version, so an editor open over the record is not superseded by a write that changed nothing.</summary>
    [Fact]
    public async Task SetEndpointAccessAsync_TheSwitchesTheRecordAlreadyStates_WritesNothing()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(
            SyntheticMailUser.Deployment,
            """{"Language":"English","EndpointAccess":{"ClientEndpoint":"false"}}""",
            version: 2);

        // Act
        var written = await harness.Records.SetEndpointAccessAsync(
            SyntheticMailUser.Deployment,
            mcpEndpoint: true,
            clientEndpoint: false,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(written!.Outcome.IsSettled);
        Assert.False(written.Outcome.IsCommitted);
        Assert.Equal(new MailUserEndpointAccess(McpEndpoint: true, ClientEndpoint: false), written.EndpointAccess);
        await harness.Store.DidNotReceiveWithAnyArgs()
            .CommitAsync(default, default!, default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>Deciding where the deployment serves somebody is the configuration write, so a caller that may only read is refused before the record is read.</summary>
    [Fact]
    public async Task SetEndpointAccessAsync_ACallerHoldingOnlyTheAdministrativeRead_IsRefused()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminRead);

        // Act & Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => harness.Records.SetEndpointAccessAsync(
                SyntheticMailUser.Deployment,
                mcpEndpoint: false,
                clientEndpoint: null,
                TestContext.Current.CancellationToken));
    }

    /// <summary>A row that is not a document of settings is refused with a sentence the administrator can act on, rather than failing the route.</summary>
    [Fact]
    public async Task SetEndpointAccessAsync_ARecordThatIsNotADocumentOfSettings_IsRefusedWithoutWriting()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(SyntheticMailUser.Deployment, "not a document", version: 4);

        // Act
        var written = await harness.Records.SetEndpointAccessAsync(
            SyntheticMailUser.Deployment,
            mcpEndpoint: false,
            clientEndpoint: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, written!.Outcome.Refusal);
        Assert.Contains("not a document of settings", Assert.Single(written.Outcome.Messages), StringComparison.Ordinal);
        await harness.Store.DidNotReceiveWithAnyArgs()
            .CommitAsync(default, default!, default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>An administrator editing the whole record reaches the switches too, and what the saved record states is what the row is given.</summary>
    [Fact]
    public async Task ApplyRecordAsync_ARecordSavedKeepingTheUserOffTheClientEndpoint_CarriesTheSwitchOntoTheRow()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(SyntheticMailUser.Deployment, LanguageOnlyRecord, version: 5);

        // Act
        var outcome = await harness.Records.ApplyRecordAsync(
            SyntheticMailUser.Deployment,
            """{"Language":"English","EndpointAccess":{"ClientEndpoint":false}}""",
            expectedVersion: 5,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
        await harness.Store.Received(1).CommitAsync(
            SyntheticMailUser.Deployment,
            Arg.Any<string>(),
            new MailUserEndpointAccess(McpEndpoint: true, ClientEndpoint: false),
            5,
            Arg.Any<CancellationToken>());
    }

    /// <summary>Which endpoints somebody is served on is the deployment's decision about them, so a user saving their own record cannot turn a switch back on.</summary>
    [Fact]
    public async Task ApplyOwnRecordAsync_ARecordMovingTheUsersOwnSwitch_IsRefusedWithoutWriting()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.MailAccountsWrite, actingFor: SyntheticMailUser.Deployment);
        harness.Holding(
            SyntheticMailUser.Deployment,
            """{"Language":"English","EndpointAccess":{"McpEndpoint":"false"}}""",
            version: 1);

        // Act
        var outcome = await harness.Records.ApplyOwnRecordAsync(
            """{"Language":"English","EndpointAccess":{"McpEndpoint":"true"}}""",
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
        await harness.Store.DidNotReceiveWithAnyArgs()
            .CommitAsync(default, default!, default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>A switch an administrator set travels through the user's own save unchanged, so keeping somebody off one endpoint does not lock them out of editing the rest of their record.</summary>
    [Fact]
    public async Task ApplyOwnRecordAsync_ARecordCarryingTheSwitchesAsTheyStand_CommitsWithThemUnchanged()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.MailAccountsWrite, actingFor: SyntheticMailUser.Deployment);
        harness.Holding(
            SyntheticMailUser.Deployment,
            """{"Language":"English","EndpointAccess":{"McpEndpoint":"false"}}""",
            version: 1);

        // Act
        var outcome = await harness.Records.ApplyOwnRecordAsync(
            """{"Language":"english","EndpointAccess":{"McpEndpoint":"false"}}""",
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
        await harness.Store.Received(1).CommitAsync(
            SyntheticMailUser.Deployment,
            Arg.Any<string>(),
            new MailUserEndpointAccess(McpEndpoint: false, ClientEndpoint: true),
            1,
            Arg.Any<CancellationToken>());
    }

    /// <summary>A user's grant is not an administrator's, so their entry point refuses a caller holding only the administrative one.</summary>
    [Fact]
    public async Task AddOwnMailAccountAsync_ACallerHoldingOnlyTheAdministrativeWrite_IsRefused()
    {
        // Arrange
        var harness = new RecordHarness(
            MailFathomPermission.AdminConfigurationWrite,
            actingFor: SyntheticMailUser.Deployment);

        // Act & Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => harness.Records.AddOwnMailAccountAsync(
                Account("archive"),
                expectedVersion: 1,
                TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A saved record becomes keyed changes rather than replacing the document wholesale, so a value left at the
    /// redaction marker leaves the reference beneath it exactly as it was rather than persisting the marker over
    /// somebody's credential.
    /// </summary>
    [Fact]
    public async Task ApplyRecordAsync_ARecordSavedWithAValueLeftAtTheMarker_LeavesTheReferenceBeneathItUntouched()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(SyntheticMailUser.Deployment, Declaring("primary", "archive"), version: 1);

        var saved = $$"""
                      {
                        "Language": "English",
                        "MailAccounts": [
                          {{AccountSaved("primary", "imap.example.test", SettingRedaction.Marker)}},
                          {{AccountSaved("archive", "imap2.example.test", "file:/run/secrets/archive-password")}}
                        ]
                      }
                      """;

        // Act
        var outcome = await harness.Records.ApplyRecordAsync(
            SyntheticMailUser.Deployment,
            saved,
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
        await harness.Store.Received(1).CommitAsync(
            SyntheticMailUser.Deployment,
            Arg.Is<string>(candidate =>
                candidate!.Contains("/run/secrets/primary-password", StringComparison.Ordinal)
                && !candidate.Contains(SettingRedaction.Marker, StringComparison.Ordinal)),
            Arg.Any<MailUserEndpointAccess>(),
            1,
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A marker stands for whatever the position beneath it held, and a position moves when an element is added,
    /// removed, or renamed — so a save that both leaves a marker and changes the element around it is refused rather
    /// than resolved against a position that no longer means what it did.
    /// </summary>
    [Fact]
    public async Task ApplyRecordAsync_AMarkerLeftInPlaceWhileTheElementAroundItChanged_IsRefusedNamingTheNarrowerChange()
    {
        // Arrange
        var harness = new RecordHarness(
            MailFathomPermission.AdminConfigurationWrite,
            alsoGranted: MailFathomPermission.AdminRead);
        harness.Holding(SyntheticMailUser.Deployment, Declaring("primary"), version: 1);

        var reading = await harness.Records.ReadRecordAsync(
            SyntheticMailUser.Deployment,
            TestContext.Current.CancellationToken);

        // Act
        var outcome = await harness.Records.ApplyRecordAsync(
            SyntheticMailUser.Deployment,
            reading!.Json.Replace("\"primary\"", "\"renamed\"", StringComparison.Ordinal),
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
        Assert.Contains("mfctl user account add", Assert.Single(outcome.Messages), StringComparison.Ordinal);
        await harness.Store.DidNotReceiveWithAnyArgs()
            .CommitAsync(default, default!, default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The write route is where a mailbox user's own record actually arrives, and it is the one path a narrowing has
    /// to be refused on: a record already held is composed to the stricter answer instead, so nothing downstream would
    /// report this. A candidate switching off a scanner the deployment requires is refused here, naming the deployment
    /// setting it would narrow rather than quoting anything out of the record.
    /// </summary>
    [Fact]
    public async Task ApplyRecordAsync_ACandidateSwitchingOffAScannerTheDeploymentRequires_IsRefused()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.Secrets.Enabled = true;

        var harness = new RecordHarness(
            MailFathomPermission.AdminConfigurationWrite,
            alsoGranted: MailFathomPermission.AdminRead,
            scanning: deployment);
        harness.Holding(SyntheticMailUser.Deployment, Declaring("primary"), version: 1);

        // Act
        var outcome = await harness.Records.ApplyRecordAsync(
            SyntheticMailUser.Deployment,
            """{ "Language": "English", "MailAccounts": [], "SensitiveContent": { "Secrets": { "Enabled": false } } }""",
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
        Assert.Contains(
            "SensitiveContent:Secrets:Enabled",
            Assert.Single(outcome.Messages),
            StringComparison.Ordinal);
        await harness.Store.DidNotReceiveWithAnyArgs()
            .CommitAsync(default, default!, default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>A save that composes what the record already carries spends no version, and says so rather than reporting a commit.</summary>
    [Fact]
    public async Task ApplyRecordAsync_ARecordSavedExactlyAsItWasRead_ChangesNothingAndSpendsNoVersion()
    {
        // Arrange
        var harness = new RecordHarness(
            MailFathomPermission.AdminConfigurationWrite,
            alsoGranted: MailFathomPermission.AdminRead);
        harness.Holding(SyntheticMailUser.Deployment, Declaring("primary"), version: 5);

        var reading = await harness.Records.ReadRecordAsync(
            SyntheticMailUser.Deployment,
            TestContext.Current.CancellationToken);

        // Act
        var outcome = await harness.Records.ApplyRecordAsync(
            SyntheticMailUser.Deployment,
            reading!.Json,
            expectedVersion: 5,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsSettled);
        Assert.False(outcome.IsCommitted);
        Assert.Equal(5, outcome.Version);
        await harness.Store.DidNotReceiveWithAnyArgs().CommitAsync(default, default!, default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A reference that already reached nothing before the edit is not what the edit is about, so an unrelated setting
    /// saved beside it commits, the reference stays exactly as the row held it, and the problem is still reported —
    /// as one the record already carried, since the next start refuses it either way.
    /// </summary>
    [Fact]
    public async Task ApplyRecordAsync_AnUnrelatedSettingBesideAReferenceThatAlreadyReachedNothing_CommitsAndReportsTheReferenceAsAlreadyHeld()
    {
        // Arrange
        var harness = new RecordHarness(
            MailFathomPermission.AdminConfigurationWrite,
            alsoGranted: MailFathomPermission.AdminRead);
        harness.Holding(
            SyntheticMailUser.Deployment,
            $$"""{ "Language": "English", "MailAccounts": [ {{AccountWhoseSecretReachesNothing("primary")}} ] }""",
            version: 3);

        var reading = await harness.Records.ReadRecordAsync(
            SyntheticMailUser.Deployment,
            TestContext.Current.CancellationToken);

        var saved = reading!.Json.Replace(
            "\"Language\": \"English\"",
            "\"Language\": \"English\", \"SpamClassification\": { \"Enabled\": true }",
            StringComparison.Ordinal);

        // Act
        var outcome = await harness.Records.ApplyRecordAsync(
            SyntheticMailUser.Deployment,
            saved,
            expectedVersion: 3,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
        Assert.Contains(
            "already carried this before the change",
            Assert.Single(outcome.Messages),
            StringComparison.Ordinal);
        await harness.Store.Received(1).CommitAsync(
            SyntheticMailUser.Deployment,
            Arg.Is<string>(candidate =>
                candidate!.Contains($"{RegisteredSchemeSecretReferenceResolver.UnreadableTarget}/primary-password", StringComparison.Ordinal)
                && candidate.Contains("SpamClassification", StringComparison.Ordinal)
                && !candidate.Contains(SettingRedaction.Marker, StringComparison.Ordinal)),
            Arg.Any<MailUserEndpointAccess>(),
            3,
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The walk's sentence names a path and a failure, never the target, so a broken reference replaced by a different
    /// broken one at the same path reads the same — and is still this write's own problem, refused rather than
    /// committed as though the record already carried it.
    /// </summary>
    [Fact]
    public async Task ApplyRecordAsync_ABrokenReferenceReplacedByAnotherBrokenOne_IsRefusedRatherThanReportedAsAlreadyHeld()
    {
        // Arrange
        var standing = $$"""{ "Language": "English", "MailAccounts": [ {{AccountWhoseSecretReachesNothing("primary")}} ] }""";

        var harness = new RecordHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(SyntheticMailUser.Deployment, standing, version: 3);

        // Act
        var outcome = await harness.Records.ApplyRecordAsync(
            SyntheticMailUser.Deployment,
            standing.Replace("/primary-password", "/primary-passwrod", StringComparison.Ordinal),
            expectedVersion: 3,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
        await harness.Store.DidNotReceiveWithAnyArgs()
            .CommitAsync(default, default!, default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// What the record already carried excuses only a write that leaves the mail accounts alone: adding a second mailbox
    /// whose credential reaches nothing is refused, naming it.
    /// </summary>
    [Fact]
    public async Task AddMailAccountAsync_AnUnusableMailboxBesideOneAlreadyUnusable_IsRefusedNamingTheNewOne()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(
            SyntheticMailUser.Deployment,
            $$"""{ "Language": "English", "MailAccounts": [ {{AccountWhoseSecretReachesNothing("primary")}} ] }""",
            version: 1);

        // Act
        var outcome = await harness.Records.AddMailAccountAsync(
            SyntheticMailUser.Deployment,
            AccountWhoseSecretReachesNothing("archive"),
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
        Assert.Contains(outcome.Messages, message => message.Contains("MailAccounts:1", StringComparison.Ordinal));
        await harness.Store.DidNotReceiveWithAnyArgs()
            .CommitAsync(default, default!, default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>The buffer is what somebody typed, so every way it can be wrong is theirs to correct rather than a defect to raise.</summary>
    [Fact]
    public async Task ApplyRecordAsync_ASavedBufferThatIsNotADocumentOfSettings_IsRefusedRatherThanRaised()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(SyntheticMailUser.Deployment, LanguageOnlyRecord, version: 1);

        // Act
        var outcome = await harness.Records.ApplyRecordAsync(
            SyntheticMailUser.Deployment,
            "{ this is not a record",
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
    }

    private static ServedMailUser Serving(MailUserId user, params string[] accountIds) =>
        new(
            user,
            $"user-{user.Value:D}",
            [.. accountIds.Select(accountId => new MailSynchronizationAccountOptions
            {
                AccountId = accountId,
            })]);

    private static string AccountSaved(string accountId, string host, string secretReference) =>
        $$"""
          {
            "AccountId": "{{accountId}}",
            "DisplayName": "{{accountId}}",
            "Host": "{{host}}",
            "UserName": "mailfathom@example.test",
            "Secrets": { "Password": { "Name": "{{accountId}}-password", "SecretReference": "{{secretReference}}" } }
          }
          """;

    private static string Account(string accountId) =>
        $$"""
          {
            "AccountId": "{{accountId}}",
            "DisplayName": "{{accountId}}",
            "Host": "imap.example.test",
            "UserName": "mailfathom@example.test",
            "Secrets": { "Password": { "Name": "{{accountId}}-password", "SecretReference": "file:/run/secrets/{{accountId}}-password" } }
          }
          """;

    /// <summary>A mailbox whose credential this deployment provisioned for one user, which its own name is what says.</summary>
    private static string AccountProvisionedFor(MailUserId user, string accountId) =>
        $$"""
          {
            "AccountId": "{{accountId}}",
            "DisplayName": "{{accountId}}",
            "Host": "imap.example.test",
            "UserName": "mailfathom@example.test",
            "Secrets": { "Password": { "Name": "{{accountId}}-password", "SecretReference": "file:/run/secrets/user-{{user.Value:D}}-{{accountId}}" } }
          }
          """;

    /// <summary>A mailbox whose credential is a well-formed reference this deployment resolves to nothing.</summary>
    /// <remarks>
    /// The record binds, the scheme is one the deployment registers, the secret name is the account's own, and every
    /// other rule the write applies passes, so the walk that resolves the reference is the only thing left that can
    /// refuse it.
    /// </remarks>
    private static string AccountWhoseSecretReachesNothing(string accountId) =>
        $$"""
          {
            "AccountId": "{{accountId}}",
            "DisplayName": "{{accountId}}",
            "Host": "imap.example.test",
            "UserName": "mailfathom@example.test",
            "Secrets": { "Password": { "Name": "{{accountId}}-password", "SecretReference": "file:{{RegisteredSchemeSecretReferenceResolver.UnreadableTarget}}/{{accountId}}-password" } }
          }
          """;

    /// <summary>A mail account naming a secret an operator chose, which is how one record comes to declare a name twice.</summary>
    private static string AccountNamingTheSecret(string accountId, string secretName) =>
        $$"""
          {
            "AccountId": "{{accountId}}",
            "DisplayName": "{{accountId}}",
            "Host": "imap.example.test",
            "UserName": "mailfathom@example.test",
            "Secrets": { "Password": { "Name": "{{secretName}}", "SecretReference": "file:/run/secrets/{{accountId}}-password" } }
          }
          """;

    private static string Declaring(params string[] accountIds) =>
        $$"""{ "Language": "English", "MailAccounts": [ {{string.Join(",", accountIds.Select(Account))}} ] }""";

    /// <summary>The service over a substituted row and the real binder, so a candidate is judged the way a start judges one.</summary>
    private sealed class RecordHarness
    {
        internal RecordHarness(
            MailFathomPermission granted,
            Dictionary<string, string?>? configuration = null,
            MailUserId actingFor = default,
            MailFathomPermission alsoGranted = default,
            SensitiveContentOptions? scanning = null)
        {
            // A caller that has to read a record before saving it holds both grants, which is what an administrator
            // editing a record actually carries; the unspecified default is what a test granting one permission passes.
            MailFathomPermission[] grants = [.. new[] { granted, alsoGranted }.Where(grant => grant.IsSpecified)];

            var principals = Substitute.For<IAuthorizedPrincipalSource>();
            principals.Current.Returns(actingFor.IsSpecified
                ? AuthorizedPrincipal.CallerActingFor(actingFor, AdministratorIdentity, grants)
                : AuthorizedPrincipal.Caller(AdministratorIdentity, grants));

            this.Documents = Substitute.For<IUserSettingsDocumentReader>();
            this.Store = Substitute.For<IUserSettingsDocumentWriter>();
            this.Store.CommitAsync(
                    Arg.Any<MailUserId>(),
                    Arg.Any<string>(),
                    Arg.Any<MailUserEndpointAccess>(),
                    Arg.Any<long>(),
                    Arg.Any<CancellationToken>())
                .Returns(call => (long?)call.ArgAt<long>(3) + 1);

            // The roster is settled with somebody the tests never write for, so the default deployment reads as a
            // user nothing declares — which is the ordinary case — until a test states otherwise.
            this.ServedUsers.Resolved(
                [Serving(MailUserId.Create(new Guid("99999999-9999-9999-9999-999999999999")))]);

            var settings = new ConfigurationBuilder()
                .AddInMemoryCollection(configuration ?? [])
                .Build();

            this.Records = new UserRecordAdministration(
                new AccessAuthorization(principals),
                this.Documents,
                this.Store,
                new UserAccountDocumentBinder(
                    new PersistedSecretMaterial(DeclaredSecretScheme.Registered),
                    new FakeTimeProvider(Today),
                    Options.Create(scanning ?? new SensitiveContentOptions())),
                SecretValidation.OverRegisteredSchemes(),
                this.ServedUsers,
                this.Files,
                new ConfigurationChangeAnnouncements(
                    () => Task.FromResult(this.Backplane.Connect()),
                    new RecordingLogger<ConfigurationChangeAnnouncements>()));
        }

        internal UserRecordAdministration Records { get; }

        /// <summary>Gets the backplane a commit is announced over, which nobody hears until a test listens.</summary>
        internal InMemoryBackplane Backplane { get; } = new();

        /// <summary>Gets the stored files, which hold nothing of anybody's until a test says otherwise.</summary>
        internal IStoredFileStore Files { get; } = Substitute.For<IStoredFileStore>();

        internal IUserSettingsDocumentReader Documents { get; }

        internal IUserSettingsDocumentWriter Store { get; }

        internal ServedMailUsers ServedUsers { get; } = new();

        internal void Holding(MailUserId user, string json, long version) =>
            this.Documents.ReadAsync(user, Arg.Any<CancellationToken>())
                .Returns(new UserSettingsDocument(user, $"user-{user.Value:D}", json, version));

        internal void Roster(params ServedMailUser[] served) => this.ServedUsers.Resolved(served);
    }
}
