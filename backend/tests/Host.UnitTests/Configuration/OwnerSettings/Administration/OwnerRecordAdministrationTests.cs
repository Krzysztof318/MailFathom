// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Domain.Failures;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Administration;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.OwnerSettings;
using MailFathom.Host.Configuration.OwnerSettings.Administration;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Owners;
using MailFathom.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.OwnerSettings.Administration;

/// <summary>
/// Covers what is done to one owner's own record. Two callers reach it — an administrator naming the owner, and that
/// owner naming nobody — and the pairs of entry points delegate to the same work, so the cases that matter are the
/// rules the work holds: that an owner a file still supplies is refused rather than quietly emptied, that a stale
/// version is refused before a candidate is composed, that an owner reads back a redacted record, and that an owner
/// naming somebody else has no argument to name them with.
/// </summary>
public sealed class OwnerRecordAdministrationTests
{
    private const string AdministratorIdentity = "operations";

    private const string EmptyRecord = "{}";

    private static readonly DateTimeOffset Today = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReadRecordAsync_AnOwnerThisDeploymentHolds_ReportsTheirRecordAndTheVersionAChangeIsComposedOver()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminRead);
        harness.Holding(SyntheticMailOwner.Deployment, Declaring("primary"), version: 4);

        // Act
        var reading = await harness.Records.ReadRecordAsync(
            SyntheticMailOwner.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(4, reading!.Version);
        Assert.Equal(SyntheticMailOwner.Deployment, reading.Owner);
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
        harness.Holding(SyntheticMailOwner.Deployment, Declaring("primary"), version: 1);

        // Act
        var reading = await harness.Records.ReadRecordAsync(
            SyntheticMailOwner.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain("/run/secrets/primary-password", reading!.Json, StringComparison.Ordinal);
    }

    /// <summary>
    /// An owner a configuration source still supplies holds an empty record, and reading that without being told why
    /// would look like an owner with no mailboxes rather than one whose mailboxes are in a file.
    /// </summary>
    [Fact]
    public async Task ReadRecordAsync_AnOwnerAConfigurationSourceSupplies_SaysTheirRecordIsNotWhereTheirMailboxesAre()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminRead);
        harness.Holding(SyntheticMailOwner.Deployment, EmptyRecord, version: 1);
        harness.Roster(Serving(SyntheticMailOwner.Deployment, MailOwnerAccountSource.DeploymentSection));

        // Act
        var reading = await harness.Records.ReadRecordAsync(
            SyntheticMailOwner.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(reading!.ReadFromConfiguration);
    }

    [Fact]
    public async Task ReadRecordAsync_AnOwnerThisDeploymentDoesNotHold_ReportsNothing()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminRead);

        // Act
        var reading = await harness.Records.ReadRecordAsync(
            SyntheticMailOwner.Another,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(reading);
    }

    [Fact]
    public async Task ReadRecordAsync_ACallerHoldingNoAdministrativeRead_IsRefused()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.MailRead);
        harness.Holding(SyntheticMailOwner.Deployment, EmptyRecord, version: 1);

        // Act & Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => harness.Records.ReadRecordAsync(SyntheticMailOwner.Deployment, TestContext.Current.CancellationToken));
    }

    /// <summary>An owner's own entry point resolves the owner from whoever was admitted, so no request can name another.</summary>
    [Fact]
    public async Task ReadOwnRecordAsync_AnOwnerSignedIn_ReadsTheRecordOfWhoeverWasAdmittedRatherThanOneNamedInARequest()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.MailRead, actingFor: SyntheticMailOwner.Deployment);
        harness.Holding(SyntheticMailOwner.Deployment, EmptyRecord, version: 2);

        // Act
        var reading = await harness.Records.ReadOwnRecordAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SyntheticMailOwner.Deployment, reading!.Owner);
    }

    /// <summary>A caller acting for nobody's mail on an owner-facing route is an entrypoint that never said whose record it wanted.</summary>
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
    public async Task ReadRecordAsync_AnOwnerNamingNobody_IsRefusedWithoutReachingTheStore()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminRead);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => harness.Records.ReadRecordAsync(default, TestContext.Current.CancellationToken));

        await harness.Documents.DidNotReceiveWithAnyArgs().ReadAsync(default, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The refusal this service exists for. A change written into the empty record of an owner a file still supplies
    /// would leave them served from less than the file was supplying — a mailbox that stops being synchronized because
    /// somebody edited a record nobody was reading.
    /// </summary>
    [Fact]
    public async Task AddMailAccountAsync_AnOwnerAConfigurationSourceStillSupplies_IsRefusedNamingTheAdoption()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(SyntheticMailOwner.Deployment, EmptyRecord, version: 1);
        harness.Roster(Serving(SyntheticMailOwner.Deployment, MailOwnerAccountSource.DeploymentSection));

        // Act
        var outcome = await harness.Records.AddMailAccountAsync(
            SyntheticMailOwner.Deployment,
            Account("archive"),
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.OwnerRecordReadFromConfiguration, outcome!.Refusal);
        Assert.Contains("mfctl owner adopt", Assert.Single(outcome.Messages), StringComparison.Ordinal);
        await harness.Store.DidNotReceiveWithAnyArgs().CommitAsync(default, default!, default, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// An owner the roster does not hold has no configuration section a write could be replacing, which is what makes
    /// an owner an administrator has just recorded writable at once rather than after a restart.
    /// </summary>
    [Fact]
    public async Task AddMailAccountAsync_AnOwnerProvisionedAfterTheRosterWasSettled_IsAnOrdinaryWrite()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(SyntheticMailOwner.Another, EmptyRecord, version: 1);
        harness.Roster(Serving(SyntheticMailOwner.Deployment, MailOwnerAccountSource.DeploymentSection));

        // Act
        var outcome = await harness.Records.AddMailAccountAsync(
            SyntheticMailOwner.Another,
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
        harness.Holding(SyntheticMailOwner.Deployment, EmptyRecord, version: 7);

        // Act
        var outcome = await harness.Records.AddMailAccountAsync(
            SyntheticMailOwner.Deployment,
            Account("archive"),
            expectedVersion: 4,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationVersionSuperseded, outcome!.Refusal);
        Assert.Equal(7, outcome.Version);
        await harness.Store.DidNotReceiveWithAnyArgs().CommitAsync(default, default!, default, TestContext.Current.CancellationToken);
    }

    /// <summary>The candidate is put through the same binder a start reads a record with, so what a write accepts is what the next start would read.</summary>
    [Fact]
    public async Task AddMailAccountAsync_ADeclarationTheBinderRefuses_IsRefusedWithWhatHasToChange()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(SyntheticMailOwner.Deployment, EmptyRecord, version: 1);

        // Act
        var outcome = await harness.Records.AddMailAccountAsync(
            SyntheticMailOwner.Deployment,
            """{"AccountId":"archive"}""",
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
        Assert.NotEmpty(outcome.Messages);
        await harness.Store.DidNotReceiveWithAnyArgs().CommitAsync(default, default!, default, TestContext.Current.CancellationToken);
    }

    /// <summary>Anything but an object of that account's settings is a caller sending the wrong thing, and the parser's own message names which — and names no value.</summary>
    [Fact]
    public async Task AddMailAccountAsync_ADeclarationThatIsNotAJsonObject_IsRefusedRatherThanRaised()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(SyntheticMailOwner.Deployment, EmptyRecord, version: 1);

        // Act
        var outcome = await harness.Records.AddMailAccountAsync(
            SyntheticMailOwner.Deployment,
            "not json",
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
    }

    /// <summary>
    /// A mail account belongs to its owner, but this release resolves an account's settings by its identifier alone, so
    /// a name two owners share would reach whichever of the two the lookup met first.
    /// </summary>
    [Fact]
    public async Task AddMailAccountAsync_AnAccountNameAnotherServedOwnerAnswersTo_IsRefused()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(SyntheticMailOwner.Deployment, EmptyRecord, version: 1);
        harness.Roster(
            Serving(SyntheticMailOwner.Deployment, MailOwnerAccountSource.OwnerDocument),
            Serving(SyntheticMailOwner.Another, MailOwnerAccountSource.OwnerDeclaration, "shared-name"));

        // Act
        var outcome = await harness.Records.AddMailAccountAsync(
            SyntheticMailOwner.Deployment,
            Account("shared-name"),
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
        Assert.Contains("shared-name", Assert.Single(outcome.Messages), StringComparison.Ordinal);
    }

    /// <summary>An owner's record is theirs, so a name that owner already answers to is a collision the naming rules refuse rather than a deployment-wide one.</summary>
    [Fact]
    public async Task AddMailAccountAsync_AnAccountNameTheSameOwnerAlreadyDeclares_IsRefusedAsTheirOwnCollision()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(SyntheticMailOwner.Deployment, Declaring("primary"), version: 1);

        // Act
        var outcome = await harness.Records.AddMailAccountAsync(
            SyntheticMailOwner.Deployment,
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
        harness.Holding(SyntheticMailOwner.Deployment, EmptyRecord, version: 3);

        // Act
        var outcome = await harness.Records.AddMailAccountAsync(
            SyntheticMailOwner.Deployment,
            Account("archive"),
            expectedVersion: 3,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
        await harness.Store.Received(1).CommitAsync(
            SyntheticMailOwner.Deployment,
            Arg.Is<string>(candidate => candidate!.Contains("archive", StringComparison.Ordinal)),
            3,
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A secret reference is a path into whatever this deployment can read — a mounted file, a credential, an
    /// environment variable — and the server the account names is the owner's own. So a reference an owner wrote would
    /// hand them whatever stands behind it, on a mailbox they control, and the only caller who may introduce one is
    /// whoever administers the deployment.
    /// </summary>
    [Fact]
    public async Task AddOwnMailAccountAsync_AMailboxNamingACredentialTheirRecordDoesNotCarry_IsRefused()
    {
        // Arrange
        var harness = new RecordHarness(
            MailFathomPermission.MailAccountsWrite,
            actingFor: SyntheticMailOwner.Deployment);
        harness.Holding(SyntheticMailOwner.Deployment, Declaring("primary"), version: 1);

        // Act
        var outcome = await harness.Records.AddOwnMailAccountAsync(
            Account("archive"),
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
        Assert.Contains(
            "mfctl owner account add",
            Assert.Single(outcome.Messages),
            StringComparison.Ordinal);
        await harness.Store.DidNotReceiveWithAnyArgs()
            .CommitAsync(default, default!, default, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// What an owner may name is material this deployment provisioned for them, and the operator says which that is by
    /// naming it after the person it belongs to. Without this the rule would refuse every mailbox an owner declares,
    /// which is the whole of what the client's own record surface is for.
    /// </summary>
    [Fact]
    public async Task AddOwnMailAccountAsync_AMailboxNamingMaterialProvisionedForThem_IsAnOrdinaryWrite()
    {
        // Arrange
        var harness = new RecordHarness(
            MailFathomPermission.MailAccountsWrite,
            actingFor: SyntheticMailOwner.Deployment);
        harness.Holding(SyntheticMailOwner.Deployment, EmptyRecord, version: 1);

        // Act
        var outcome = await harness.Records.AddOwnMailAccountAsync(
            AccountProvisionedFor(SyntheticMailOwner.Deployment, "archive"),
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
    }

    /// <summary>
    /// The bound is the name of the material rather than where it is kept, because that is the part a path written in
    /// front of it cannot rewrite — so an owner naming another owner's credential is refused however they spell the
    /// way to it.
    /// </summary>
    [Fact]
    public async Task AddOwnMailAccountAsync_AMailboxNamingAnotherOwnersCredential_IsRefused()
    {
        // Arrange
        var harness = new RecordHarness(
            MailFathomPermission.MailAccountsWrite,
            actingFor: SyntheticMailOwner.Deployment);
        harness.Holding(SyntheticMailOwner.Deployment, EmptyRecord, version: 1);

        // Act
        var outcome = await harness.Records.AddOwnMailAccountAsync(
            AccountProvisionedFor(SyntheticMailOwner.Another, "archive"),
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
        await harness.Store.DidNotReceiveWithAnyArgs()
            .CommitAsync(default, default!, default, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The rule reads which references a record carries rather than which path each sits at, so an owner withdrawing
    /// the first of two mailboxes is not refused over the credential that moved up an index behind it. Comparing per
    /// path would refuse every withdrawal but the last one.
    /// </summary>
    [Fact]
    public async Task RemoveOwnMailAccountAsync_TheFirstOfTwoMailboxes_IsNotRefusedOverTheCredentialThatMovedUp()
    {
        // Arrange
        var harness = new RecordHarness(
            MailFathomPermission.MailAccountsWrite,
            actingFor: SyntheticMailOwner.Deployment);
        harness.Holding(SyntheticMailOwner.Deployment, Declaring("primary", "archive"), version: 1);

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
        harness.Holding(SyntheticMailOwner.Deployment, Declaring("primary"), version: 1);

        // Act
        var outcome = await harness.Records.AddMailAccountAsync(
            SyntheticMailOwner.Deployment,
            AccountNamingTheSecret("archive", "primary-password"),
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
        Assert.NotEmpty(outcome.Messages);
        await harness.Store.DidNotReceiveWithAnyArgs()
            .CommitAsync(default, default!, default, TestContext.Current.CancellationToken);
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
        harness.Holding(SyntheticMailOwner.Deployment, EmptyRecord, version: 1);
        harness.Store.CommitAsync(
                SyntheticMailOwner.Deployment,
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>())
            .Returns((long?)null);

        // Act
        var outcome = await harness.Records.AddMailAccountAsync(
            SyntheticMailOwner.Deployment,
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
        harness.Holding(SyntheticMailOwner.Deployment, Declaring("primary"), version: 1);

        // Act
        var outcome = await harness.Records.RemoveMailAccountAsync(
            SyntheticMailOwner.Deployment,
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
        harness.Holding(SyntheticMailOwner.Deployment, Declaring("primary", "archive"), version: 1);

        // Act
        var outcome = await harness.Records.RemoveMailAccountAsync(
            SyntheticMailOwner.Deployment,
            "archive",
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
        await harness.Store.Received(1).CommitAsync(
            SyntheticMailOwner.Deployment,
            Arg.Is<string>(candidate => !candidate!.Contains("archive", StringComparison.Ordinal)),
            1,
            Arg.Any<CancellationToken>());
    }

    /// <summary>An owner's own write resolves them from the principal, so an owner may withdraw one of their own mailboxes and nobody else's.</summary>
    [Fact]
    public async Task RemoveOwnMailAccountAsync_AnOwnerSignedIn_ComposesTheChangeOverTheirOwnRecord()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.MailAccountsWrite, actingFor: SyntheticMailOwner.Deployment);
        harness.Holding(SyntheticMailOwner.Deployment, Declaring("primary", "archive"), version: 1);

        // Act
        var outcome = await harness.Records.RemoveOwnMailAccountAsync(
            "archive",
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
        await harness.Store.Received(1).CommitAsync(
            SyntheticMailOwner.Deployment,
            Arg.Any<string>(),
            1,
            Arg.Any<CancellationToken>());
    }

    /// <summary>An owner's grant is not an administrator's, so their entry point refuses a caller holding only the administrative one.</summary>
    [Fact]
    public async Task AddOwnMailAccountAsync_ACallerHoldingOnlyTheAdministrativeWrite_IsRefused()
    {
        // Arrange
        var harness = new RecordHarness(
            MailFathomPermission.AdminConfigurationWrite,
            actingFor: SyntheticMailOwner.Deployment);

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
        harness.Holding(SyntheticMailOwner.Deployment, Declaring("primary", "archive"), version: 1);

        var saved = $$"""
                      {
                        "MailAccounts": [
                          {{AccountSaved("primary", "imap.example.test", SettingRedaction.Marker)}},
                          {{AccountSaved("archive", "imap2.example.test", "file:/run/secrets/archive-password")}}
                        ]
                      }
                      """;

        // Act
        var outcome = await harness.Records.ApplyRecordAsync(
            SyntheticMailOwner.Deployment,
            saved,
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
        await harness.Store.Received(1).CommitAsync(
            SyntheticMailOwner.Deployment,
            Arg.Is<string>(candidate =>
                candidate!.Contains("/run/secrets/primary-password", StringComparison.Ordinal)
                && !candidate.Contains(SettingRedaction.Marker, StringComparison.Ordinal)),
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
        harness.Holding(SyntheticMailOwner.Deployment, Declaring("primary"), version: 1);

        var reading = await harness.Records.ReadRecordAsync(
            SyntheticMailOwner.Deployment,
            TestContext.Current.CancellationToken);

        // Act
        var outcome = await harness.Records.ApplyRecordAsync(
            SyntheticMailOwner.Deployment,
            reading!.Json.Replace("\"primary\"", "\"renamed\"", StringComparison.Ordinal),
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
        Assert.Contains("mfctl owner account add", Assert.Single(outcome.Messages), StringComparison.Ordinal);
        await harness.Store.DidNotReceiveWithAnyArgs()
            .CommitAsync(default, default!, default, TestContext.Current.CancellationToken);
    }

    /// <summary>A save that composes what the record already carries spends no version, and says so rather than reporting a commit.</summary>
    [Fact]
    public async Task ApplyRecordAsync_ARecordSavedExactlyAsItWasRead_ChangesNothingAndSpendsNoVersion()
    {
        // Arrange
        var harness = new RecordHarness(
            MailFathomPermission.AdminConfigurationWrite,
            alsoGranted: MailFathomPermission.AdminRead);
        harness.Holding(SyntheticMailOwner.Deployment, Declaring("primary"), version: 5);

        var reading = await harness.Records.ReadRecordAsync(
            SyntheticMailOwner.Deployment,
            TestContext.Current.CancellationToken);

        // Act
        var outcome = await harness.Records.ApplyRecordAsync(
            SyntheticMailOwner.Deployment,
            reading!.Json,
            expectedVersion: 5,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsSettled);
        Assert.False(outcome.IsCommitted);
        Assert.Equal(5, outcome.Version);
        await harness.Store.DidNotReceiveWithAnyArgs().CommitAsync(default, default!, default, TestContext.Current.CancellationToken);
    }

    /// <summary>The buffer is what somebody typed, so every way it can be wrong is theirs to correct rather than a defect to raise.</summary>
    [Fact]
    public async Task ApplyRecordAsync_ASavedBufferThatIsNotADocumentOfSettings_IsRefusedRatherThanRaised()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(SyntheticMailOwner.Deployment, EmptyRecord, version: 1);

        // Act
        var outcome = await harness.Records.ApplyRecordAsync(
            SyntheticMailOwner.Deployment,
            "{ this is not a record",
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
    }

    /// <summary>The preview is what an operator confirms an adoption against, so it names the section and the mailboxes the move would materialize.</summary>
    [Fact]
    public async Task ReadAdoptableAsync_AnOwnerServedFromTheDeploymentSection_NamesTheSectionAndTheMailboxesItSupplies()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminRead, DeploymentSectionDeclaring("configured"));
        harness.Holding(SyntheticMailOwner.Deployment, EmptyRecord, version: 1);
        harness.Roster(Serving(SyntheticMailOwner.Deployment, MailOwnerAccountSource.DeploymentSection));

        // Act
        var preview = await harness.Records.ReadAdoptableAsync(
            SyntheticMailOwner.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(preview!.HasSomethingToAdopt);
        Assert.Equal("MailSynchronization:Accounts", preview.ConfigurationPath);
        Assert.Equal(["configured"], preview.MailAccounts.Select(account => account.AccountId));
    }

    /// <summary>An owner whose record is already their own has nothing to adopt, which is a preview offering nothing rather than an absent one.</summary>
    [Fact]
    public async Task ReadAdoptableAsync_AnOwnerWhoseRecordIsAlreadyTheirOwn_OffersNothingToAdopt()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminRead, DeploymentSectionDeclaring("configured"));
        harness.Holding(SyntheticMailOwner.Deployment, EmptyRecord, version: 1);
        harness.Roster(Serving(SyntheticMailOwner.Deployment, MailOwnerAccountSource.OwnerDocument));

        // Act
        var preview = await harness.Records.ReadAdoptableAsync(
            SyntheticMailOwner.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(preview!.HasSomethingToAdopt);
        Assert.Null(preview.ConfigurationPath);
    }

    /// <summary>The one act that moves a decision from a file into the database, and the only thing in MailFathom that ever does it.</summary>
    [Fact]
    public async Task AdoptAsync_AnOwnerServedFromTheDeploymentSection_CommitsTheirConfiguredMailboxesIntoTheirRecord()
    {
        // Arrange
        var harness = new RecordHarness(
            MailFathomPermission.AdminConfigurationWrite,
            DeploymentSectionDeclaring("configured"));
        harness.Holding(SyntheticMailOwner.Deployment, EmptyRecord, version: 1);
        harness.Roster(Serving(SyntheticMailOwner.Deployment, MailOwnerAccountSource.DeploymentSection));

        // Act
        var outcome = await harness.Records.AdoptAsync(
            SyntheticMailOwner.Deployment,
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
        await harness.Store.Received(1).CommitAsync(
            SyntheticMailOwner.Deployment,
            Arg.Is<string>(candidate => candidate!.Contains("configured", StringComparison.Ordinal)),
            1,
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The one composition here whose input nobody in the request authored: the row is what the patch is applied to, so
    /// a row that is not a document of settings is reported as a refusal naming what could not be read rather than left
    /// to fault. What an operator needs either way is the same — which document, and that nothing was written.
    /// </summary>
    [Fact]
    public async Task AdoptAsync_ARowThatIsNotADocumentOfSettings_IsRefusedWithoutCommitting()
    {
        // Arrange
        var harness = new RecordHarness(
            MailFathomPermission.AdminConfigurationWrite,
            DeploymentSectionDeclaring("configured"));
        harness.Holding(SyntheticMailOwner.Deployment, "[]", version: 1);
        harness.Roster(Serving(SyntheticMailOwner.Deployment, MailOwnerAccountSource.DeploymentSection));

        // Act
        var outcome = await harness.Records.AdoptAsync(
            SyntheticMailOwner.Deployment,
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome!.Refusal);
        Assert.Contains(
            "not a document of settings this deployment can read",
            Assert.Single(outcome.Messages),
            StringComparison.Ordinal);
        await harness.Store.DidNotReceiveWithAnyArgs()
            .CommitAsync(default, default!, default, TestContext.Current.CancellationToken);
    }

    /// <summary>Adoption is the one act permitted to open the record of an owner a configuration source supplies, which is what makes it the way out of that state.</summary>
    [Fact]
    public async Task AdoptAsync_AnOwnerServedFromTheirOwnDeclaration_IsNotRefusedTheWayAnOrdinaryWriteIs()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(SyntheticMailOwner.Deployment, EmptyRecord, version: 1);
        harness.Roster(Serving(SyntheticMailOwner.Deployment, MailOwnerAccountSource.OwnerDeclaration));

        // Act
        var outcome = await harness.Records.AdoptAsync(
            SyntheticMailOwner.Deployment,
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(MailFathomErrorCode.OwnerRecordReadFromConfiguration, outcome!.Refusal);
    }

    /// <summary>An owner already reading their own record has nothing to move, and saying so is not a refusal.</summary>
    [Fact]
    public async Task AdoptAsync_AnOwnerWhoseMailboxesAlreadyComeFromTheirOwnRecord_ChangesNothing()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(SyntheticMailOwner.Deployment, EmptyRecord, version: 2);
        harness.Roster(Serving(SyntheticMailOwner.Deployment, MailOwnerAccountSource.OwnerDocument));

        // Act
        var outcome = await harness.Records.AdoptAsync(
            SyntheticMailOwner.Deployment,
            expectedVersion: 2,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsSettled);
        Assert.False(outcome.IsCommitted);
        await harness.Store.DidNotReceiveWithAnyArgs().CommitAsync(default, default!, default, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// It commits even where the file supplies nothing, because the marker beside the document is what tells the next
    /// start to stop applying the configured section to this owner.
    /// </summary>
    [Fact]
    public async Task AdoptAsync_AnOwnerWhoseConfigurationSectionDeclaresNoMailbox_StillCommitsSoTheNextStartStopsReadingTheSection()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(SyntheticMailOwner.Deployment, EmptyRecord, version: 1);
        harness.Roster(Serving(SyntheticMailOwner.Deployment, MailOwnerAccountSource.DeploymentSection));

        // Act
        var outcome = await harness.Records.AdoptAsync(
            SyntheticMailOwner.Deployment,
            expectedVersion: 1,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.IsCommitted);
    }

    [Fact]
    public async Task AdoptAsync_ACallerHoldingOnlyTheAdministrativeRead_IsRefused()
    {
        // Arrange
        var harness = new RecordHarness(MailFathomPermission.AdminRead);

        // Act & Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => harness.Records.AdoptAsync(
                SyntheticMailOwner.Deployment,
                expectedVersion: 1,
                TestContext.Current.CancellationToken));
    }

    private static ServedMailOwner Serving(
        MailOwnerId owner,
        MailOwnerAccountSource source,
        params string[] accountIds) =>
        new(
            owner,
            $"owner-{owner.Value:D}",
            source,
            [.. accountIds.Select(accountId => new MailSynchronizationAccountOptions
            {
                AccountId = accountId,
            })]);

    private static Dictionary<string, string?> DeploymentSectionDeclaring(string accountId) => new()
    {
        ["MailSynchronization:Accounts:0:AccountId"] = accountId,
        ["MailSynchronization:Accounts:0:DisplayName"] = accountId,
        ["MailSynchronization:Accounts:0:Host"] = "imap.example.test",
        ["MailSynchronization:Accounts:0:UserName"] = "mailfathom@example.test",
        ["MailSynchronization:Accounts:0:Secrets:Password:Name"] = $"{accountId}-password",
        ["MailSynchronization:Accounts:0:Secrets:Password:SecretReference"] = $"file:/run/secrets/{accountId}-password",
    };

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

    /// <summary>A mailbox whose credential this deployment provisioned for one owner, which its own name is what says.</summary>
    private static string AccountProvisionedFor(MailOwnerId owner, string accountId) =>
        $$"""
          {
            "AccountId": "{{accountId}}",
            "DisplayName": "{{accountId}}",
            "Host": "imap.example.test",
            "UserName": "mailfathom@example.test",
            "Secrets": { "Password": { "Name": "{{accountId}}-password", "SecretReference": "file:/run/secrets/owner-{{owner.Value:D}}-{{accountId}}" } }
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
        $$"""{ "MailAccounts": [ {{string.Join(",", accountIds.Select(Account))}} ] }""";

    /// <summary>The service over a substituted row and the real binder, so a candidate is judged the way a start judges one.</summary>
    private sealed class RecordHarness
    {
        private readonly ServedMailOwners servedOwners = new();

        internal RecordHarness(
            MailFathomPermission granted,
            Dictionary<string, string?>? configuration = null,
            MailOwnerId actingFor = default,
            MailFathomPermission alsoGranted = default)
        {
            // A caller that has to read a record before saving it holds both grants, which is what an administrator
            // editing a record actually carries; the unspecified default is what a test granting one permission passes.
            MailFathomPermission[] grants = [.. new[] { granted, alsoGranted }.Where(grant => grant.IsSpecified)];

            var principals = Substitute.For<IAuthorizedPrincipalSource>();
            principals.Current.Returns(actingFor.IsSpecified
                ? AuthorizedPrincipal.CallerActingFor(actingFor, AdministratorIdentity, grants)
                : AuthorizedPrincipal.Caller(AdministratorIdentity, grants));

            this.Documents = Substitute.For<IOwnerSettingsDocumentReader>();
            this.Store = Substitute.For<IOwnerSettingsDocumentWriter>();
            this.Store.CommitAsync(
                    Arg.Any<MailOwnerId>(),
                    Arg.Any<string>(),
                    Arg.Any<long>(),
                    Arg.Any<CancellationToken>())
                .Returns(call => (long?)call.ArgAt<long>(2) + 1);

            // The roster is settled with somebody the tests never write for, so the default deployment reads as an
            // owner nothing declares — which is the ordinary case — until a test states otherwise.
            this.servedOwners.Resolved(
                [Serving(MailOwnerId.Create(new Guid("99999999-9999-9999-9999-999999999999")), MailOwnerAccountSource.OwnerDocument)]);

            var settings = new ConfigurationBuilder()
                .AddInMemoryCollection(configuration ?? [])
                .Build();

            this.Records = new OwnerRecordAdministration(
                new AccessAuthorization(principals),
                this.Documents,
                this.Store,
                new OwnerAccountDocumentBinder(
                    new PersistedSecretMaterial(DeclaredSecretScheme.Registered),
                    new FakeTimeProvider(Today)),
                SecretValidation.OverRegisteredSchemes(),
                this.servedOwners,
                new ConfiguredOwnerMailAccounts(settings, this.servedOwners));
        }

        internal OwnerRecordAdministration Records { get; }

        internal IOwnerSettingsDocumentReader Documents { get; }

        internal IOwnerSettingsDocumentWriter Store { get; }

        internal void Holding(MailOwnerId owner, string json, long version) =>
            this.Documents.ReadAsync(owner, Arg.Any<CancellationToken>())
                .Returns(new OwnerSettingsDocument(owner, $"owner-{owner.Value:D}", json, version, WrittenAtRuntime: true));

        internal void Roster(params ServedMailOwner[] served) => this.servedOwners.Resolved(served);
    }
}
