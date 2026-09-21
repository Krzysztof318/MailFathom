// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.Configuration.UserSettings.Administration;
using MailFathom.Host.Signals;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.UserSettings.Administration;

/// <summary>
/// Covers what an administrator does to the roster itself: who this deployment holds, who joins it, and who leaves.
/// The rules worth stating are the ones that stop a deployment from becoming one that serves the wrong person — the
/// several-user refusal, the unique label, the bound on how many users one deployment holds — and the one act that
/// disposes of everything recorded for somebody.
/// </summary>
public sealed class UserRosterAdministrationTests
{
    private const string AdministratorIdentity = "operations";

    [Fact]
    public async Task ReadRosterAsync_ADeploymentHoldingUsers_ReportsEachOneWithWhatThisProcessIsDoingAboutThem()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminRead);
        harness.Holding(
            new UserRecord(SyntheticUser.Deployment, "alex"),
            new UserRecord(SyntheticUser.Another, "morgan"));
        harness.Serving(SyntheticUser.Deployment);

        // Act
        var roster = await harness.Roster.ReadRosterAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [("alex", true), ("morgan", false)],
            roster.Select(entry => (entry.DisplayName, entry.Served)));
    }

    /// <summary>
    /// One more than a deployment may declare is read, so a roster past the bound is observable rather than silently
    /// truncated into a listing an administrator would then act on as though it were complete.
    /// </summary>
    [Fact]
    public async Task ReadRosterAsync_AnyDeployment_ReadsOneMoreUserThanADeploymentMayHold()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminRead);

        // Act
        await harness.Roster.ReadRosterAsync(TestContext.Current.CancellationToken);

        // Assert
        await harness.Directory.Received(1).ReadUsersAsync(
            ServedUsers.MaximumUsers + 1,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadRosterAsync_ACallerHoldingNoAdministrativeRead_IsRefused()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.MailRead);

        // Act & Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => harness.Roster.ReadRosterAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The identifier is minted here rather than supplied, and it is a version 4 value because a user identifier
    /// reaches administrative APIs, audit records, and logs — and a time-ordered one would publish when each user was
    /// provisioned and in what order relative to every other.
    /// </summary>
    [Fact]
    public async Task ProvisionAsync_ALabelTheDeploymentAccepts_MintsAVersionFourIdentifier()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);

        // Act
        var outcome = await harness.Roster.ProvisionAsync("alex", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.IsProvisioned);
        Assert.Equal(4, outcome.User.Value.Version);
    }

    /// <summary>
    /// The record rather than only the envelope, because a user nothing declares is served from their own record or
    /// from nothing at all — and the marker beside the document is what the next start reads to decide that. It
    /// declares nothing beyond the language, because everything else a record can state is the user's own to state
    /// later and a mailbox is a record of its own; what matters is that the document exists and that a save of it would
    /// be accepted, which a record stating no language would not be.
    /// </summary>
    [Fact]
    public async Task ProvisionAsync_ALabelTheDeploymentAccepts_CommitsARecordNamingTheirLanguageBesideTheEnvelope()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);

        // Act
        var outcome = await harness.Roster.ProvisionAsync("alex", TestContext.Current.CancellationToken);

        // Assert
        await harness.Documents.Received(1).CommitAsync(
            outcome.User,
            """{"Language":"English"}""",
            UserEndpointAccess.Everywhere,
            1,
            Arg.Any<CancellationToken>());
        Assert.Contains(harness.ServedUsers.Users, user => user.User == outcome.User);
    }

    /// <summary>
    /// A recorded user is announced so a replica that did not record them serves them at once, and only once the roster
    /// is released, so a backplane slow to answer holds no other roster write behind it.
    /// </summary>
    [Fact]
    public async Task ProvisionAsync_ALabelTheDeploymentAccepts_AnnouncesTheChangeOnceTheRosterIsReleased()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        var heard = await RosterAnnouncementListener.ListenAsync(harness.Backplane, harness.ServedUsers);

        // Act
        await harness.Roster.ProvisionAsync("alex", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([true], heard);
    }

    /// <summary>A refused provisioning recorded nobody, so no replica is asked to read anything again.</summary>
    [Fact]
    public async Task ProvisionAsync_ALabelAnotherUserAlreadyCarries_AnnouncesNothing()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(new UserRecord(SyntheticUser.Deployment, "alex"));
        var heard = await RosterAnnouncementListener.ListenAsync(harness.Backplane, harness.ServedUsers);

        // Act
        await harness.Roster.ProvisionAsync("alex", TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(heard);
    }

    /// <summary>An erasure is announced so every other replica stops serving the user at once, once the roster is released.</summary>
    [Fact]
    public async Task EraseAsync_AUserThisDeploymentHolds_AnnouncesTheChangeOnceTheRosterIsReleased()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminErase);
        harness.Serving(SyntheticUser.Deployment);
        harness.Erasing(SyntheticUser.Deployment);
        var heard = await RosterAnnouncementListener.ListenAsync(harness.Backplane, harness.ServedUsers);

        // Act
        await harness.Roster.EraseAsync(SyntheticUser.Deployment, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([true], heard);
    }

    /// <summary>Erasing a user the deployment does not hold removed nothing, so no replica is asked to read anything again.</summary>
    [Fact]
    public async Task EraseAsync_AUserThisDeploymentDoesNotHold_AnnouncesNothing()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminErase);
        var heard = await RosterAnnouncementListener.ListenAsync(harness.Backplane, harness.ServedUsers);

        // Act
        await harness.Roster.EraseAsync(SyntheticUser.Another, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(heard);
    }

    /// <summary>A label is what an administrator selects a user by, so two users carrying one would leave nothing to select on.</summary>
    [Fact]
    public async Task ProvisionAsync_ALabelAnotherUserAlreadyCarries_IsRefusedWithoutWritingAnything()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(new UserRecord(SyntheticUser.Deployment, "alex"));

        // Act
        var outcome = await harness.Roster.ProvisionAsync("  alex  ", TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome.IsProvisioned);
        Assert.Contains("already recorded as 'alex'", outcome.RefusalMessage!, StringComparison.Ordinal);
        await harness.Provisioning.DidNotReceiveWithAnyArgs()
            .ProvisionAsync(default, default!, TestContext.Current.CancellationToken);
    }

    /// <summary>The label was taken between the roster being read and the insert reaching the table, which no reading of a snapshot could have refused earlier.</summary>
    [Fact]
    public async Task ProvisionAsync_ALabelTakenBetweenTheReadAndTheInsert_IsRefusedRatherThanReportedAsProvisioned()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Provisioning
            .ProvisionAsync(Arg.Any<UserId>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        var outcome = await harness.Roster.ProvisionAsync("alex", TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome.IsProvisioned);
        await harness.Documents.DidNotReceiveWithAnyArgs()
            .CommitAsync(default, default!, default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The envelope and the record are two writes, so the user can be removed between them — and an outcome reporting
    /// the provisioning as done would leave an administrator believing this deployment holds somebody it holds no
    /// record for, which is the one state every read of that user then answers as an absence.
    /// </summary>
    [Fact]
    public async Task ProvisionAsync_AUserRemovedBeforeTheirRecordWasWritten_IsRefusedRatherThanReportedAsProvisioned()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Documents
            .CommitAsync(
                Arg.Any<UserId>(),
                Arg.Any<string>(),
                Arg.Any<UserEndpointAccess>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>())
            .Returns((long?)null);

        // Act
        var outcome = await harness.Roster.ProvisionAsync("alex", TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome.IsProvisioned);
        Assert.NotNull(outcome.RefusalMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ProvisionAsync_NoLabelAtAll_IsRefusedWithoutReadingTheRoster(string? displayName)
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);

        // Act
        var outcome = await harness.Roster.ProvisionAsync(displayName, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome.IsProvisioned);
        await harness.Directory.DidNotReceiveWithAnyArgs()
            .ReadUsersAsync(default, TestContext.Current.CancellationToken);
    }

    /// <summary>The rules the column and the declared collection are held to, asked here so a label refused in a file is refused over a route.</summary>
    [Fact]
    public async Task ProvisionAsync_ALabelPastWhatTheColumnStores_IsRefusedNamingTheBound()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);

        // Act
        var outcome = await harness.Roster.ProvisionAsync(
            new string('a', UserRecord.MaximumDisplayNameLength + 1),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome.IsProvisioned);
        Assert.Contains(
            UserRecord.MaximumDisplayNameLength.ToString(CultureInfo.InvariantCulture),
            outcome.RefusalMessage!,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A second user beside a user-facing surface that admits a caller naming nobody would leave that surface
    /// serving one person another person's mail, so the roster is held to one user instead.
    /// </summary>
    [Fact]
    public async Task ProvisionAsync_ASecondUserWhileAUserFacingSurfaceAdmitsACallerNamingNobody_IsRefused()
    {
        // Arrange
        var harness = new RosterHarness(
            MailFathomPermission.AdminConfigurationWrite,
            clientEndpoint: new ClientEndpointOptions { Enabled = true });
        harness.Holding(new UserRecord(SyntheticUser.Deployment, "alex"));

        // Act
        var outcome = await harness.Roster.ProvisionAsync("morgan", TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome.IsProvisioned);
        Assert.Contains("requires no authentication", outcome.RefusalMessage!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The refusal is about a second user rather than about the surface, so the first user of a deployment serving an
    /// unauthenticated surface is still recorded — which is the deployment an easy first run produces.
    /// </summary>
    [Fact]
    public async Task ProvisionAsync_TheFirstUserWhileAUserFacingSurfaceAdmitsACallerNamingNobody_IsRecorded()
    {
        // Arrange
        var harness = new RosterHarness(
            MailFathomPermission.AdminConfigurationWrite,
            clientEndpoint: new ClientEndpointOptions { Enabled = true });

        // Act
        var outcome = await harness.Roster.ProvisionAsync("alex", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.IsProvisioned);
    }

    /// <summary>An administrator acts for the deployment rather than for a person, which is what makes recording a second user reachable at all.</summary>
    [Fact]
    public async Task ProvisionAsync_ASecondUserWhileOnlyTheAdministrativeSurfaceIsServed_IsRecorded()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(new UserRecord(SyntheticUser.Deployment, "alex"));

        // Act
        var outcome = await harness.Roster.ProvisionAsync("morgan", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.IsProvisioned);
    }

    [Fact]
    public async Task ProvisionAsync_ADeploymentAlreadyHoldingEveryUserItMay_IsRefusedNamingTheBound()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(
        [
            .. Enumerable.Range(0, ServedUsers.MaximumUsers)
                .Select(position => new UserRecord(
                    UserId.Create(Guid.NewGuid()),
                    $"user-{position}")),
        ]);

        // Act
        var outcome = await harness.Roster.ProvisionAsync("morgan", TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome.IsProvisioned);
        Assert.Contains("already holds", outcome.RefusalMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProvisionAsync_ACallerHoldingOnlyTheAdministrativeRead_IsRefused()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminRead);

        // Act & Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => harness.Roster.ProvisionAsync("alex", TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Whether the user was served is read before the erasure rather than after, because the answer must describe the
    /// deployment the caller asked about rather than the one the erasure left.
    /// </summary>
    [Fact]
    public async Task EraseAsync_AUserThisProcessIsServing_ReportsThatARestartIsOwed()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminErase);
        harness.Serving(SyntheticUser.Deployment);
        harness.Erasing(SyntheticUser.Deployment);

        // Act
        var outcome = await harness.Roster.EraseAsync(
            SyntheticUser.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.UserErased);
        Assert.True(outcome.WasServed);
    }

    /// <summary>An erasure waits until a document write has published, so it cannot remove the user between commit and publication.</summary>
    [Fact]
    public async Task EraseAsync_AnotherRosterWriteIsPublishing_WaitsBeforeDeletingTheUser()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminErase);
        harness.Serving(SyntheticUser.Deployment);
        harness.Erasing(SyntheticUser.Deployment);
        await harness.ServedUsers.WaitForRosterPublicationAsync(TestContext.Current.CancellationToken);

        Task<UserRosterErasureOutcome> erasing;
        try
        {
            // Act
            erasing = harness.Roster.EraseAsync(
                SyntheticUser.Deployment,
                TestContext.Current.CancellationToken);

            // Assert
            Assert.False(erasing.IsCompleted);
            await harness.Erasure.DidNotReceiveWithAnyArgs().EraseAsync(default, [], CancellationToken.None);
        }
        finally
        {
            harness.ServedUsers.ReleaseRosterPublication();
        }

        Assert.True((await erasing).UserErased);
    }

    /// <summary>
    /// The accounts stopped are the ones the erasure disposes of and no others, and the user is off the roster while
    /// the deletion runs — which is what has this replica's coordinator give their supervision back rather than write
    /// into a mailbox being deleted. An account somebody else is also assigned stays running, because erasing this
    /// user takes nothing of it.
    /// </summary>
    [Fact]
    public async Task EraseAsync_AUserSharingOneOfTwoAccounts_StopsOnlyTheirOwnAndIsUnservedWhileItRuns()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminErase);
        var ownAccount = new Guid("41d7b2e0-9c35-4a68-8f12-3b6d5e7a9c04");
        var sharedAccount = new Guid("52e8c3f1-0d46-4b79-9023-4c7e6f8b0d15");
        harness.Serving(SyntheticUser.Deployment);
        harness.MailAccountRecords.HoldUser(
            SyntheticUser.Deployment,
            "{}",
            1,
            new MailAccountRecord(ownAccount, "own@roster.test", "own", "{}", 1),
            new MailAccountRecord(sharedAccount, "shared@roster.test", "shared", "{}", 1));
        harness.MailAccountRecords.HoldUser(
            SyntheticUser.Another,
            "{}",
            1,
            new MailAccountRecord(sharedAccount, "shared@roster.test", "shared", "{}", 1));

        var servedWhileErasing = true;
        IReadOnlyList<Guid> statedAsQuiesced = [];
        harness.Erasure
            .EraseAsync(SyntheticUser.Deployment, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                servedWhileErasing = harness.ServedUsers.Users.Any(
                    candidate => candidate.User == SyntheticUser.Deployment);
                statedAsQuiesced = call.Arg<IReadOnlyList<Guid>>()!;

                return new UserErasureOutcome(true, null);
            });

        // Act
        var outcome = await harness.Roster.EraseAsync(
            SyntheticUser.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.UserErased);
        Assert.False(servedWhileErasing);
        Assert.Equal([ownAccount.ToString("D")], [.. harness.Quiescing.Quiesced.Select(account => account.Value)]);

        // The transaction refuses an account it would delete and which the caller did not state, so the same set that
        // was held is the set it is told about — a shared mailbox being in neither.
        Assert.Equal([ownAccount], statedAsQuiesced);
    }

    /// <summary>
    /// Work the deployment could not stop is a refusal rather than a partial erasure, and a person nothing erased goes
    /// on being served: the roster they were taken off for the attempt has them back.
    /// </summary>
    [Fact]
    public async Task EraseAsync_WorkBoundToTheirAccountsWillNotStop_ErasesNothingAndGoesOnServingThem()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminErase);
        harness.Serving(SyntheticUser.Deployment);
        harness.Erasing(SyntheticUser.Deployment);
        harness.Quiescing.Refusal = "Mail account 41d7b2e0 is still being synchronized.";
        var heard = await RosterAnnouncementListener.ListenAsync(harness.Backplane, harness.ServedUsers);

        // Act
        var outcome = await harness.Roster.EraseAsync(
            SyntheticUser.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome.UserErased);
        Assert.False(outcome.IsQuiesced);
        Assert.Equal(harness.Quiescing.Refusal, outcome.RefusalMessage);
        await harness.Erasure.DidNotReceiveWithAnyArgs().EraseAsync(default, [], CancellationToken.None);
        Assert.Contains(harness.ServedUsers.Users, candidate => candidate.User == SyntheticUser.Deployment);
        Assert.Empty(heard);
    }

    /// <summary>
    /// The transaction refuses on what only it can see — an account that became this user's alone after the holds were
    /// taken, or a job claimed a moment before the lock. Nothing was written, so the caller is answered as for any
    /// other refusal rather than told a user was erased.
    /// </summary>
    [Fact]
    public async Task EraseAsync_TheTransactionRefusesOnAnAccountNothingHolds_ReportsARefusalAndAnnouncesNothing()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminErase);
        harness.Serving(SyntheticUser.Deployment);
        var appearedUnheld = Guid.Parse("7d3a9c15-4e28-4b61-9f07-2c8b6d0e5a34");
        harness.Erasure
            .EraseAsync(SyntheticUser.Deployment, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new UserErasureOutcome(false, appearedUnheld));
        var heard = await RosterAnnouncementListener.ListenAsync(harness.Backplane, harness.ServedUsers);

        // Act
        var outcome = await harness.Roster.EraseAsync(
            SyntheticUser.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome.UserErased);
        Assert.False(outcome.IsQuiesced);
        Assert.Contains(appearedUnheld.ToString("D"), outcome.RefusalMessage!, StringComparison.Ordinal);

        // Nothing was erased, so the person goes on being served and no replica is told otherwise.
        Assert.Contains(harness.ServedUsers.Users, candidate => candidate.User == SyntheticUser.Deployment);
        Assert.Empty(heard);
    }

    [Fact]
    public async Task EraseAsync_AUserThisDeploymentDoesNotHold_ReportsThatNothingWasRemoved()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminErase);

        // Act
        var outcome = await harness.Roster.EraseAsync(
            SyntheticUser.Another,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome.UserErased);
    }

    [Fact]
    public async Task EraseAsync_AUserNamingNobody_IsRefusedWithoutReachingTheErasure()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminErase);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => harness.Roster.EraseAsync(default, TestContext.Current.CancellationToken));

        await harness.Erasure.DidNotReceiveWithAnyArgs().EraseAsync(default, [], TestContext.Current.CancellationToken);
    }

    /// <summary>Erasing somebody disposes of every message this deployment holds for them, which is a grant of its own.</summary>
    [Fact]
    public async Task EraseAsync_ACallerHoldingOnlyTheAdministrativeConfigurationWrite_IsRefused()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);

        // Act & Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => harness.Roster.EraseAsync(SyntheticUser.Deployment, TestContext.Current.CancellationToken));
    }

    /// <summary>A label is what an administrator selects a user by, and nothing is keyed by it, so replacing one is an ordinary write.</summary>
    [Fact]
    public async Task RelabelAsync_AUserThisDeploymentHolds_PutsTheLabelOnTheirRow()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(new UserRecord(SyntheticUser.Deployment, "alexandra"));

        // Act
        var outcome = await harness.Roster.RelabelAsync(
            SyntheticUser.Deployment,
            "alex",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.IsRelabelled);
        await harness.Provisioning.Received(1)
            .RelabelAsync(SyntheticUser.Deployment, "alex", Arg.Any<CancellationToken>());
    }

    /// <summary>The label is trimmed the way a declared one is, so a roster is never told apart by trailing space.</summary>
    [Fact]
    public async Task RelabelAsync_ALabelWrittenWithSurroundingSpace_WritesItTrimmed()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(new UserRecord(SyntheticUser.Deployment, "alexandra"));

        // Act
        await harness.Roster.RelabelAsync(
            SyntheticUser.Deployment,
            "  alex  ",
            TestContext.Current.CancellationToken);

        // Assert
        await harness.Provisioning.Received(1)
            .RelabelAsync(SyntheticUser.Deployment, "alex", Arg.Any<CancellationToken>());
    }

    /// <summary>Two users carrying one label would leave an administrator nothing to select on, which the column's index refuses.</summary>
    [Fact]
    public async Task RelabelAsync_ALabelAnotherUserCarries_IsRefusedWithoutReachingTheRow()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(
            new UserRecord(SyntheticUser.Deployment, "alexandra"),
            new UserRecord(SyntheticUser.Another, "alex"));

        // Act
        var outcome = await harness.Roster.RelabelAsync(
            SyntheticUser.Deployment,
            "alex",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.UserHeld);
        Assert.NotNull(outcome.RefusalMessage);
        Assert.Contains("'alex'", outcome.RefusalMessage, StringComparison.Ordinal);
        await harness.Provisioning.DidNotReceiveWithAnyArgs()
            .RelabelAsync(default, default!, CancellationToken.None);
    }

    /// <summary>
    /// The label taken between the roster being read and the statement reaching the table is what no reading of a
    /// snapshot could have refused, so the write reports it and the refusal is the same sentence either way.
    /// </summary>
    [Fact]
    public async Task RelabelAsync_ALabelTakenWhileTheWriteWasInFlight_IsRefusedWithTheSameSentence()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(new UserRecord(SyntheticUser.Deployment, "alexandra"));
        harness.Provisioning
            .RelabelAsync(Arg.Any<UserId>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        var outcome = await harness.Roster.RelabelAsync(
            SyntheticUser.Deployment,
            "alex",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.UserHeld);
        Assert.NotNull(outcome.RefusalMessage);
        Assert.Contains("'alex'", outcome.RefusalMessage, StringComparison.Ordinal);
    }

    /// <summary>A user this deployment does not hold is an absence to report rather than a label to refuse.</summary>
    /// <remarks>
    /// The two are one sentence to an administrator and two answers to a caller: the route publishes this one as the
    /// same absence every other user-scoped route answers with, so the outcome carries which of them it is.
    /// </remarks>
    [Fact]
    public async Task RelabelAsync_AUserThisDeploymentDoesNotHold_ReportsTheUserAsUnheld()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(new UserRecord(SyntheticUser.Deployment, "alex"));

        // Act
        var outcome = await harness.Roster.RelabelAsync(
            SyntheticUser.Another,
            "sam",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome.UserHeld);
        Assert.False(outcome.IsRelabelled);
        await harness.Provisioning.DidNotReceiveWithAnyArgs()
            .RelabelAsync(default, default!, CancellationToken.None);
    }

    /// <summary>A label is the one thing an administrator reads a roster by, so an empty one leaves nothing to read.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RelabelAsync_ALabelNamingNothing_IsRefusedWithoutReadingTheRoster(string? label)
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);

        // Act
        var outcome = await harness.Roster.RelabelAsync(
            SyntheticUser.Deployment,
            label,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.UserHeld);
        Assert.NotNull(outcome.RefusalMessage);
        await harness.Directory.DidNotReceiveWithAnyArgs()
            .ReadUsersAsync(default, CancellationToken.None);
    }

    [Fact]
    public async Task RelabelAsync_AUserNamingNobody_IsRefusedWithoutReachingTheRow()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => harness.Roster.RelabelAsync(default, "alex", TestContext.Current.CancellationToken));

        await harness.Provisioning.DidNotReceiveWithAnyArgs()
            .RelabelAsync(default, default!, CancellationToken.None);
    }

    /// <summary>Changing what a roster reads like is what this deployment is rather than what it does next, so it takes the configuration grant.</summary>
    [Fact]
    public async Task RelabelAsync_ACallerHoldingOnlyTheAdministrativeRead_IsRefused()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminRead);

        // Act & Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => harness.Roster.RelabelAsync(
                SyntheticUser.Deployment,
                "alex",
                TestContext.Current.CancellationToken));
    }

    /// <summary>An administrator reads which endpoints each user is served on where they select the user, so the roster carries both switches.</summary>
    [Fact]
    public async Task ReadRosterAsync_AUserKeptOffAnEndpoint_ReportsTheirSwitches()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminRead);
        harness.Holding(new UserRecord(SyntheticUser.Deployment, "alex")
        {
            EndpointAccess = new UserEndpointAccess(McpEndpoint: false, ClientEndpoint: true),
        });

        // Act
        var roster = await harness.Roster.ReadRosterAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new UserEndpointAccess(McpEndpoint: false, ClientEndpoint: true), Assert.Single(roster).EndpointAccess);
    }

    /// <summary>The roster over substituted rows, with the endpoint posture a deployment's several-user refusal is read from.</summary>
    private sealed class RosterHarness
    {
        internal RosterHarness(
            MailFathomPermission granted,
            ClientEndpointOptions? clientEndpoint = null)
        {
            var principals = Substitute.For<IAuthorizedPrincipalSource>();
            principals.Current.Returns(AuthorizedPrincipal.Caller(AdministratorIdentity, [granted]));

            this.Directory = Substitute.For<IUserDirectory>();
            this.Directory.ReadUsersAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);

            this.Provisioning = Substitute.For<IUserProvisioning>();
            this.Provisioning
                .ProvisionAsync(Arg.Any<UserId>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(true);
            this.Provisioning
                .RelabelAsync(Arg.Any<UserId>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(true);

            this.Erasure = Substitute.For<IUserErasure>();
            this.Erasure.EraseAsync(Arg.Any<UserId>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
                .Returns(new UserErasureOutcome(false, null));

            this.Documents = Substitute.For<IUserSettingsDocumentWriter>();
            this.Documents
                .CommitAsync(
                    Arg.Any<UserId>(),
                    Arg.Any<string>(),
                    Arg.Any<UserEndpointAccess>(),
                    Arg.Any<long>(),
                    Arg.Any<CancellationToken>())
                .Returns((long?)2);

            // A roster naming somebody no test acts on, so "served" is a fact a test states rather than a default.
            this.ServedUsers.Resolved(
            [
                new(
                    UserId.Create(new Guid("99999999-9999-9999-9999-999999999999")),
                    "nobody-these-tests-name",
                    []),
            ]);

            var settings = new ConfigurationBuilder().Build();

            this.Roster = new UserRosterAdministration(
                new AccessAuthorization(principals),
                this.Directory,
                this.Provisioning,
                this.Erasure,
                this.MailAccountRecords,
                this.Quiescing,
                this.Documents,
                this.ServedUsers,
                new SeveralUserAdmission(
                    Options.Create(new McpEndpointOptions()),
                    Options.Create(clientEndpoint ?? new ClientEndpointOptions())),
                new ConfigurationChangeAnnouncements(
                    () => Task.FromResult(this.Backplane.Connect()),
                    NullLogger<ConfigurationChangeAnnouncements>.Instance),
                NullLogger<UserRosterAdministration>.Instance);
        }

        internal UserRosterAdministration Roster { get; }

        /// <summary>Gets the backplane a roster change is announced over, which nobody hears until a test listens.</summary>
        internal InMemoryBackplane Backplane { get; } = new();

        internal IUserDirectory Directory { get; }

        internal IUserProvisioning Provisioning { get; }

        internal IUserErasure Erasure { get; }

        internal IUserSettingsDocumentWriter Documents { get; }

        /// <summary>Gets the accounts and assignments an erasure reads the mailboxes it has to stop out of.</summary>
        internal InMemoryMailAccountRecordStore MailAccountRecords { get; } = new();

        /// <summary>Gets the quiescing an erasure runs under, which lets the work through unless a test refuses it.</summary>
        internal RecordedMailAccountWorkQuiescing Quiescing { get; } = new();

        internal ServedUsers ServedUsers { get; } = new();

        internal void Holding(params UserRecord[] held) =>
            this.Directory.ReadUsersAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(held);

        internal void Serving(UserId user) =>
            this.ServedUsers.Resolved([new(user, "served", [])]);

        internal void Serving(params ServedUser[] users) => this.ServedUsers.Resolved(users);

        internal void Erasing(UserId user) =>
            this.Erasure.EraseAsync(user, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
                .Returns(new UserErasureOutcome(true, null));
    }
}
