// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Access;
using MailFathom.Application.Paging;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.Configuration.UserSettings.Administration;
using MailFathom.Host.Hosting.Workers;
using MailFathom.Host.Signals;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.UserSettings.Administration;

/// <summary>
/// Covers what an administrator does to the roster itself: who this deployment holds, who joins it, and who leaves.
/// The rules worth stating are the ones that stop a deployment from becoming one that serves the wrong person — the
/// several-user refusal and the unique label — and the one act that disposes of everything recorded for somebody.
/// </summary>
public sealed class UserRosterAdministrationTests
{
    private const string AdministratorIdentity = "operations";

    private static readonly AdministrativeListingQuery FirstPage = AdministrativeListingQuery.Create(pageSize: null, after: null)!;

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
        var roster = await harness.Roster.ReadRosterAsync(FirstPage, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [("alex", true), ("morgan", false)],
            roster.Entries.Select(entry => (entry.DisplayName, entry.Served)));
    }

    /// <summary>A page the directory says more follow is answered with where the next one continues, so a walk reaches every user.</summary>
    [Fact]
    public async Task ReadRosterAsync_APageWithMoreFollowing_CarriesWhereTheNextPageContinues()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminRead);
        var query = AdministrativeListingQuery.Create(pageSize: 1, after: SyntheticUser.Another.Value)!;
        harness.Directory.ReadUserPageAsync(query, Arg.Any<IReadOnlySet<AssignmentScope>>(), Arg.Any<CancellationToken>())
            .Returns(new AdministrativeListingPage<UserRecord>(
                [new UserRecord(SyntheticUser.Deployment, "alex")],
                ContinuesAfter: SyntheticUser.Deployment.Value));

        // Act
        var roster = await harness.Roster.ReadRosterAsync(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("alex", Assert.Single(roster.Entries).DisplayName);
        Assert.Equal(SyntheticUser.Deployment.Value, roster.ContinuesAfter);
    }

    /// <summary>Rows that could not be read list their users as unserved rather than failing the whole page.</summary>
    [Fact]
    public async Task ReadRosterAsync_RowsThatCannotBeRead_ListTheirUsersAsUnserved()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminRead);
        harness.Holding(new UserRecord(SyntheticUser.Another, "morgan"));
        harness.UserRows.ReadVersionsAsync(Arg.Any<IReadOnlyCollection<UserId>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new UserSettingsUnreadableException("The user records could not be read."));

        // Act
        var roster = await harness.Roster.ReadRosterAsync(FirstPage, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(Assert.Single(roster.Entries).Served);
    }

    /// <summary>
    /// Whether a listed user is served is answered from what this replica holds and one reading of the page's rows, so a
    /// page of a thousand people costs one statement rather than a read and a secret resolution for each of them.
    /// </summary>
    [Fact]
    public async Task ReadRosterAsync_UsersThisReplicaHasNotComposed_AreListedFromTheirRowsWithoutReadingTheirRecords()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminRead);
        harness.Holding(
            new UserRecord(SyntheticUser.Deployment, "alex"),
            new UserRecord(SyntheticUser.Another, "morgan"));
        harness.UserRows.ReadVersionsAsync(Arg.Any<IReadOnlyCollection<UserId>>(), Arg.Any<CancellationToken>())
            .Returns([new UserSettingsDocumentVersion(SyntheticUser.Another, 1)]);

        // Act
        var roster = await harness.Roster.ReadRosterAsync(FirstPage, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [("alex", false), ("morgan", true)],
            roster.Entries.Select(entry => (entry.DisplayName, entry.Served)));
        await harness.UserRows.DidNotReceive().ReadAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadRosterAsync_ACallerHoldingNoAdministrativeRead_IsRefused()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.MailRead);

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => harness.Roster.ReadRosterAsync(FirstPage, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminRead, refusal.RequiredPermission);
    }

    /// <summary>
    /// The identifier is minted here rather than supplied, and it is a version 7 value like every identifier MailFathom
    /// mints — ADR 0036 records the rule and the residual it accepts for an identifier naming a person.
    /// </summary>
    [Fact]
    public async Task ProvisionAsync_ALabelTheDeploymentAccepts_MintsAVersionSevenIdentifier()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);

        // Act
        var outcome = await harness.Roster.ProvisionAsync("alex", organizationId: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.IsProvisioned);
        Assert.Equal(7, outcome.User.Value.Version);
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
        var outcome = await harness.Roster.ProvisionAsync("alex", organizationId: null, TestContext.Current.CancellationToken);

        // Assert
        await harness.Documents.Received(1).CommitAsync(
            outcome.User,
            """{"Language":"English"}""",
            UserEndpointAccess.Everywhere,
            1,
            Arg.Any<CancellationToken>());
        Assert.Equal("alex", harness.ServedUsers.Peek(outcome.User)?.DisplayName);
    }

    /// <summary>
    /// A recorded user is announced so every other replica counts its users again at once, and only once this replica
    /// has counted them itself, so the replica that recorded the user is never the one still answering for the old count.
    /// </summary>
    [Fact]
    public async Task ProvisionAsync_ALabelTheDeploymentAccepts_CountsTheUsersAgainBeforeAnnouncingTheChange()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        var heard = await RosterAnnouncementListener.ListenAsync(harness.Backplane, harness.CountsRead);

        // Act
        await harness.Roster.ProvisionAsync("alex", organizationId: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([1], heard);
    }

    /// <summary>A refused provisioning recorded nobody, so no replica is asked to read anything again.</summary>
    [Fact]
    public async Task ProvisionAsync_ALabelAnotherUserAlreadyCarries_AnnouncesNothing()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.LabelTaken();
        var heard = await RosterAnnouncementListener.ListenAsync(harness.Backplane, () => true);

        // Act
        await harness.Roster.ProvisionAsync("alex", organizationId: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(heard);
    }

    /// <summary>
    /// An erasure is announced so every other replica stops serving the user at once, and only once this replica has
    /// let go of them and counted its users again.
    /// </summary>
    [Fact]
    public async Task EraseAsync_AUserThisDeploymentHolds_LetsGoOfThemAndCountsAgainBeforeAnnouncingTheChange()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminErase);
        harness.Serving(SyntheticUser.Deployment);
        harness.Erasing(SyntheticUser.Deployment);
        var heard = await RosterAnnouncementListener.ListenAsync(
            harness.Backplane,
            () => (Held: harness.ServedUsers.Peek(SyntheticUser.Deployment) is not null, Counted: harness.CountsRead()));

        // Act
        await harness.Roster.EraseAsync(SyntheticUser.Deployment, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([(false, 1)], heard);
    }

    /// <summary>Erasing a user the deployment does not hold removed nothing, so no replica is asked to read anything again.</summary>
    [Fact]
    public async Task EraseAsync_AUserThisDeploymentDoesNotHold_AnnouncesNothing()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminErase);
        var heard = await RosterAnnouncementListener.ListenAsync(harness.Backplane, () => true);

        // Act
        await harness.Roster.EraseAsync(SyntheticUser.Another, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(heard);
    }

    /// <summary>
    /// A label is what an administrator selects a user by, so two users carrying one would leave nothing to select on.
    /// The table's unique index is what refuses it, because no read ahead of the insert could have done so without
    /// racing a concurrent write — and the label it is asked about is the one written, trimmed.
    /// </summary>
    [Fact]
    public async Task ProvisionAsync_ALabelAnotherUserAlreadyCarries_IsRefusedNamingTheLabel()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.LabelTaken();

        // Act
        var outcome = await harness.Roster.ProvisionAsync("  alex  ", organizationId: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome.IsProvisioned);
        Assert.Contains("already recorded as 'alex'", outcome.RefusalMessage!, StringComparison.Ordinal);
        await harness.Provisioning.Received(1).ProvisionAsync(Arg.Any<UserId>(), "alex", Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    /// <summary>An insert the index refused recorded nobody, so no record is written for an identifier nothing holds and nothing is counted again.</summary>
    [Fact]
    public async Task ProvisionAsync_ALabelTheInsertRefuses_WritesNoRecordAndCountsNothing()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.LabelTaken();

        // Act
        var outcome = await harness.Roster.ProvisionAsync("alex", organizationId: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome.IsProvisioned);
        await harness.Documents.DidNotReceiveWithAnyArgs()
            .CommitAsync(default, default!, default, default, TestContext.Current.CancellationToken);
        Assert.Equal(0, harness.CountsRead());
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
        var outcome = await harness.Roster.ProvisionAsync("alex", organizationId: null, TestContext.Current.CancellationToken);

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
        var outcome = await harness.Roster.ProvisionAsync(displayName, organizationId: null, TestContext.Current.CancellationToken);

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
            organizationId: null,
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
        harness.UserRows.ReadVersionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new UserSettingsDocumentVersion(SyntheticUser.Deployment, 1)]);

        // Act
        var outcome = await harness.Roster.ProvisionAsync("morgan", organizationId: null, TestContext.Current.CancellationToken);

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
        var outcome = await harness.Roster.ProvisionAsync("alex", organizationId: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.IsProvisioned);
    }

    /// <summary>
    /// An administrator acts for the deployment rather than for a person, which is what makes recording a second user
    /// reachable at all — and, with no surface to refuse it for, the users already held are not even read.
    /// </summary>
    [Fact]
    public async Task ProvisionAsync_ASecondUserWhileOnlyTheAdministrativeSurfaceIsServed_IsRecordedWithoutReadingTheUsersHeld()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(new UserRecord(SyntheticUser.Deployment, "alex"));

        // Act
        var outcome = await harness.Roster.ProvisionAsync("morgan", organizationId: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.IsProvisioned);
        await harness.Directory.DidNotReceiveWithAnyArgs().ReadUsersAsync(default, TestContext.Current.CancellationToken);
    }

    /// <summary>A caller holding the write nowhere is refused whether or not the request names an organization.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProvisionAsync_ACallerHoldingOnlyTheAdministrativeRead_IsRefusedWithoutRecordingAnybody(bool intoAnOrganization)
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminRead);
        Guid? organizationId = intoAnOrganization ? AccessAuthorizations.ScopedOrganization : null;

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => harness.Roster.ProvisionAsync("alex", organizationId, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminConfigurationWrite, refusal.RequiredPermission);
        Assert.Empty(harness.Provisioning.ReceivedCalls());
    }

    public static TheoryData<AssignmentScope> ScopesCoveringTheUser => [.. AccessAuthorizations.ScopesCoveringTheirTarget];

    public static TheoryData<AssignmentScope> ScopesOutsideTheUser => [.. AccessAuthorizations.ScopesOutsideTheirTarget];

    public static TheoryData<AssignmentScope> ScopesCoveringTheOrganization =>
        [AssignmentScope.Deployment, AssignmentScope.Organization(AccessAuthorizations.ScopedOrganization)];

    /// <summary>Gets the narrower scopes that reach no organization: another organization, a member of this one, and somebody else.</summary>
    public static TheoryData<AssignmentScope> ScopesOutsideTheOrganization =>
        [.. AccessAuthorizations.ScopesOutsideTheirTarget, AssignmentScope.User(AccessAuthorizations.ScopedHolder)];

    public static TheoryData<AssignmentScope> ScopesBelowTheDeployment =>
    [
        AssignmentScope.Organization(AccessAuthorizations.ScopedOrganization),
        AssignmentScope.User(AccessAuthorizations.ScopedHolder),
    ];

    /// <summary>A listing never refuses over a scope: it answers within the one the caller reads at, whichever kind it is.</summary>
    [Theory]
    [MemberData(nameof(ScopesCoveringTheUser))]
    public async Task ReadRosterAsync_ACallerReadingAtOneScope_AsksTheDirectoryWithinThatScopeAlone(AssignmentScope scope)
    {
        // Arrange
        var harness = HarnessScopedAt(scope, MailFathomPermission.AdminRead);
        harness.Holding();

        // Act
        await harness.Roster.ReadRosterAsync(FirstPage, TestContext.Current.CancellationToken);

        // Assert
        await harness.Directory.Received(1).ReadUserPageAsync(
            FirstPage,
            Arg.Is<IReadOnlySet<AssignmentScope>>(scopes => scopes!.SetEquals(new[] { scope })),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [MemberData(nameof(ScopesCoveringTheOrganization))]
    public async Task ProvisionAsync_IntoAnOrganizationTheCallersScopeCovers_RecordsTheUserIntoIt(AssignmentScope scope)
    {
        // Arrange
        var harness = HarnessScopedAt(scope, MailFathomPermission.AdminConfigurationWrite);

        // Act
        var outcome = await harness.Roster.ProvisionAsync(
            "alex",
            AccessAuthorizations.ScopedOrganization,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.IsProvisioned);
        await harness.Provisioning.Received(1).ProvisionAsync(
            outcome.User,
            "alex",
            AccessAuthorizations.ScopedOrganization,
            Arg.Any<CancellationToken>());
    }

    /// <summary>An organization outside the caller's scope is refused exactly as one this deployment does not hold, so the refusal discloses nothing beyond the caller's own.</summary>
    [Theory]
    [MemberData(nameof(ScopesOutsideTheOrganization))]
    public async Task ProvisionAsync_IntoAnOrganizationOutsideTheCallersScope_IsRefusedAsNoSuchOrganizationWithoutRecordingAnybody(
        AssignmentScope scope)
    {
        // Arrange
        var harness = HarnessScopedAt(scope, MailFathomPermission.AdminConfigurationWrite);

        // Act
        var outcome = await harness.Roster.ProvisionAsync(
            "alex",
            AccessAuthorizations.ScopedOrganization,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome.IsProvisioned);
        Assert.Contains(
            $"holds no organization '{AccessAuthorizations.ScopedOrganization}'",
            outcome.RefusalMessage!,
            StringComparison.Ordinal);
        Assert.Empty(harness.Provisioning.ReceivedCalls());
    }

    [Fact]
    public async Task ProvisionAsync_IntoAnOrganizationTheInsertFindsNoLonger_IsRefusedAsNoSuchOrganization()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Provisioning
            .ProvisionAsync(Arg.Any<UserId>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(UserProvisioningResult.UnknownOrganization);

        // Act
        var outcome = await harness.Roster.ProvisionAsync(
            "alex",
            AccessAuthorizations.ScopedOrganization,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome.IsProvisioned);
        Assert.Contains(
            $"holds no organization '{AccessAuthorizations.ScopedOrganization}'",
            outcome.RefusalMessage!,
            StringComparison.Ordinal);
    }

    /// <summary>Only the deployment's scope covers a user in no organization, so only it records one there, and the refusal is the same whatever the request named.</summary>
    [Theory]
    [MemberData(nameof(ScopesBelowTheDeployment))]
    public async Task ProvisionAsync_IntoNoOrganizationByACallerScopedBelowTheDeployment_IsRefusedForTheDeploymentAloneWithoutRecordingAnybody(
        AssignmentScope scope)
    {
        // Arrange
        var harness = HarnessScopedAt(scope, MailFathomPermission.AdminConfigurationWrite);

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => harness.Roster.ProvisionAsync("alex", organizationId: null, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminConfigurationWrite, refusal.RequiredPermission);
        Assert.True(refusal.RefusedForTheDeploymentAlone);
        Assert.Empty(harness.Provisioning.ReceivedCalls());
    }

    [Theory]
    [MemberData(nameof(ScopesCoveringTheUser))]
    public async Task RelabelAsync_AUserTheCallersScopeCovers_PutsTheLabelOnTheirRow(AssignmentScope scope)
    {
        // Arrange
        var harness = HarnessScopedAt(scope, MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(new UserRecord(AccessAuthorizations.ScopedHolder, "alexandra"));

        // Act
        var outcome = await harness.Roster.RelabelAsync(
            AccessAuthorizations.ScopedHolder,
            "alex",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.IsRelabelled);
    }

    [Theory]
    [MemberData(nameof(ScopesOutsideTheUser))]
    public async Task RelabelAsync_AUserOutsideTheCallersScope_ReportsTheUserAsUnheldWithoutTouchingTheRow(AssignmentScope scope)
    {
        // Arrange
        var harness = HarnessScopedAt(scope, MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(new UserRecord(AccessAuthorizations.ScopedHolder, "alexandra"));

        // Act
        var outcome = await harness.Roster.RelabelAsync(
            AccessAuthorizations.ScopedHolder,
            "alex",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(UserRelabelOutcome.NoSuchUser, outcome);
        Assert.Empty(harness.Provisioning.ReceivedCalls());
    }

    [Theory]
    [MemberData(nameof(ScopesCoveringTheUser))]
    public async Task EraseAsync_AUserTheCallersScopeCovers_ErasesThem(AssignmentScope scope)
    {
        // Arrange
        var harness = HarnessScopedAt(scope, MailFathomPermission.AdminErase);
        harness.Erasing(AccessAuthorizations.ScopedHolder);

        // Act
        var outcome = await harness.Roster.EraseAsync(AccessAuthorizations.ScopedHolder, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.UserErased);
    }

    /// <summary>A user outside the caller's scope is answered exactly as one this deployment does not hold, served or not.</summary>
    [Theory]
    [MemberData(nameof(ScopesOutsideTheUser))]
    public async Task EraseAsync_AUserOutsideTheCallersScope_ErasesNothingAndReportsNothingServed(AssignmentScope scope)
    {
        // Arrange
        var harness = HarnessScopedAt(scope, MailFathomPermission.AdminErase);
        harness.Serving(AccessAuthorizations.ScopedHolder);
        harness.Erasing(AccessAuthorizations.ScopedHolder);

        // Act
        var outcome = await harness.Roster.EraseAsync(AccessAuthorizations.ScopedHolder, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((false, false), (outcome.UserErased, outcome.WasServed));
        Assert.Empty(harness.Erasure.ReceivedCalls());
    }

    /// <summary>
    /// An erasure deletes the mail accounts assigned to the user alone, and covering the user is not covering those: an
    /// account the caller's scope does not cover refuses the whole erasure, while one it does cover goes with the user.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EraseAsync_AUserAloneAssignedAnAccountOutsideTheCallersScope_IsRefusedBeforeAnythingIsErased(bool scopedOverTheOrganization)
    {
        // Arrange
        var scope = scopedOverTheOrganization
            ? AssignmentScope.Organization(AccessAuthorizations.ScopedOrganization)
            : AssignmentScope.User(AccessAuthorizations.ScopedHolder);
        var coveredAccount = Guid.Parse(AccessAuthorizations.ScopedAccount.Value);
        var uncoveredAccount = new Guid("63f9d402-1e57-4c8a-a134-5d8f7a9c1e26");

        var covering = HarnessScopedAt(scope, MailFathomPermission.AdminErase);
        covering.Erasing(AccessAuthorizations.ScopedHolder);
        covering.MailAccountRecords.HoldUser(
            AccessAuthorizations.ScopedHolder,
            "{}",
            1,
            new MailAccountRecord(coveredAccount, "covered@roster.test", "covered", "{}", 1));

        var reachingPast = HarnessScopedAt(scope, MailFathomPermission.AdminErase);
        reachingPast.Erasing(AccessAuthorizations.ScopedHolder);
        reachingPast.MailAccountRecords.HoldUser(
            AccessAuthorizations.ScopedHolder,
            "{}",
            1,
            new MailAccountRecord(coveredAccount, "covered@roster.test", "covered", "{}", 1),
            new MailAccountRecord(uncoveredAccount, "uncovered@roster.test", "uncovered", "{}", 1));

        // Act
        var erased = await covering.Roster.EraseAsync(AccessAuthorizations.ScopedHolder, TestContext.Current.CancellationToken);
        var refused = await reachingPast.Roster.EraseAsync(AccessAuthorizations.ScopedHolder, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(erased.UserErased);
        Assert.False(refused.UserErased);
        Assert.Contains(MailFathomPermission.AdminErase.Name, refused.RefusalMessage!, StringComparison.Ordinal);
        Assert.Empty(reachingPast.Erasure.ReceivedCalls());
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

    /// <summary>A user whose record could not be read is reported as unserved rather than failing the erasure, which still runs.</summary>
    [Fact]
    public async Task EraseAsync_AUserWhoseRecordCannotBeRead_ErasesThemAndReportsThemUnserved()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminErase);
        harness.Erasing(SyntheticUser.Deployment);
        harness.UserRows.ReadAsync(SyntheticUser.Deployment, Arg.Any<CancellationToken>())
            .ThrowsAsync(new UserSettingsUnreadableException("The user records could not be read."));

        // Act
        var outcome = await harness.Roster.EraseAsync(
            SyntheticUser.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.UserErased);
        Assert.False(outcome.WasServed);
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
        var withheldWhileErasing = (Own: false, Shared: true);
        IReadOnlyList<Guid> statedAsQuiesced = [];
        harness.Erasure
            .EraseAsync(SyntheticUser.Deployment, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                servedWhileErasing = harness.ServedUsers.Peek(SyntheticUser.Deployment) is not null;
                withheldWhileErasing = (
                    harness.WithheldAccounts.IsWithheld(MailAccountId.Create(ownAccount.ToString("D"))),
                    harness.WithheldAccounts.IsWithheld(MailAccountId.Create(sharedAccount.ToString("D"))));
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
        Assert.Equal((true, false), withheldWhileErasing);
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
        var ownAccount = new Guid("41d7b2e0-9c35-4a68-8f12-3b6d5e7a9c04");
        harness.Serving(SyntheticUser.Deployment);
        harness.MailAccountRecords.HoldUser(
            SyntheticUser.Deployment,
            "{}",
            1,
            new MailAccountRecord(ownAccount, "own@roster.test", "own", "{}", 1));
        harness.Erasing(SyntheticUser.Deployment);
        harness.Quiescing.Refusal = "Mail account 41d7b2e0 is still being synchronized.";
        var heard = await RosterAnnouncementListener.ListenAsync(harness.Backplane, () => true);

        // Act
        var outcome = await harness.Roster.EraseAsync(
            SyntheticUser.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome.UserErased);
        Assert.False(outcome.IsQuiesced);
        Assert.Equal(harness.Quiescing.Refusal, outcome.RefusalMessage);
        await harness.Erasure.DidNotReceiveWithAnyArgs().EraseAsync(default, [], CancellationToken.None);
        Assert.NotNull(harness.ServedUsers.Peek(SyntheticUser.Deployment));
        Assert.False(harness.WithheldAccounts.IsWithheld(MailAccountId.Create(ownAccount.ToString("D"))));
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
        var heard = await RosterAnnouncementListener.ListenAsync(harness.Backplane, () => true);

        // Act
        var outcome = await harness.Roster.EraseAsync(
            SyntheticUser.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome.UserErased);
        Assert.False(outcome.IsQuiesced);
        Assert.Contains(appearedUnheld.ToString("D"), outcome.RefusalMessage!, StringComparison.Ordinal);

        // Nothing was erased, so the person goes on being served and no replica is told otherwise.
        Assert.NotNull(harness.ServedUsers.Peek(SyntheticUser.Deployment));
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

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => harness.Roster.EraseAsync(SyntheticUser.Deployment, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminErase, refusal.RequiredPermission);
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

    /// <summary>
    /// Two users carrying one label would leave an administrator nothing to select on, which the column's index refuses —
    /// and the index is what decides it, because no reading of a snapshot could refuse a label taken while the write
    /// was in flight.
    /// </summary>
    [Fact]
    public async Task RelabelAsync_ALabelAnotherUserCarries_IsRefusedNamingTheLabel()
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
            .ReadUserAsync(default, CancellationToken.None);
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

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => harness.Roster.RelabelAsync(
                SyntheticUser.Deployment,
                "alex",
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminConfigurationWrite, refusal.RequiredPermission);
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
        var roster = await harness.Roster.ReadRosterAsync(FirstPage, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            new UserEndpointAccess(McpEndpoint: false, ClientEndpoint: true),
            Assert.Single(roster.Entries).EndpointAccess);
    }

    /// <summary>The roster for an administrator holding one permission at one scope, over <see cref="AccessAuthorizations.ScopedHolder" /> placed in the scoped organization.</summary>
    private static RosterHarness HarnessScopedAt(AssignmentScope scope, MailFathomPermission granted) =>
        new(AccessAuthorizations.ForAdministratorScopedAt(scope, granted));

    /// <summary>The roster over substituted rows, with the endpoint posture a deployment's several-user refusal is read from.</summary>
    private sealed class RosterHarness
    {
        internal RosterHarness(
            MailFathomPermission granted,
            ClientEndpointOptions? clientEndpoint = null)
            : this(
                AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Caller(AdministratorIdentity, [granted])),
                clientEndpoint)
        {
        }

        internal RosterHarness(
            AccessAuthorization authorization,
            ClientEndpointOptions? clientEndpoint = null)
        {
            this.Directory = Substitute.For<IUserDirectory>();
            this.Directory.ReadUsersAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);

            this.Provisioning = Substitute.For<IUserProvisioning>();
            this.Provisioning
                .ProvisionAsync(Arg.Any<UserId>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
                .Returns(UserProvisioningResult.Provisioned);
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

            // No user's record is held unless a test states it, so "served" is a fact a test states rather than a default.
            this.UserRows = Substitute.For<IUserSettingsDocumentReader>();
            this.UserRows.ReadVersionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
            this.UserRows.ReadVersionsAsync(Arg.Any<IReadOnlyCollection<UserId>>(), Arg.Any<CancellationToken>()).Returns([]);
            this.ServedUsers = ResolvedServedUsers.Over(this.UserRows);

            this.Roster = new UserRosterAdministration(
                authorization,
                this.Directory,
                this.Provisioning,
                this.Erasure,
                this.MailAccountRecords,
                this.Quiescing,
                this.WithheldAccounts,
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

        /// <summary>Gets the accounts this replica's synchronization is kept off while an erasure runs.</summary>
        internal WithheldMailAccounts WithheldAccounts { get; } = new();

        /// <summary>Gets the user rows the served users are read from, which hold nobody's record until a test says so.</summary>
        internal IUserSettingsDocumentReader UserRows { get; }

        internal ServedUsers ServedUsers { get; }

        internal void Holding(params UserRecord[] held)
        {
            this.Directory.ReadUsersAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(held);
            this.Directory.ReadUserPageAsync(
                    Arg.Any<AdministrativeListingQuery>(),
                    Arg.Any<IReadOnlySet<AssignmentScope>>(),
                    Arg.Any<CancellationToken>())
                .Returns(new AdministrativeListingPage<UserRecord>(held, ContinuesAfter: null));

            foreach (var record in held)
            {
                this.Directory.ReadUserAsync(record.User, Arg.Any<CancellationToken>()).Returns(record);
            }
        }

        internal void Serving(UserId user) => this.ServedUsers.Published(new ServedUser(user, "served", []), 1);

        /// <summary>Has the table's unique index refuse whatever label is written, as it does a label another user carries.</summary>
        internal void LabelTaken() =>
            this.Provisioning
                .ProvisionAsync(Arg.Any<UserId>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
                .Returns(UserProvisioningResult.LabelTaken);

        /// <summary>Counts how often the users this deployment holds were counted, which a provisioning or an erasure does once it commits.</summary>
        internal int CountsRead() =>
            this.UserRows.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IUserSettingsDocumentReader.ReadVersionsAsync));

        internal void Erasing(UserId user) =>
            this.Erasure.EraseAsync(user, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
                .Returns(new UserErasureOutcome(true, null));
    }
}
