// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Coordination;
using MailFathom.Application.Folders;
using MailFathom.Application.Synchronization;
using MailFathom.Application.Synchronization.Administration;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Transport;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.Hosting.Workers;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Observability;
using MailFathom.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Workers;

public sealed class MailSynchronizationCoordinatorTests
{
    /// <summary>Guards against a hung coordinator. No assertion depends on how long a run actually takes.</summary>
    private static readonly TimeSpan DeadlockGuard = TimeSpan.FromSeconds(30);

    /// <summary>Moves a fake clock past a shutdown drain in one step and to a supervision interval in a few.</summary>
    /// <remarks>
    /// Kept well inside a lease's renewal window, because the stepping loop can run several steps ahead of the code it is
    /// driving, and a hold that sees its clock jump past that window gives the account up as a paused process would.
    /// </remarks>
    private static readonly TimeSpan AdvanceStep = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task ExecuteAsync_SynchronizationDisabled_NeverOpensAMailbox()
    {
        // Arrange
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        using var harness = CreateHarness(
            SynchronizationTestHost.CreateSingleAccountOptions(enabled: false, "INBOX"),
            sessionFactory);

        // Act
        await harness.Coordinator.StartAsync(CancellationToken.None);
        await harness.Coordinator.ExecuteTask!;

        // Assert
        await sessionFactory.DidNotReceiveWithAnyArgs().OpenReadOnlyAsync(default!, default!, default!, CancellationToken.None);
    }

