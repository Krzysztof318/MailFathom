// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Configuration.OwnerSettings;
using MailFathom.Host.Configuration.OwnerSettings.Administration;
using MailFathom.Infrastructure.Persistence.Owners;
using MailFathom.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.OwnerSettings.Administration;

/// <summary>
/// Covers what an administrator does to the roster itself: who this deployment holds, who joins it, and who leaves.
/// The rules worth stating are the ones that stop a deployment from becoming one that serves the wrong person — the
/// several-owner refusal, the unique label, the bound on how many owners one deployment holds — and the one act that
/// disposes of everything recorded for somebody.
/// </summary>
public sealed class OwnerRosterAdministrationTests
{
    private const string AdministratorIdentity = "operations";

    [Fact]
    public async Task ReadRosterAsync_ADeploymentHoldingOwners_ReportsEachOneWithWhatThisProcessIsDoingAboutThem()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminRead);
        harness.Holding(
            new MailOwnerRecord(SyntheticMailOwner.Deployment, "alex", DocumentWrittenAtRuntime: true),
            new MailOwnerRecord(SyntheticMailOwner.Another, "morgan", DocumentWrittenAtRuntime: false));
        harness.Serving(SyntheticMailOwner.Deployment);

        // Act
        var roster = await harness.Roster.ReadRosterAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            ["alex recordIsTheirOwn served", "morgan"],
            roster.Select(entry => string.Join(
                ' ',
                new[]
                {
                    entry.DisplayName,
                    entry.RecordIsTheirOwn ? "recordIsTheirOwn" : null,
                    entry.Served ? "served" : null,
                }.OfType<string>())));
    }

    /// <summary>
    /// A declaration is what decides whether an owner can be erased or usefully relabelled, and it is read from a file
    /// this process composed rather than from anything the row carries — so an administrator reading the roster is
    /// told, instead of finding out from a refusal.
    /// </summary>
    [Fact]
    public async Task ReadRosterAsync_AnOwnerAConfigurationSourceDeclares_ReportsThemAsDeclaredInConfiguration()
    {
        // Arrange
        var harness = new RosterHarness(
            MailFathomPermission.AdminRead,
            declaredInConfiguration: SyntheticMailOwner.Deployment);
        harness.Holding(
            new MailOwnerRecord(SyntheticMailOwner.Deployment, "alex", DocumentWrittenAtRuntime: false),
            new MailOwnerRecord(SyntheticMailOwner.Another, "morgan", DocumentWrittenAtRuntime: true));

        // Act
        var roster = await harness.Roster.ReadRosterAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [("alex", true), ("morgan", false)],
            roster.Select(entry => (entry.DisplayName, entry.DeclaredInConfiguration)));
    }

    /// <summary>
    /// One more than a deployment may declare is read, so a roster past the bound is observable rather than silently
    /// truncated into a listing an administrator would then act on as though it were complete.
    /// </summary>
    [Fact]
    public async Task ReadRosterAsync_AnyDeployment_ReadsOneMoreOwnerThanADeploymentMayHold()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminRead);

        // Act
        await harness.Roster.ReadRosterAsync(TestContext.Current.CancellationToken);

        // Assert
        await harness.Directory.Received(1).ReadOwnersAsync(
            DeclaredOwners.MaximumDeclaredOwners + 1,
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
    /// The identifier is minted here rather than supplied, and it is a version 4 value because an owner identifier
    /// reaches administrative APIs, audit records, and logs — and a time-ordered one would publish when each owner was
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
        Assert.Equal(4, outcome.Owner.Value.Version);
    }

    /// <summary>
    /// The record rather than only the envelope, because an owner nothing declares is served from their own record or
    /// from nothing at all — and the marker beside the document is what the next start reads to decide that.
    /// </summary>
    [Fact]
    public async Task ProvisionAsync_ALabelTheDeploymentAccepts_CommitsTheEmptyRecordBesideTheEnvelope()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);

        // Act
        var outcome = await harness.Roster.ProvisionAsync("alex", TestContext.Current.CancellationToken);

        // Assert
        await harness.Documents.Received(1).CommitAsync(
            outcome.Owner,
            "{}",
            1,
            Arg.Any<CancellationToken>());
        Assert.Contains(harness.ServedOwners.Owners, owner => owner.Owner == outcome.Owner);
    }

    /// <summary>A label is what an administrator selects an owner by, so two owners carrying one would leave nothing to select on.</summary>
    [Fact]
    public async Task ProvisionAsync_ALabelAnotherOwnerAlreadyCarries_IsRefusedWithoutWritingAnything()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(new MailOwnerRecord(SyntheticMailOwner.Deployment, "alex", DocumentWrittenAtRuntime: true));

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
            .ProvisionAsync(Arg.Any<MailOwnerId>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        var outcome = await harness.Roster.ProvisionAsync("alex", TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome.IsProvisioned);
        await harness.Documents.DidNotReceiveWithAnyArgs()
            .CommitAsync(default, default!, default, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The envelope and the record are two writes, so the owner can be removed between them — and an outcome reporting
    /// the provisioning as done would leave an administrator believing this deployment holds somebody it holds no
    /// record for, which is the one state every read of that owner then answers as an absence.
    /// </summary>
    [Fact]
    public async Task ProvisionAsync_AnOwnerRemovedBeforeTheirRecordWasWritten_IsRefusedRatherThanReportedAsProvisioned()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Documents
            .CommitAsync(Arg.Any<MailOwnerId>(), Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
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
            .ReadOwnersAsync(default, TestContext.Current.CancellationToken);
    }

    /// <summary>The rules the column and the declared collection are held to, asked here so a label refused in a file is refused over a route.</summary>
    [Fact]
    public async Task ProvisionAsync_ALabelPastWhatTheColumnStores_IsRefusedNamingTheBound()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);

        // Act
        var outcome = await harness.Roster.ProvisionAsync(
            new string('a', MailOwnerRecord.MaximumDisplayNameLength + 1),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome.IsProvisioned);
        Assert.Contains(
            MailOwnerRecord.MaximumDisplayNameLength.ToString(CultureInfo.InvariantCulture),
            outcome.RefusalMessage!,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A second owner beside an owner-facing surface that admits a caller naming nobody would leave that surface
    /// serving one person another person's mail, so the roster is held to one owner instead.
    /// </summary>
    [Fact]
    public async Task ProvisionAsync_ASecondOwnerWhileAnOwnerFacingSurfaceAdmitsACallerNamingNobody_IsRefused()
    {
        // Arrange
        var harness = new RosterHarness(
            MailFathomPermission.AdminConfigurationWrite,
            clientEndpoint: new ClientEndpointOptions { Enabled = true });
        harness.Holding(new MailOwnerRecord(SyntheticMailOwner.Deployment, "alex", DocumentWrittenAtRuntime: true));

        // Act
        var outcome = await harness.Roster.ProvisionAsync("morgan", TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome.IsProvisioned);
        Assert.Contains("requires no authentication", outcome.RefusalMessage!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The refusal is about a second owner rather than about the surface, so the first owner of a deployment serving an
    /// unauthenticated surface is still recorded — which is the deployment an easy first run produces.
    /// </summary>
    [Fact]
    public async Task ProvisionAsync_TheFirstOwnerWhileAnOwnerFacingSurfaceAdmitsACallerNamingNobody_IsRecorded()
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

    /// <summary>An administrator acts for the deployment rather than for a person, which is what makes recording a second owner reachable at all.</summary>
    [Fact]
    public async Task ProvisionAsync_ASecondOwnerWhileOnlyTheAdministrativeSurfaceIsServed_IsRecorded()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(new MailOwnerRecord(SyntheticMailOwner.Deployment, "alex", DocumentWrittenAtRuntime: true));

        // Act
        var outcome = await harness.Roster.ProvisionAsync("morgan", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.IsProvisioned);
    }

    [Fact]
    public async Task ProvisionAsync_ADeploymentAlreadyHoldingEveryOwnerItMay_IsRefusedNamingTheBound()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(
        [
            .. Enumerable.Range(0, DeclaredOwners.MaximumDeclaredOwners)
                .Select(position => new MailOwnerRecord(
                    MailOwnerId.Create(Guid.NewGuid()),
                    $"owner-{position}",
                    DocumentWrittenAtRuntime: true)),
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
    /// Whether the owner was served is read before the erasure rather than after, because the answer must describe the
    /// deployment the caller asked about rather than the one the erasure left.
    /// </summary>
    [Fact]
    public async Task EraseAsync_AnOwnerThisProcessIsServing_ReportsThatARestartIsOwed()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminErase);
        harness.Serving(SyntheticMailOwner.Deployment);
        harness.Erasing(SyntheticMailOwner.Deployment);

        // Act
        var outcome = await harness.Roster.EraseAsync(
            SyntheticMailOwner.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.OwnerErased);
        Assert.True(outcome.WasServed);
    }

    /// <summary>An erasure waits until a document write has published, so it cannot remove the owner between commit and publication.</summary>
    [Fact]
    public async Task EraseAsync_AnotherRosterWriteIsPublishing_WaitsBeforeDeletingTheOwner()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminErase);
        harness.Serving(SyntheticMailOwner.Deployment);
        harness.Erasing(SyntheticMailOwner.Deployment);
        await harness.ServedOwners.WaitForRosterPublicationAsync(TestContext.Current.CancellationToken);

        Task<OwnerErasureOutcome> erasing;
        try
        {
            // Act
            erasing = harness.Roster.EraseAsync(
                SyntheticMailOwner.Deployment,
                TestContext.Current.CancellationToken);

            // Assert
            Assert.False(erasing.IsCompleted);
            await harness.Erasure.DidNotReceiveWithAnyArgs().EraseAsync(default, CancellationToken.None);
        }
        finally
        {
            harness.ServedOwners.ReleaseRosterPublication();
        }

        Assert.True((await erasing).OwnerErased);
    }

    [Fact]
    public async Task EraseAsync_AnOwnerThisDeploymentDoesNotHold_ReportsThatNothingWasRemoved()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminErase);

        // Act
        var outcome = await harness.Roster.EraseAsync(
            SyntheticMailOwner.Another,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome.OwnerErased);
    }

    /// <summary>
    /// An owner a file declares is written back by the next start, so erasing them would dispose of their mail and
    /// hand the person straight back — with their mailboxes downloaded again. The refusal names the declaration to
    /// remove first rather than performing a deletion the deployment would undo.
    /// </summary>
    [Fact]
    public async Task EraseAsync_AnOwnerADeclarationNames_IsRefusedNamingWhatToRemoveFirst()
    {
        // Arrange
        var harness = new RosterHarness(
            MailFathomPermission.AdminErase,
            declaredInConfiguration: SyntheticMailOwner.Deployment);
        harness.Erasing(SyntheticMailOwner.Deployment);

        // Act
        var outcome = await harness.Roster.EraseAsync(
            SyntheticMailOwner.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome.OwnerErased);
        Assert.NotNull(outcome.RefusalMessage);
        await harness.Erasure.DidNotReceiveWithAnyArgs().EraseAsync(default, CancellationToken.None);
    }

    /// <summary>
    /// The deployment's own mail-account section names no owner, so the sole owner it is attributed to is declared by
    /// that section exactly as a listed owner is declared by theirs — and the next start supplies them again.
    /// </summary>
    [Fact]
    public async Task EraseAsync_AnOwnerServedFromTheDeploymentsOwnSection_IsRefusedNamingWhatToRemoveFirst()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminErase);
        harness.ServingFromTheDeploymentSection(SyntheticMailOwner.Deployment);
        harness.Erasing(SyntheticMailOwner.Deployment);

        // Act
        var outcome = await harness.Roster.EraseAsync(
            SyntheticMailOwner.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome.OwnerErased);
        Assert.True(outcome.WasServed);
        Assert.NotNull(outcome.RefusalMessage);
        await harness.Erasure.DidNotReceiveWithAnyArgs().EraseAsync(default, CancellationToken.None);
    }

    [Fact]
    public async Task EraseAsync_AnOwnerNamingNobody_IsRefusedWithoutReachingTheErasure()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminErase);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => harness.Roster.EraseAsync(default, TestContext.Current.CancellationToken));

        await harness.Erasure.DidNotReceiveWithAnyArgs().EraseAsync(default, TestContext.Current.CancellationToken);
    }

    /// <summary>Erasing somebody disposes of every message this deployment holds for them, which is a grant of its own.</summary>
    [Fact]
    public async Task EraseAsync_ACallerHoldingOnlyTheAdministrativeConfigurationWrite_IsRefused()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);

        // Act & Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => harness.Roster.EraseAsync(SyntheticMailOwner.Deployment, TestContext.Current.CancellationToken));
    }

    /// <summary>A label is what an administrator selects an owner by, and nothing is keyed by it, so replacing one is an ordinary write.</summary>
    [Fact]
    public async Task RelabelAsync_AnOwnerThisDeploymentHolds_PutsTheLabelOnTheirRow()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(new MailOwnerRecord(SyntheticMailOwner.Deployment, "alexandra", DocumentWrittenAtRuntime: true));

        // Act
        var outcome = await harness.Roster.RelabelAsync(
            SyntheticMailOwner.Deployment,
            "alex",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.IsRelabelled);
        await harness.Provisioning.Received(1)
            .RelabelAsync(SyntheticMailOwner.Deployment, "alex", Arg.Any<CancellationToken>());
    }

    /// <summary>The label is trimmed the way a declared one is, so a roster is never told apart by trailing space.</summary>
    [Fact]
    public async Task RelabelAsync_ALabelWrittenWithSurroundingSpace_WritesItTrimmed()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(new MailOwnerRecord(SyntheticMailOwner.Deployment, "alexandra", DocumentWrittenAtRuntime: true));

        // Act
        await harness.Roster.RelabelAsync(
            SyntheticMailOwner.Deployment,
            "  alex  ",
            TestContext.Current.CancellationToken);

        // Assert
        await harness.Provisioning.Received(1)
            .RelabelAsync(SyntheticMailOwner.Deployment, "alex", Arg.Any<CancellationToken>());
    }

    /// <summary>Two owners carrying one label would leave an administrator nothing to select on, which the column's index refuses.</summary>
    [Fact]
    public async Task RelabelAsync_ALabelAnotherOwnerCarries_IsRefusedWithoutReachingTheRow()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(
            new MailOwnerRecord(SyntheticMailOwner.Deployment, "alexandra", DocumentWrittenAtRuntime: true),
            new MailOwnerRecord(SyntheticMailOwner.Another, "alex", DocumentWrittenAtRuntime: true));

        // Act
        var outcome = await harness.Roster.RelabelAsync(
            SyntheticMailOwner.Deployment,
            "alex",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.OwnerHeld);
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
        harness.Holding(new MailOwnerRecord(SyntheticMailOwner.Deployment, "alexandra", DocumentWrittenAtRuntime: true));
        harness.Provisioning
            .RelabelAsync(Arg.Any<MailOwnerId>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        var outcome = await harness.Roster.RelabelAsync(
            SyntheticMailOwner.Deployment,
            "alex",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.OwnerHeld);
        Assert.NotNull(outcome.RefusalMessage);
        Assert.Contains("'alex'", outcome.RefusalMessage, StringComparison.Ordinal);
    }

    /// <summary>An owner this deployment does not hold is an absence to report rather than a label to refuse.</summary>
    /// <remarks>
    /// The two are one sentence to an administrator and two answers to a caller: the route publishes this one as the
    /// same absence every other owner-scoped route answers with, so the outcome carries which of them it is.
    /// </remarks>
    [Fact]
    public async Task RelabelAsync_AnOwnerThisDeploymentDoesNotHold_ReportsTheOwnerAsUnheld()
    {
        // Arrange
        var harness = new RosterHarness(MailFathomPermission.AdminConfigurationWrite);
        harness.Holding(new MailOwnerRecord(SyntheticMailOwner.Deployment, "alex", DocumentWrittenAtRuntime: true));

        // Act
        var outcome = await harness.Roster.RelabelAsync(
            SyntheticMailOwner.Another,
            "sam",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome.OwnerHeld);
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
            SyntheticMailOwner.Deployment,
            label,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.OwnerHeld);
        Assert.NotNull(outcome.RefusalMessage);
        await harness.Directory.DidNotReceiveWithAnyArgs()
            .ReadOwnersAsync(default, CancellationToken.None);
    }

    [Fact]
    public async Task RelabelAsync_AnOwnerNamingNobody_IsRefusedWithoutReachingTheRow()
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
                SyntheticMailOwner.Deployment,
                "alex",
                TestContext.Current.CancellationToken));
    }

    /// <summary>The roster over substituted rows, with the endpoint posture a deployment's several-owner refusal is read from.</summary>
    private sealed class RosterHarness
    {
        internal RosterHarness(
            MailFathomPermission granted,
            ClientEndpointOptions? clientEndpoint = null,
            MailOwnerId declaredInConfiguration = default)
        {
            var principals = Substitute.For<IAuthorizedPrincipalSource>();
            principals.Current.Returns(AuthorizedPrincipal.Caller(AdministratorIdentity, [granted]));

            this.Directory = Substitute.For<IMailOwnerDirectory>();
            this.Directory.ReadOwnersAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);

            this.Provisioning = Substitute.For<IMailOwnerProvisioning>();
            this.Provisioning
                .ProvisionAsync(Arg.Any<MailOwnerId>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(true);
            this.Provisioning
                .RelabelAsync(Arg.Any<MailOwnerId>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(true);

            this.Erasure = Substitute.For<IMailOwnerErasure>();
            this.Erasure.EraseAsync(Arg.Any<MailOwnerId>(), Arg.Any<CancellationToken>()).Returns(false);

            this.Documents = Substitute.For<IOwnerSettingsDocumentWriter>();
            this.Documents
                .CommitAsync(Arg.Any<MailOwnerId>(), Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
                .Returns((long?)2);

            // A roster naming somebody no test acts on, so "served" is a fact a test states rather than a default.
            this.ServedOwners.Resolved(
            [
                new(
                    MailOwnerId.Create(new Guid("99999999-9999-9999-9999-999999999999")),
                    "nobody-these-tests-name",
                    MailOwnerAccountSource.OwnerDocument,
                    []),
            ]);

            var settings = new ConfigurationBuilder()
                .AddInMemoryCollection(declaredInConfiguration.IsSpecified
                    ? new Dictionary<string, string?>
                    {
                        [$"{DeclaredOwnerOptions.SectionName}:0:{nameof(DeclaredOwnerOptions.Id)}"] =
                            declaredInConfiguration.Value.ToString(),
                        [$"{DeclaredOwnerOptions.SectionName}:0:{nameof(DeclaredOwnerOptions.DisplayName)}"] = "declared",
                    }
                    : [])
                .Build();

            this.Roster = new OwnerRosterAdministration(
                new AccessAuthorization(principals),
                this.Directory,
                this.Provisioning,
                this.Erasure,
                this.Documents,
                this.ServedOwners,
                new SeveralOwnerAdmission(
                    Options.Create(new McpEndpointOptions()),
                    Options.Create(clientEndpoint ?? new ClientEndpointOptions())),
                new ConfiguredOwnerMailAccounts(settings, this.ServedOwners),
                NullLogger<OwnerRosterAdministration>.Instance);
        }

        internal OwnerRosterAdministration Roster { get; }

        internal IMailOwnerDirectory Directory { get; }

        internal IMailOwnerProvisioning Provisioning { get; }

        internal IMailOwnerErasure Erasure { get; }

        internal IOwnerSettingsDocumentWriter Documents { get; }

        internal ServedMailOwners ServedOwners { get; } = new();

        internal void Holding(params MailOwnerRecord[] held) =>
            this.Directory.ReadOwnersAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(held);

        internal void Serving(MailOwnerId owner) =>
            this.ServedOwners.Resolved([new(owner, "served", MailOwnerAccountSource.OwnerDocument, [])]);

        internal void ServingFromTheDeploymentSection(MailOwnerId owner) =>
            this.ServedOwners.Resolved([new(owner, "served", MailOwnerAccountSource.DeploymentSection, [])]);

        internal void Erasing(MailOwnerId owner) =>
            this.Erasure.EraseAsync(owner, Arg.Any<CancellationToken>()).Returns(true);
    }
}
