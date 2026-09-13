// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.StoredFiles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Failures;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.SensitiveContent;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.Configuration.UserSettings.Administration;
using MailFathom.Host.Signals;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Users;
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
        harness.Holding(SyntheticMailUser.Deployment, LanguageOnlyRecord, version: 4);

        // Act
        var reading = await harness.Records.ReadRecordAsync(
            SyntheticMailUser.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(4, reading!.Version);
        Assert.Equal(SyntheticMailUser.Deployment, reading.User);
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

    /// <summary>
    /// A record committed before the language became required still takes a switch, because the switch is all the write
    /// changes and a rule that arrived after the record was accepted must not keep its user served where they are.
    /// </summary>
    [Fact]
    public async Task SetEndpointAccessAsync_ARecordStatingNoLanguage_StillWritesTheSwitch()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(SyntheticMailUser.Deployment, "{}", version: 2);

        // Act
        var written = await harness.Records.SetEndpointAccessAsync(
            SyntheticMailUser.Deployment,
            mcpEndpoint: false,
            clientEndpoint: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(written!.Outcome.IsCommitted);
        await harness.Store.Received(1).CommitAsync(
            SyntheticMailUser.Deployment,
            Arg.Any<string>(),
            new MailUserEndpointAccess(McpEndpoint: false, ClientEndpoint: true),
            2,
            Arg.Any<CancellationToken>());
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

    /// <summary>Deleting the block is the other way a user's own save would reach both switches on, because a switch the record does not state binds as on.</summary>
    [Fact]
    public async Task ApplyOwnRecordAsync_ARecordDroppingTheEndpointAccessBlock_IsRefusedWithoutWriting()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.MailAccountsWrite, actingFor: SyntheticMailUser.Deployment);
        harness.Holding(
            SyntheticMailUser.Deployment,
            """{"Language":"English","EndpointAccess":{"McpEndpoint":"false"}}""",
            version: 1);

        // Act
        var outcome = await harness.Records.ApplyOwnRecordAsync(
            """{"Language":"English"}""",
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
        Assert.Contains(outcome.Messages, message => message.Contains("EndpointAccess", StringComparison.Ordinal));
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

    /// <summary>
    /// A mail account is a record of its own, so a saved user record that still names one is refused rather than
    /// quietly dropping what somebody typed, and the refusal says which command changes an account instead.
    /// </summary>
    [Fact]
    public async Task ApplyRecordAsync_ASavedRecordNamingMailAccounts_IsRefusedNamingTheAccountCommand()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(SyntheticMailUser.Deployment, LanguageOnlyRecord, version: 1);

        // Act
        var outcome = await harness.Records.ApplyRecordAsync(
            SyntheticMailUser.Deployment,
            """{ "Language": "English", "MailAccounts": { "0": { "DisplayName": "primary", "Host": "imap.example.test" } } }""",
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
        Assert.Contains("mfctl account edit", Assert.Single(outcome.Messages), StringComparison.Ordinal);
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
        harness.Holding(SyntheticMailUser.Deployment, LanguageOnlyRecord, version: 1);

        // Act
        var outcome = await harness.Records.ApplyRecordAsync(
            SyntheticMailUser.Deployment,
            """{ "Language": "English", "SensitiveContent": { "Secrets": { "Enabled": false } } }""",
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
        harness.Holding(SyntheticMailUser.Deployment, LanguageOnlyRecord, version: 5);

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
    /// A user's own record carries no mail account, so a write to it cannot have introduced a credential one of their
    /// accounts cannot use: an unrelated setting commits, and the account's broken reference is still reported — as one
    /// the user already carried, since the next start refuses it either way.
    /// </summary>
    [Fact]
    public async Task ApplyRecordAsync_AnUnrelatedSettingBesideAnAccountWhoseReferenceReachesNothing_CommitsAndReportsTheReferenceAsAlreadyHeld()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(SyntheticMailUser.Deployment, LanguageOnlyRecord, version: 3, MailboxWhoseSecretReachesNothing());

        // Act
        var outcome = await harness.Records.ApplyRecordAsync(
            SyntheticMailUser.Deployment,
            """{ "Language": "English", "SpamClassification": { "Enabled": true } }""",
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
                candidate!.Contains("SpamClassification", StringComparison.Ordinal)
                && !candidate.Contains("MailAccounts", StringComparison.Ordinal)),
            Arg.Any<MailUserEndpointAccess>(),
            3,
            Arg.Any<CancellationToken>());
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

    /// <summary>A mailbox assigned to the user whose credential is a well-formed reference this deployment resolves to nothing.</summary>
    /// <remarks>
    /// The account binds, the scheme is one the deployment registers, and the secret name is the account's own, so the
    /// walk that resolves the reference is the only thing left that can report it.
    /// </remarks>
    private static MailAccountRecord MailboxWhoseSecretReachesNothing() =>
        new(
            Guid.Parse("0197a3c0-0000-7000-8000-0000000000a1"),
            "primary@example.test",
            "primary",
            $$"""
              {
                "Host": "imap.example.test",
                "UserName": "mailfathom@example.test",
                "Secrets": { "Password": { "Name": "primary-password", "SecretReference": "file:{{RegisteredSchemeSecretReferenceResolver.UnreadableTarget}}/primary-password" } }
              }
              """,
            Version: 1);

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

        internal void Holding(MailUserId user, string json, long version, params MailAccountRecord[] accounts) =>
            this.Documents.ReadAsync(user, Arg.Any<CancellationToken>())
                .Returns(new UserSettingsDocument(user, $"user-{user.Value:D}", json, version) { MailAccounts = accounts });

        internal void Roster(params ServedMailUser[] served) => this.ServedUsers.Resolved(served);
    }
}