    /// <summary>A deployment that has configured nothing yet must start and stop like any other, and say so.</summary>
    [Fact]
    public async Task ExecuteAsync_NoAccountConfiguredAndSynchronizationDisabled_StartsAndStopsCleanly()
    {
        // Arrange
        using var harness = CreateHarness(
            SynchronizationTestHost.CreateOptions(enabled: false),
            Substitute.For<IMailboxSessionFactory>());

        // Act
        await harness.Coordinator.StartAsync(CancellationToken.None);
        await harness.Coordinator.ExecuteTask!;
        await harness.Coordinator.StopAsync(CancellationToken.None).WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            harness.LoggedMessages,
            message => message.Contains("IMAP synchronization is disabled", StringComparison.Ordinal));
    }

    /// <summary>The whole point of a supervisor per account: an account whose server never answers holds up no other one.</summary>
    [Fact]
    public async Task ExecuteAsync_OneAccountsServerNeverAnswers_StillSynchronizesTheOtherAccount()
    {
        // Arrange
        var unreachableAccountEntered = new TaskCompletionSource();
        var reachableAccountAttempted = new TaskCompletionSource();
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        sessionFactory
            .OpenReadOnlyAsync(
                Arg.Any<MailAccountId>(),
                Arg.Any<MailFolderResolution>(),
                Arg.Any<MailTransportSecurityPolicy>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<MailAccountId>().Value == "unreachable"
                ? NeverAnswerAsync(unreachableAccountEntered, call.Arg<CancellationToken>())
                : FailImmediately(reachableAccountAttempted));
        using var harness = CreateHarness(
            SynchronizationTestHost.CreateOptions(
                enabled: true,
                SynchronizationTestHost.CreateAccount("unreachable", "INBOX"),
                SynchronizationTestHost.CreateAccount("reachable", "INBOX")),
            sessionFactory);

        // Act
        await harness.Coordinator.StartAsync(CancellationToken.None);
        await unreachableAccountEntered.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        await reachableAccountAttempted.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(unreachableAccountEntered.Task.IsFaulted);
        await harness.StopAndDrainAsync();
    }

    /// <summary>The bound, not the length of the operator's account list, decides how much synchronization runs at once.</summary>
    [Fact]
    public async Task ExecuteAsync_MoreAccountsThanTheConcurrencyBound_NeverRunsMoreAccountsThanTheBound()
    {
        // Arrange
        var concurrency = new MailboxConcurrencyProbe(expectedEntryCount: 4, entriesToHoldTogether: 2);
        var options = SynchronizationTestHost.CreateOptions(
            enabled: true,
            SynchronizationTestHost.CreateAccount("first", "INBOX"),
            SynchronizationTestHost.CreateAccount("second", "INBOX"),
            SynchronizationTestHost.CreateAccount("third", "INBOX"),
            SynchronizationTestHost.CreateAccount("fourth", "INBOX"));
        options.MaxConcurrentAccounts = 2;
        using var harness = CreateHarness(options, concurrency.CreateSessionFactory());

        // Act
        await harness.Coordinator.StartAsync(CancellationToken.None);
        await concurrency.AllEntered.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, concurrency.MaxObservedConcurrency);
        await harness.StopAndDrainAsync();
    }

    /// <summary>A mailbox recorded while the process runs must start synchronizing without the host being restarted.</summary>
    [Fact]
    public async Task ExecuteAsync_MailboxAddedByARecordCommit_IsSupervisedWithoutARestart()
    {
        // Arrange
        var originalAccountAttempted = new TaskCompletionSource();
        var addedAccountAttempted = new TaskCompletionSource();
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        sessionFactory
            .OpenReadOnlyAsync(
                Arg.Any<MailAccountId>(),
                Arg.Any<MailFolderResolution>(),
                Arg.Any<MailTransportSecurityPolicy>(),
                Arg.Any<CancellationToken>())
            .Throws(call =>
            {
                var attemptedAccountId = call.Arg<MailAccountId>().Value;

                if (attemptedAccountId == "added")
                {
                    addedAccountAttempted.TrySetResult();
                }
                else if (attemptedAccountId == "original")
                {
                    originalAccountAttempted.TrySetResult();
                }

                return new InvalidOperationException("connect failed");
            });
        using var harness = CreateHarness(
            SynchronizationTestHost.CreateOptions(enabled: true, SynchronizationTestHost.CreateAccount("original", "INBOX")),
            sessionFactory);

        // Act
        await harness.Coordinator.StartAsync(CancellationToken.None);
        await originalAccountAttempted.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        harness.RecordMailboxes(
            SynchronizationTestHost.CreateAccount("original", "INBOX"),
            SynchronizationTestHost.CreateAccount("added", "INBOX"));
        await addedAccountAttempted.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Assert
        // Drained first, because the awaited signal is raised from inside the supervisor and the coordinator writes
        // this line after starting it. A supervisor that reaches its mail server before its first suspension therefore
        // raises the signal ahead of the line being logged, and asserting on the collection at that moment reads a
        // scheduling race as a missing log.
        await harness.StopAndDrainAsync();

        Assert.Contains(
            harness.LoggedMessages,
            message => message.Contains("Account added is now supervised", StringComparison.Ordinal));
    }

    /// <summary>A changed account starts from the new snapshot without waiting for the old schedule to elapse.</summary>
    [Fact]
    public async Task ExecuteAsync_AccountChangedByAReload_IsResupervisedWithoutARestart()
    {
        // Arrange
        var originalFolderAttempted = new TaskCompletionSource();
        var releaseOriginalWorkUnit = new TaskCompletionSource();
        var changedFolderAttempted = new TaskCompletionSource();
        var originalWorkUnitObservedCancellation = false;
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        sessionFactory
            .OpenReadOnlyAsync(
                Arg.Any<MailAccountId>(),
                Arg.Any<MailFolderResolution>(),
                Arg.Any<MailTransportSecurityPolicy>(),
                Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var attemptedFolder = call.Arg<MailFolderResolution>()!.Alias.Value;

                if (attemptedFolder == "ARCHIVE")
                {
                    changedFolderAttempted.TrySetResult();
                }
                else
                {
                    originalFolderAttempted.TrySetResult();
                    await releaseOriginalWorkUnit.Task;
                    originalWorkUnitObservedCancellation = call.Arg<CancellationToken>().IsCancellationRequested;
                }

                throw new InvalidOperationException("connect failed");
            });
        using var harness = CreateHarness(
            SynchronizationTestHost.CreateSingleAccountOptions(enabled: true, "INBOX"),
            sessionFactory,
            CatalogAdvertising("INBOX", "ARCHIVE"));

        // Act
        await harness.Coordinator.StartAsync(CancellationToken.None);
        await originalFolderAttempted.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        harness.Settings.Current = SynchronizationTestHost.CreateSingleAccountOptions(enabled: true, "ARCHIVE");
        Assert.False(changedFolderAttempted.Task.IsCompleted);
        releaseOriginalWorkUnit.SetResult();
        await changedFolderAttempted.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(originalWorkUnitObservedCancellation);
        await harness.StopAndDrainAsync();
    }

    /// <summary>A work unit is not torn down where it happens to be; shutdown stops scheduling and lets it finish.</summary>
    [Fact]
    public async Task ExecuteAsync_HostStopsWhileAWorkUnitRuns_LetsItFinishWithinTheDrain()
    {
        // Arrange
        var workUnitStarted = new TaskCompletionSource();
        var releaseWorkUnit = new TaskCompletionSource();
        var workUnitObservedCancellation = false;
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        sessionFactory
            .OpenReadOnlyAsync(
                Arg.Any<MailAccountId>(),
                Arg.Any<MailFolderResolution>(),
                Arg.Any<MailTransportSecurityPolicy>(),
                Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                workUnitStarted.TrySetResult();

                await releaseWorkUnit.Task;

                workUnitObservedCancellation = call.Arg<CancellationToken>().IsCancellationRequested;

                throw new InvalidOperationException("connect failed");
            });
        using var harness = CreateHarness(
            SynchronizationTestHost.CreateSingleAccountOptions(enabled: true, "INBOX"),
            sessionFactory);

        // Act
        await harness.Coordinator.StartAsync(CancellationToken.None);
        await workUnitStarted.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        var stopping = harness.Coordinator.StopAsync(CancellationToken.None);
        releaseWorkUnit.SetResult();
        await stopping.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(workUnitObservedCancellation);
    }

    /// <summary>The drain is bounded, so work that outlasts it is cancelled rather than holding shutdown open.</summary>
    [Fact]
    public async Task ExecuteAsync_WorkUnitOutlastsTheDrain_CancelsItAndSaysSo()
    {
        // Arrange
        var workUnitStarted = new TaskCompletionSource();
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        sessionFactory
            .OpenReadOnlyAsync(
                Arg.Any<MailAccountId>(),
                Arg.Any<MailFolderResolution>(),
                Arg.Any<MailTransportSecurityPolicy>(),
                Arg.Any<CancellationToken>())
            .Returns(call => NeverAnswerAsync(workUnitStarted, call.Arg<CancellationToken>()));
        using var harness = CreateHarness(
            SynchronizationTestHost.CreateSingleAccountOptions(enabled: true, "INBOX"),
            sessionFactory);

        // Act
        await harness.Coordinator.StartAsync(CancellationToken.None);
        await workUnitStarted.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        await harness.StopAndDrainAsync();

        // Assert
        Assert.Contains(
            harness.LoggedMessages,
            message => message.Contains("after shutdown began and was cancelled", StringComparison.Ordinal));
    }

    /// <summary>A second replica configured with the same account supervises nothing for it, and keeps asking on its own interval.</summary>
    [Fact]
    public async Task ExecuteAsync_AnotherReplicaHoldsTheAccount_OpensNoMailboxAndAsksAgainOnTheInterval()
    {
        // Arrange
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        var leases = new ScriptedWorkLeaseStore { HeldElsewhere = true };
        using var harness = CreateHarness(
            SynchronizationTestHost.CreateSingleAccountOptions(enabled: true, "INBOX"),
            sessionFactory,
            workLeaseStore: leases);

        // Act
        await harness.Coordinator.StartAsync(CancellationToken.None);
        await leases.WaitForClaimAsync(TestContext.Current.CancellationToken).WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        await SynchronizationTestHost.AdvanceUntilAsync(
            harness.Clock,
            leases.WaitForClaimAsync(TestContext.Current.CancellationToken),
            AdvanceStep,
            DeadlockGuard);

        // Assert
        await harness.StopAndDrainAsync();
        await sessionFactory.DidNotReceiveWithAnyArgs().OpenReadOnlyAsync(default!, default!, default!, CancellationToken.None);
        Assert.DoesNotContain(
            harness.LoggedMessages,
            message => message.Contains("is now supervised", StringComparison.Ordinal));
    }

    /// <summary>
    /// A replica that can no longer show it holds an account stops the run it has under way on that renewal, rather than
    /// letting it run on until the lease has expired and another replica may already be synchronizing the same account.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_HoldLostWhileARunIsUnderWay_CancelsTheRunBeforeTheLeaseCouldExpire()
    {
        // Arrange
        var workUnitStarted = new TaskCompletionSource();
        var workUnitCancelled = new TaskCompletionSource();
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        sessionFactory
            .OpenReadOnlyAsync(
                Arg.Any<MailAccountId>(),
                Arg.Any<MailFolderResolution>(),
                Arg.Any<MailTransportSecurityPolicy>(),
                Arg.Any<CancellationToken>())
            .Returns(call => NeverAnswerUntilCancelledAsync(workUnitStarted, workUnitCancelled, call.Arg<CancellationToken>()));
        var options = SynchronizationTestHost.CreateSingleAccountOptions(enabled: true, "INBOX");
        var leases = new ScriptedWorkLeaseStore();
        using var harness = CreateHarness(options, sessionFactory, workLeaseStore: leases);
        var claimedAt = harness.Clock.GetUtcNow();

        // Act
        await harness.Coordinator.StartAsync(CancellationToken.None);
        await workUnitStarted.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        leases.HeldElsewhere = true;
        harness.Clock.Advance(options.LeaseRenewalInterval);
        await workUnitCancelled.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(harness.Clock.GetUtcNow() - claimedAt < options.LeaseDuration);
        await harness.StopAndDrainAsync();
    }

    /// <summary>A replica that stops gracefully gives its accounts back, so another replica need not wait out the lease.</summary>
    [Fact]
    public async Task ExecuteAsync_HostStops_ReleasesTheAccountItHeld()
    {
        // Arrange
        var accountAttempted = new TaskCompletionSource();
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        sessionFactory
            .OpenReadOnlyAsync(
                Arg.Any<MailAccountId>(),
                Arg.Any<MailFolderResolution>(),
                Arg.Any<MailTransportSecurityPolicy>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => FailImmediately(accountAttempted));
        var leases = new ScriptedWorkLeaseStore();
        using var harness = CreateHarness(
            SynchronizationTestHost.CreateSingleAccountOptions(enabled: true, "INBOX"),
            sessionFactory,
            workLeaseStore: leases);

        // Act
        await harness.Coordinator.StartAsync(CancellationToken.None);
        await accountAttempted.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        var heldWhileRunning = leases.HeldScopes;
        await harness.StopAndDrainAsync();

        // Assert
        var held = Assert.Single(heldWhileRunning);
        Assert.Equal([held], leases.Releases);
        Assert.Empty(leases.HeldScopes);
    }

    /// <summary>An operator reading the lease table finds the account under the user and the name the configuration gave it.</summary>
    [Fact]
    public void SupervisionScope_AccountIdentifierFits_NamesTheUserAndTheAccount()
    {
        // Arrange
        var user = MailUserId.Create(new Guid("0197c0de-0000-7000-8000-000000001290"));

        // Act
        var scope = MailSynchronizationCoordinator.SupervisionScope(MailAccountIdentity.Create(user, MailAccountId.Create("primary")));

        // Assert
        Assert.Equal("mail-synchronization/0197c0de-0000-7000-8000-000000001290/primary", scope.Value);
    }

    /// <summary>
    /// Configuration accepts account identifiers far longer than a scope may be, and a scope that could not be composed
    /// would end supervision for every account on the replica; such an account is named by its digest instead.
    /// </summary>
    [Fact]
    public void SupervisionScope_AccountIdentifierTooLongForAScope_NamesEachAccountByItsOwnDigest()
    {
        // Arrange
        var user = MailUserId.Create(new Guid("0197c0de-0000-7000-8000-000000001290"));
        var first = MailAccountIdentity.Create(user, MailAccountId.Create(new string('a', 4000)));
        var second = MailAccountIdentity.Create(user, MailAccountId.Create(new string('b', 4000)));

        // Act
        var firstScope = MailSynchronizationCoordinator.SupervisionScope(first);
        var secondScope = MailSynchronizationCoordinator.SupervisionScope(second);

        // Assert
        Assert.StartsWith("mail-synchronization/0197c0de-0000-7000-8000-000000001290/sha256-", firstScope.Value, StringComparison.Ordinal);
        Assert.True(firstScope.Value.Length <= WorkScope.MaximumLength);
        Assert.Equal(firstScope, MailSynchronizationCoordinator.SupervisionScope(first));
        Assert.NotEqual(firstScope, secondScope);
    }

    /// <summary>Models a server that accepts the connection and then answers nothing until the caller gives up.</summary>
    private static async Task<IMailboxSession> NeverAnswerAsync(TaskCompletionSource entered, CancellationToken cancellationToken)
    {
        entered.TrySetResult();

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        throw new InvalidOperationException("The mail server answered after all.");
    }

    /// <summary>Models a server that answers nothing, and says when the caller gave up on it.</summary>
    private static async Task<IMailboxSession> NeverAnswerUntilCancelledAsync(
        TaskCompletionSource entered,
        TaskCompletionSource cancelled,
        CancellationToken cancellationToken)
    {
        try
        {
            return await NeverAnswerAsync(entered, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            cancelled.TrySetResult();

            throw;
        }
    }

    /// <summary>Models a server that refuses the connection at once, which is the cheapest failed work unit there is.</summary>
    private static Task<IMailboxSession> FailImmediately(TaskCompletionSource attempted)
    {
        attempted.TrySetResult();

        return Task.FromException<IMailboxSession>(new InvalidOperationException("connect failed"));
    }

    private static CoordinatorHarness CreateHarness(
        MailSynchronizationOptions options,
        IMailboxSessionFactory sessionFactory,
        IRemoteFolderCatalog? remoteFolderCatalog = null,
        ScriptedWorkLeaseStore? workLeaseStore = null)
    {
        var clock = new FakeTimeProvider();
        var settings = new StubSettingsSnapshot<MailSynchronizationOptions>(options);
        var services = SynchronizationTestHost.BuildServiceProvider(
            options,
            settings,
            sessionFactory,
            clock,
            remoteFolderCatalog: remoteFolderCatalog,
            workLeaseStore: workLeaseStore);

        return new CoordinatorHarness(services, settings, clock);
    }

    private static IRemoteFolderCatalog CatalogAdvertising(params string[] folders)
    {
        var catalog = Substitute.For<IRemoteFolderCatalog>();
        catalog.ListFoldersAsync(
                Arg.Any<MailAccountId>(),
                Arg.Any<MailTransportSecurityPolicy>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<RemoteFolder>>(
                [.. folders.Select(folder => new RemoteFolder(RemoteFolderPath.Create(folder), []))]));

        return catalog;
    }

    /// <summary>Holds the coordinator a test drives, together with what it was composed from.</summary>
    private sealed class CoordinatorHarness : IDisposable
    {
        private readonly ServiceProvider services;
        private readonly RecordingLoggerFactory loggerFactory = new();
        private long recordedDocumentVersion;

        internal CoordinatorHarness(
            ServiceProvider services,
            StubSettingsSnapshot<MailSynchronizationOptions> settings,
            FakeTimeProvider clock)
        {
            this.services = services;
            this.ServedUsers = services.GetRequiredService<ServedMailUsers>();
            this.Settings = settings;
            this.Clock = clock;
            this.Coordinator = new MailSynchronizationCoordinator(
                services.GetRequiredService<IServiceScopeFactory>(),
                settings,
                services.GetRequiredService<MailSynchronizationTelemetry>(),
                new MailSynchronizationRunLedger(clock),
                new MailAccountRunSignal(),
                ClientSignalPublishers.ReachingNobody,
                this.loggerFactory,
                clock);
        }

        internal StubSettingsSnapshot<MailSynchronizationOptions> Settings { get; }

        /// <summary>Gets the roster a record commit publishes into, which is where the accounts a run supervises come from.</summary>
        internal ServedMailUsers ServedUsers { get; }

        internal FakeTimeProvider Clock { get; }

        /// <summary>Gets everything the coordinator and its supervisors have logged, whichever category wrote it.</summary>
        internal IEnumerable<string> LoggedMessages => this.loggerFactory.Records.Select(record => record.Message);

        internal MailSynchronizationCoordinator Coordinator { get; }

        /// <summary>Commits one user's record, which is the only way a mailbox arrives while the process runs.</summary>
        /// <param name="mailAccounts">The mailboxes that user's document now holds.</param>
        /// <remarks>
        /// Both halves, because the composition root publishes both: the roster is what decides which accounts are
        /// served, and the settings snapshot is what a supervision pass wakes on.
        /// </remarks>
        internal void RecordMailboxes(params MailSynchronizationAccountOptions[] mailAccounts)
        {
            this.ServedUsers.UserDocumentPublished(
                SyntheticMailUser.Deployment,
                "user",
                new UserAccountOptions { MailAccounts = [.. mailAccounts] },
                ++this.recordedDocumentVersion);
            this.Settings.Current = this.Settings.Current.WithServedUsers(this.ServedUsers.Users);
        }

        /// <summary>Stops the coordinator and advances the clock, so work that ignores the drain is cancelled by it.</summary>
        internal Task StopAndDrainAsync() => SynchronizationTestHost.AdvanceUntilAsync(
            this.Clock,
            this.Coordinator.StopAsync(CancellationToken.None),
            AdvanceStep,
            DeadlockGuard);

        public void Dispose()
        {
            this.Coordinator.Dispose();
            this.loggerFactory.Dispose();
            this.services.Dispose();
        }
    }
}
