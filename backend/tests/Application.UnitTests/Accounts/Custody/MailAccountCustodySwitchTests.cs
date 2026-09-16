// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts.Custody;
using MailFathom.Application.Coordination;
using MailFathom.Application.Folders;
using MailFathom.Application.Mail;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Transport;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Accounts.Custody;

public sealed class MailAccountCustodySwitchTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("personal");

    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly MailTransportSecurityPolicy TransportPolicy = MailTransportSecurityPolicy.Create(
        MailConnectionSecurity.TlsOnConnect,
        MailAuthenticationPolicy.Create(
            [MailAuthenticationMechanism.Plain],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false),
        MailServerCertificateTrust.SystemTrustStore,
        trustedCertificateAuthorityReference: null);

    [Fact]
    public async Task SwitchAsync_NothingRefusesIt_RecordsTheRequestAndLeavesThePhaseWhereItStands()
    {
        // Arrange
        var context = new SwitchContext();

        // Act
        var outcome = await context.Switch.SwitchAsync(
            Account,
            MailAccountCustody.HoldMailbox,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(outcome);
        Assert.True(outcome.WasAccepted);
        Assert.Equal(MailAccountCustody.HoldMailbox, context.Custody.StateOf(Account)!.Requested);
        Assert.Equal(MailAccountCustodyPhase.Mirrored, context.Custody.StateOf(Account)!.Phase);
    }

    [Fact]
    public async Task SwitchAsync_AReplicaHoldsALeaseNamingNoBuild_RefusesTheSwitchNamingThatReplica()
    {
        // Arrange
        var context = new SwitchContext().WithLeaseHeldBy(WorkLeaseHolder.NewHold(), "replica-2");

        // Act
        var outcome = await context.Switch.SwitchAsync(
            Account,
            MailAccountCustody.HoldMailbox,
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.Single(outcome!.Refusals);
        Assert.Equal(MailAccountCustodySwitchRefusal.ReplicaOnBuildWithoutTheMode, refusal.Reason);
        Assert.Equal("replica-2", refusal.Subject);
        Assert.Equal(MailAccountCustody.MirrorSource, context.Custody.StateOf(Account)!.Requested);
    }

    /// <summary>
    /// A reading that filled its bound is a prefix of what the deployment holds, so an older build's lease may sit in
    /// the part nothing read. The refusal names no replica because there is none to name.
    /// </summary>
    [Fact]
    public async Task SwitchAsync_TheLeaseReadingFilledItsPage_RefusesTheSwitchNamingNoReplica()
    {
        // Arrange
        var context = new SwitchContext().WithALeaseReadingThatFillsItsPage();

        // Act
        var outcome = await context.Switch.SwitchAsync(
            Account,
            MailAccountCustody.HoldMailbox,
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.Single(outcome!.Refusals);
        Assert.Equal(MailAccountCustodySwitchRefusal.ReplicaOnBuildWithoutTheMode, refusal.Reason);
        Assert.Null(refusal.Subject);
        Assert.Equal(MailAccountCustody.MirrorSource, context.Custody.StateOf(Account)!.Requested);
    }

    [Fact]
    public async Task SwitchAsync_EveryLeaseNamesABuildThatKnowsTheMode_AcceptsTheSwitch()
    {
        // Arrange
        var context = new SwitchContext().WithLeaseHeldBy(WorkLeaseHolder.ForBuild("0.8.0"), "replica-2");

        // Act
        var outcome = await context.Switch.SwitchAsync(
            Account,
            MailAccountCustody.HoldMailbox,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.WasAccepted);
        Assert.Equal(MailAccountCustody.HoldMailbox, context.Custody.StateOf(Account)!.Requested);
    }

    [Fact]
    public async Task SwitchAsync_TheAccountSynchronizesAVirtualFolder_RefusesTheSwitchNamingTheAlias()
    {
        // Arrange
        var context = new SwitchContext().Mapping(
            MailFolderMapping.ToSpecialUse(MailFolderAlias.Create("everything"), MailFolderSpecialUse.All));

        // Act
        var outcome = await context.Switch.SwitchAsync(
            Account,
            MailAccountCustody.HoldMailbox,
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.Single(outcome!.Refusals);
        Assert.Equal(MailAccountCustodySwitchRefusal.SynchronizedVirtualFolder, refusal.Reason);
        Assert.Equal("EVERYTHING", refusal.Subject);
    }

    [Fact]
    public async Task SwitchAsync_SeveralReasonsRefuseIt_ReportsEveryOneOfThem()
    {
        // Arrange
        var context = new SwitchContext()
            .Mapping(MailFolderMapping.ToSpecialUse(MailFolderAlias.Create("everything"), MailFolderSpecialUse.All))
            .WithLeaseHeldBy(WorkLeaseHolder.NewHold(), "replica-2");

        // Act
        var outcome = await context.Switch.SwitchAsync(
            Account,
            MailAccountCustody.HoldMailbox,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [
                MailAccountCustodySwitchRefusal.SynchronizedVirtualFolder,
                MailAccountCustodySwitchRefusal.ReplicaOnBuildWithoutTheMode,
            ],
            outcome!.Refusals.Select(static refusal => refusal.Reason));
    }

    [Fact]
    public async Task SwitchAsync_AMappingNamesAFolderTheSourceDoesNotAdvertise_RefusesTheSwitchOff()
    {
        // Arrange
        var context = new SwitchContext(MailAccountCustody.HoldMailbox)
            .Mapping(MailFolderMapping.ToRemotePath(
                MailFolderAlias.Create("notes"),
                RemoteFolderPath.Create("Notes")))
            .Advertising(RemoteFolderPath.Create("INBOX"));

        // Act
        var outcome = await context.Switch.SwitchAsync(
            Account,
            MailAccountCustody.MirrorSource,
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.Single(outcome!.Refusals);
        Assert.Equal(MailAccountCustodySwitchRefusal.UnusableFolderMapping, refusal.Reason);
        Assert.Equal("NOTES", refusal.Subject);
        Assert.Equal(MailAccountCustody.HoldMailbox, context.Custody.StateOf(Account)!.Requested);
    }

    [Fact]
    public async Task SwitchAsync_AMappingMayCreateTheFolderItNames_AcceptsTheSwitchOff()
    {
        // Arrange
        var context = new SwitchContext(MailAccountCustody.HoldMailbox)
            .Mapping(MailFolderMapping.ToRemotePath(
                MailFolderAlias.Create("notes"),
                RemoteFolderPath.Create("Notes"),
                mayCreateMissingFolder: true))
            .Advertising(RemoteFolderPath.Create("INBOX"));

        // Act
        var outcome = await context.Switch.SwitchAsync(
            Account,
            MailAccountCustody.MirrorSource,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.WasAccepted);
        Assert.Equal(MailAccountCustody.MirrorSource, context.Custody.StateOf(Account)!.Requested);
    }

    [Fact]
    public async Task SwitchAsync_TheAccountIsAlreadyAtTheRequestedCustody_IsAcceptedWithoutLookingForARefusal()
    {
        // Arrange
        var context = new SwitchContext(MailAccountCustody.HoldMailbox)
            .WithLeaseHeldBy(WorkLeaseHolder.NewHold(), "replica-2");

        // Act
        var outcome = await context.Switch.SwitchAsync(
            Account,
            MailAccountCustody.HoldMailbox,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome!.WasAccepted);
    }

    [Fact]
    public async Task SwitchAsync_ARefusedSwitch_IsWrittenToTheAuditTrailWithItsReasons()
    {
        // Arrange
        var context = new SwitchContext().WithLeaseHeldBy(WorkLeaseHolder.NewHold(), "replica-2");

        // Act
        await context.Switch.SwitchAsync(
            Account,
            MailAccountCustody.HoldMailbox,
            TestContext.Current.CancellationToken);

        // Assert
        var decision = Assert.Single(context.Auditor.Decisions);
        Assert.Equal(Account, decision.Account);
        Assert.Equal(MailAccountCustody.MirrorSource, decision.From);
        Assert.Equal(MailAccountCustody.HoldMailbox, decision.To);
        Assert.Equal([MailAccountCustodySwitchRefusal.ReplicaOnBuildWithoutTheMode], decision.Refusals);
        Assert.Equal(Now, decision.DecidedAt);
    }

    [Fact]
    public async Task SwitchAsync_AnAcceptedSwitch_IsWrittenToTheAuditTrailNamingWhoAsked()
    {
        // Arrange
        var context = new SwitchContext();

        // Act
        await context.Switch.SwitchAsync(
            Account,
            MailAccountCustody.HoldMailbox,
            TestContext.Current.CancellationToken);

        // Assert
        var decision = Assert.Single(context.Auditor.Decisions);
        Assert.True(decision.WasAccepted);
        Assert.Equal("test-administrator", decision.RequestedBy);
    }

    [Fact]
    public async Task SwitchAsync_TheCallerMayNotWriteCustody_IsRefusedBeforeAnythingIsWritten()
    {
        // Arrange
        var context = new SwitchContext(granted: MailFathomPermission.AdminRead);

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            context.Switch.SwitchAsync(Account, MailAccountCustody.HoldMailbox, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminCustodyWrite, refusal.RequiredPermission);
        Assert.Equal(MailAccountCustody.MirrorSource, context.Custody.StateOf(Account)!.Requested);
        Assert.Empty(context.Auditor.Decisions);
    }

    [Fact]
    public async Task ReadAsync_TheCallerMayNotReadAdministratively_IsRefused()
    {
        // Arrange
        var context = new SwitchContext(granted: MailFathomPermission.AdminCustodyWrite);

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            context.Switch.ReadAsync(Account, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminRead, refusal.RequiredPermission);
    }

    [Fact]
    public async Task SwitchAsync_TheDeploymentHoldsNoSuchAccount_AnswersWithNothing()
    {
        // Arrange
        var context = new SwitchContext();

        // Act
        var outcome = await context.Switch.SwitchAsync(
            MailAccountId.Create("absent"),
            MailAccountCustody.HoldMailbox,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(outcome);
        Assert.Empty(context.Auditor.Decisions);
    }

    /// <summary>Arranges one deployment the switch is asked about, with every refusal absent until a test adds one.</summary>
    private sealed class SwitchContext
    {
        private readonly IMailFolderMappingReader mappings = Substitute.For<IMailFolderMappingReader>();
        private readonly IRemoteFolderCatalog remoteFolders = Substitute.For<IRemoteFolderCatalog>();
        private readonly IWorkLeaseStore leases = Substitute.For<IWorkLeaseStore>();

        internal SwitchContext(
            MailAccountCustody current = MailAccountCustody.MirrorSource,
            MailFathomPermission? granted = null)
        {
            this.Custody = InMemoryMailAccountCustodyStore.With(
                Account,
                new MailAccountCustodyState(current, MailAccountCustodyPhase.Mirrored));

            this.Mapping(MailFolderMapping.ToRemotePath(
                MailFolderAlias.Create("inbox"),
                RemoteFolderPath.Create("INBOX")));
            this.Advertising(RemoteFolderPath.Create("INBOX"));
            this.leases.ReadEveryHeldAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<WorkLease>>([]));

            var transportSecurity = Substitute.For<IMailTransportSecurityPolicyReader>();
            transportSecurity.GetPolicy(Arg.Any<MailAccountId>()).Returns(TransportPolicy);

            var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
            sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

            var clock = new FakeTimeProvider(Now);

            this.Switch = new MailAccountCustodySwitch(
                this.Custody,
                this.mappings,
                this.remoteFolders,
                transportSecurity,
                this.leases,
                this.Auditor,
                new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), clock),
                AccessAuthorizations.ForAdministratorGranted(granted ?? MailFathomPermission.AdminCustodyWrite),
                clock);
        }

        internal InMemoryMailAccountCustodyStore Custody { get; }

        internal RecordingMailAccountCustodyAuditor Auditor { get; } = new();

        internal MailAccountCustodySwitch Switch { get; }

        internal SwitchContext Mapping(params MailFolderMapping[] folders)
        {
            this.mappings.FoldersOf(Arg.Any<MailAccountId>()).Returns(folders);

            return this;
        }

        internal SwitchContext Advertising(params RemoteFolderPath[] paths)
        {
            this.remoteFolders.ListFoldersAsync(
                    Arg.Any<MailAccountId>(),
                    Arg.Any<MailTransportSecurityPolicy>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<RemoteFolder>>(
                    [.. paths.Select(static path => new RemoteFolder(path, []))]));

            return this;
        }

        /// <summary>Answers the lease reading with as many rows as it asked for, every one naming a build that knows the mode.</summary>
        /// <remarks>
        /// The bound is read from the call rather than restated here, so the arrangement stays the refusal's own
        /// condition — a full page — whatever number the switch decides to ask for.
        /// </remarks>
        internal SwitchContext WithALeaseReadingThatFillsItsPage()
        {
            this.leases.ReadEveryHeldAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult<IReadOnlyList<WorkLease>>(
                [
                    .. Enumerable.Range(0, call.Arg<int>()).Select(static index => new WorkLease(
                        WorkScope.Create($"mail-account:account-{index}"),
                        WorkLeaseHolder.ForBuild("0.8.0"),
                        ReplicaIdentity.Create("replica-2"),
                        Now.AddMinutes(1))),
                ]));

            return this;
        }

        internal SwitchContext WithLeaseHeldBy(WorkLeaseHolder holder, string replica)
        {
            this.leases.ReadEveryHeldAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<WorkLease>>([
                    new WorkLease(
                        WorkScope.Create($"mail-account:{Account.Value}"),
                        holder,
                        ReplicaIdentity.Create(replica),
                        Now.AddMinutes(1)),
                ]));

            return this;
        }

        private sealed class CommittingSession : IPersistenceSession
        {
            public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
                Task.FromResult(PersistenceCommitResult.Committed);

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    /// <summary>Keeps every decision the switch recorded, in the order it recorded them.</summary>
    private sealed class RecordingMailAccountCustodyAuditor : IMailAccountCustodyAuditor
    {
        internal List<MailAccountCustodyDecision> Decisions { get; } = [];

        public Task RecordAsync(MailAccountCustodyDecision decision, CancellationToken cancellationToken)
        {
            this.Decisions.Add(decision);

            return Task.CompletedTask;
        }
    }
}
