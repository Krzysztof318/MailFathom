// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Folders;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Persistence;
using MailFathom.Application.Rules.Evaluation;
using MailFathom.Application.Signals;
using MailFathom.Application.Spam.Runs;
using MailFathom.Application.Synchronization;
using MailFathom.Application.Synchronization.Administration;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Common.Observability;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Synchronization;
using MailFathom.Domain.Transport;
using MailFathom.Host.Configuration.Mail;
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

public sealed class AccountSynchronizationSupervisorTests
{
    /// <summary>Guards against a hung supervisor. No assertion depends on how long a run actually takes.</summary>
    private static readonly TimeSpan DeadlockGuard = TimeSpan.FromSeconds(30);

    /// <summary>Moves a fake clock far enough for any backoff a five-minute interval produces to elapse.</summary>
    private static readonly TimeSpan AdvanceStep = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task RunAsync_FirstFolderFails_StillSynchronizesTheRemainingFolder()
    {
        // Arrange
        var attemptedFolders = new List<string>();
        var lastFolderAttempted = new TaskCompletionSource();
        var sessionFactory = CreateFailingSessionFactory(
            attemptedFolders,
            lastFolderAttempted,
            expectedFolderCount: 2,
            _ => new InvalidOperationException("connect failed"));
        using var harness = CreateHarness(
            SynchronizationTestHost.CreateSingleAccountOptions(enabled: true, "INBOX", "Archive"),
            sessionFactory);

        // Act
        await harness.SuperviseUntilAsync(lastFolderAttempted.Task);

        // Assert
        Assert.Equal(["INBOX", "ARCHIVE"], attemptedFolders);
    }

    [Fact]
    public async Task RunAsync_FolderDefersAfterAConcurrencyConflict_LogsTheDeferralAndContinues()
    {
        // Arrange
        var attemptedFolders = new List<string>();
        var lastFolderAttempted = new TaskCompletionSource();
        var sessionFactory = CreateFailingSessionFactory(
            attemptedFolders,
            lastFolderAttempted,
            expectedFolderCount: 2,
            folderAlias => folderAlias == "INBOX"
                ? new PersistenceConcurrencyConflictException("A competing writer won the race.")
                : new InvalidOperationException("connect failed"));
        using var harness = CreateHarness(
            SynchronizationTestHost.CreateSingleAccountOptions(enabled: true, "INBOX", "Archive"),
            sessionFactory);

        // Act
        await harness.SuperviseUntilAsync(lastFolderAttempted.Task);

        // Assert
        Assert.Contains(
            harness.Logger.Messages,
            message => message.Contains("Deferred IMAP folder synchronization for primary/INBOX", StringComparison.Ordinal));
    }

    /// <summary>A struggling mail server and a host that is shutting down must not read as the same event.</summary>
    [Fact]
    public async Task RunAsync_MailServerRefusesTheFolder_LogsItAsAServerDeferralAndContinues()
    {
        // Arrange
        var attemptedFolders = new List<string>();
        var lastFolderAttempted = new TaskCompletionSource();
        var sessionFactory = CreateFailingSessionFactory(
            attemptedFolders,
            lastFolderAttempted,
            expectedFolderCount: 2,
            folderAlias => folderAlias == "INBOX"
                ? new MailboxUnavailableException(
                    MailAccountId.Create("primary"),
                    MailFolderAlias.Create("INBOX"),
                    new TimeoutException("The server stopped answering."))
                : new InvalidOperationException("connect failed"));
        using var harness = CreateHarness(
            SynchronizationTestHost.CreateSingleAccountOptions(enabled: true, "INBOX", "Archive"),
            sessionFactory);

        // Act
        await harness.SuperviseUntilAsync(lastFolderAttempted.Task);

        // Assert
        Assert.Equal(["INBOX", "ARCHIVE"], attemptedFolders);
        Assert.Contains(
            harness.Logger.Messages,
            message => message.Contains("primary/INBOX because the mail server did not serve it", StringComparison.Ordinal));
    }

    /// <summary>An alias the server advertises no folder for is a configuration mistake, not a failed run.</summary>
    [Fact]
    public async Task RunAsync_AliasMatchesNoAdvertisedFolder_LogsItAndSynchronizesTheRemainingFolder()
    {
        // Arrange
        var attemptedFolders = new List<string>();
        var lastFolderAttempted = new TaskCompletionSource();
        var sessionFactory = CreateFailingSessionFactory(
            attemptedFolders,
            lastFolderAttempted,
            expectedFolderCount: 1,
            _ => new InvalidOperationException("connect failed"));
        using var harness = CreateHarness(
            SynchronizationTestHost.CreateSingleAccountOptions(enabled: true, "Archive", "INBOX"),
            sessionFactory,
            unadvertisedAliases: "ARCHIVE");

        // Act
        await harness.SuperviseUntilAsync(lastFolderAttempted.Task);

        // Assert
        Assert.Equal(["INBOX"], attemptedFolders);
        Assert.Contains(
            harness.Logger.Messages,
            message => message.Contains("Folder alias primary/ARCHIVE matched no folder", StringComparison.Ordinal));
    }

    /// <summary>A folder the operator stopped mirroring is not scheduled, which is what makes "no connection is opened for it" true.</summary>
    [Fact]
    public async Task RunAsync_AFolderTheAccountNoLongerMirrors_OpensNoSessionForIt()
    {
        // Arrange
        var attemptedFolders = new List<string>();
        var sessionFactory = CreateFailingSessionFactory(
            attemptedFolders,
            new TaskCompletionSource(),
            expectedFolderCount: 1,
            _ => new InvalidOperationException("connect failed"));
        var ruleEvaluationReached = new TaskCompletionSource();
        using var harness = CreateHarness(
            CreateOptionsWithArchiveUnmirrored(),
            sessionFactory,
            ruleEvaluationStore: CreateRuleStoreReporting(ruleEvaluationReached));

        // Act, waiting on the rule pass, which follows every folder the run scheduled.
        await harness.SuperviseUntilAsync(ruleEvaluationReached.Task);

        // Assert
        Assert.Equal(["INBOX"], attemptedFolders);
    }

    /// <summary>
    /// Mail a folder stored before its synchronization was switched off is kept, so turning the folder back on next
    /// month costs the mail that arrived meanwhile rather than the whole folder again.
    /// </summary>
    [Fact]
    public async Task RunAsync_AFolderTheAccountNoLongerMirrors_ErasesNothingOfWhatItStored()
    {
        // Arrange
        var mirrorStore = new RecordingMailFolderMirrorStore();
        var ruleEvaluationReached = new TaskCompletionSource();
        using var harness = CreateHarness(
            CreateOptionsWithArchiveUnmirrored(),
            Substitute.For<IMailboxSessionFactory>(),
            folderMirrorStore: mirrorStore,
            ruleEvaluationStore: CreateRuleStoreReporting(ruleEvaluationReached));

        // Act, waiting on the rule pass, which is past where an erasure pass would have run.
        await harness.SuperviseUntilAsync(ruleEvaluationReached.Task);

        // Assert
        Assert.Empty(mirrorStore.ErasedFolders);
    }

    /// <summary>
    /// No configuration value erases stored mail, which is what makes the erasure machinery something an operator
    /// reaches for rather than something a switch reaches for on their behalf.
    /// </summary>
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task RunAsync_WhicheverWayTheFolderSwitchesAreSet_ErasesNothing(
        bool synchronize,
        bool generateEmbeddings,
        bool visibleToTools)
    {
        // Arrange
        var options = SynchronizationTestHost.CreateSingleAccountOptions(enabled: true, "INBOX", "Archive");
        options.Accounts[0].Folders[1].Synchronize = synchronize;
        options.Accounts[0].Folders[1].GenerateEmbeddings = generateEmbeddings;
        options.Accounts[0].Folders[1].VisibleToTools = visibleToTools;
        var mirrorStore = new RecordingMailFolderMirrorStore();
        var ruleEvaluationReached = new TaskCompletionSource();
        using var harness = CreateHarness(
            options,
            Substitute.For<IMailboxSessionFactory>(),
            folderMirrorStore: mirrorStore,
            ruleEvaluationStore: CreateRuleStoreReporting(ruleEvaluationReached));

        // Act
        await harness.SuperviseUntilAsync(ruleEvaluationReached.Task);

        // Assert
        Assert.Empty(mirrorStore.ErasedFolders);
    }

    /// <summary>An ambiguous role and an alias that matches nothing need different remedies, so they are logged as different things.</summary>
    [Fact]
    public async Task RunAsync_AliasMatchesSeveralAdvertisedFolders_LogsTheAmbiguityAndTheRemedy()
    {
        // Arrange
        var listingRequested = new TaskCompletionSource();
        var catalog = Substitute.For<IRemoteFolderCatalog>();
        catalog
            .ListFoldersAsync(Arg.Any<MailAccountId>(), Arg.Any<MailTransportSecurityPolicy>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                listingRequested.TrySetResult();

                return Task.FromResult<IReadOnlyList<RemoteFolder>>(
                [
                    new RemoteFolder(RemoteFolderPath.Create("Archief", '/'), [MailFolderSpecialUse.Archive]),
                    new RemoteFolder(RemoteFolderPath.Create("Archive", '/'), [MailFolderSpecialUse.Archive]),
                ]);
            });
        var options = SynchronizationTestHost.CreateSingleAccountOptions(enabled: true);
        options.Accounts[0].Folders = [new MailFolderMappingOptions { Alias = "archive", SpecialUse = "Archive" }];
        using var harness = CreateHarness(options, Substitute.For<IMailboxSessionFactory>(), remoteFolderCatalog: catalog);

        // Act
        await harness.SuperviseUntilAsync(listingRequested.Task);

        // Assert
        Assert.Contains(
            harness.Logger.Messages,
            message => message.Contains("primary/ARCHIVE matched several folders", StringComparison.Ordinal)
                && message.Contains("configure its RemotePath", StringComparison.Ordinal));
    }

    /// <summary>Options validation should have caught it, but a folder that reaches the supervisor unusable must not end the account.</summary>
    [Fact]
    public async Task RunAsync_ConfiguredFolderCannotBecomeAMapping_FailsThatFolderAndContinues()
    {
        // Arrange
        var attemptedFolders = new List<string>();
        var lastFolderAttempted = new TaskCompletionSource();
        var sessionFactory = CreateFailingSessionFactory(
            attemptedFolders,
            lastFolderAttempted,
            expectedFolderCount: 1,
            _ => new InvalidOperationException("connect failed"));
        var options = SynchronizationTestHost.CreateSingleAccountOptions(enabled: true, "INBOX");
        options.Accounts[0].Folders.Insert(0, new MailFolderMappingOptions { Alias = "  ", RemotePath = "Archive" });
        using var harness = CreateHarness(options, sessionFactory);

        // Act
        await harness.SuperviseUntilAsync(lastFolderAttempted.Task);

        // Assert
        Assert.Equal(["INBOX"], attemptedFolders);
        Assert.Contains(
            harness.Logger.Messages,
            message => message.Contains("IMAP synchronization failed for primary/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_FolderFails_LogsNeitherTheUserNameNorTheSecretReference()
    {
        // Arrange
        var attemptedFolders = new List<string>();
        var lastFolderAttempted = new TaskCompletionSource();
        var sessionFactory = CreateFailingSessionFactory(
            attemptedFolders,
            lastFolderAttempted,
            expectedFolderCount: 1,
            _ => new InvalidOperationException("connect failed"));
        using var harness = CreateHarness(
            SynchronizationTestHost.CreateSingleAccountOptions(enabled: true, "INBOX"),
            sessionFactory);

        // Act
        await harness.SuperviseUntilAsync(lastFolderAttempted.Task);

        // Assert
        var logged = string.Join(' ', harness.Logger.Messages);
        Assert.DoesNotContain("mailfathom@example.test", logged, StringComparison.Ordinal);
        Assert.DoesNotContain("imap-primary-password", logged, StringComparison.Ordinal);
    }

    /// <summary>A cycle is one span with its folders beneath it, which is what attributes a stall to the step it stalled in.</summary>
    /// <remarks>
    /// Asserted here rather than only against the telemetry itself, because the shape depends on where the supervisor
    /// opens each scope: a folder span started outside the cycle's own would be a root span per folder, and a trace
    /// that no longer says which cycle a slow folder belonged to.
    /// </remarks>
    [Fact]
    public async Task RunAsync_ACycleOverTwoFolders_PublishesEachFolderBeneathTheCyclesOwnSpan()
    {
        // Arrange
        var secondFolderReached = new TaskCompletionSource();
        var openedFolderCount = 0;

        await using var emptyMailbox = CreateEmptyMailbox();
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        sessionFactory
            .OpenReadOnlyAsync(
                Arg.Any<MailAccountId>(),
                Arg.Any<MailFolderResolution>(),
                Arg.Any<MailTransportSecurityPolicy>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref openedFolderCount) == 2)
                {
                    secondFolderReached.TrySetResult();
                }

                return Task.FromResult(emptyMailbox);
            });

        using var spans = new SynchronizationSpanCollector("spans-its-folders");
        using var harness = CreateHarness(
            SynchronizationTestHost.CreateOptions(
                enabled: true,
                SynchronizationTestHost.CreateAccount("spans-its-folders", "INBOX", "Archive")),
            sessionFactory);

        // Act
        await harness.SuperviseUntilAsync(secondFolderReached.Task);

        // Assert
        var cycle = Assert.Single(spans.Named("synchronize_account"));
        var folders = spans.Named("synchronize_folder");

        Assert.Equal(
            ["INBOX", "ARCHIVE"],
            folders.Select(folder => (string?)folder.GetTagItem("mailfathom.mail.folder")));
        Assert.All(folders, folder => Assert.Equal(cycle.SpanId, folder.ParentSpanId));
    }

    /// <summary>One IMAP connection per account is the default, so two folders of one account never open at once.</summary>
    [Fact]
    public async Task RunAsync_FolderConcurrencyIsOne_NeverOpensTwoFoldersOfTheAccountAtOnce()
    {
        // Arrange
        var concurrency = new MailboxConcurrencyProbe(expectedEntryCount: 3, entriesToHoldTogether: 1);
        var options = SynchronizationTestHost.CreateSingleAccountOptions(enabled: true, "INBOX", "Archive", "Sent");
        using var harness = CreateHarness(options, concurrency.CreateSessionFactory());

        // Act
        await harness.SuperviseUntilAsync(concurrency.AllEntered);

        // Assert
        Assert.Equal(1, concurrency.MaxObservedConcurrency);
    }

    /// <summary>Raising the bound is what an operator with a fast server and many folders configures, so it has to take effect.</summary>
    [Fact]
    public async Task RunAsync_FolderConcurrencyRaised_SynchronizesThatManyFoldersAtOnce()
    {
        // Arrange
        var concurrency = new MailboxConcurrencyProbe(expectedEntryCount: 3, entriesToHoldTogether: 3);
        var options = SynchronizationTestHost.CreateSingleAccountOptions(enabled: true, "INBOX", "Archive", "Sent");
        options.MaxConcurrentFoldersPerAccount = 3;
        using var harness = CreateHarness(options, concurrency.CreateSessionFactory());

        // Act
        await harness.SuperviseUntilAsync(concurrency.AllEntered);

        // Assert
        Assert.Equal(3, concurrency.MaxObservedConcurrency);
    }

    /// <summary>A failed run defers the account rather than approaching a struggling server again on the ordinary interval.</summary>
    [Fact]
    public async Task RunAsync_RunFails_DefersTheAccountsNextRun()
    {
        // Arrange
        var attemptedFolders = new List<string>();
        var lastFolderAttempted = new TaskCompletionSource();
        var sessionFactory = CreateFailingSessionFactory(
            attemptedFolders,
            lastFolderAttempted,
            expectedFolderCount: 1,
            _ => new InvalidOperationException("connect failed"));
        using var harness = CreateHarness(
            SynchronizationTestHost.CreateSingleAccountOptions(enabled: true, "INBOX"),
            sessionFactory);

        // Act
        await harness.SuperviseUntilAsync(lastFolderAttempted.Task);

        // Assert
        Assert.Contains(
            harness.Logger.Messages,
            message => message.Contains("Account primary has failed 1 runs in a row", StringComparison.Ordinal));
    }

    /// <summary>
    /// The ledger is what an operator without a metrics stack reads, so a folder the mail server refused has to reach
    /// it classified rather than only reach the log. The wait beside it is the other half: a folder that is failing and
    /// an account that is approaching its server less often for it are one event read two ways.
    /// </summary>
    [Fact]
    public async Task RunAsync_MailServerRefusesTheFolder_RecordsTheDeferralAndTheWaitInTheLedger()
    {
        // Arrange
        var attemptedFolders = new List<string>();
        var lastFolderAttempted = new TaskCompletionSource();
        var sessionFactory = CreateFailingSessionFactory(
            attemptedFolders,
            lastFolderAttempted,
            expectedFolderCount: 1,
            folderAlias => new MailboxUnavailableException(
                MailAccountId.Create("primary"),
                MailFolderAlias.Create(folderAlias),
                new TimeoutException("The server stopped answering.")));
        using var harness = CreateHarness(
            SynchronizationTestHost.CreateSingleAccountOptions(enabled: true, "INBOX"),
            sessionFactory);

        // Act
        await harness.SuperviseUntilAsync(lastFolderAttempted.Task);

        // Assert
        var account = MailAccountId.Create("primary");
        var folder = harness.RunLedger.ReadFolder(new MailFolderIdentity(account, MailFolderAlias.Create("INBOX")));
        Assert.Equal(MailFolderRunOutcome.DeferredAfterMailServerUnavailable, folder?.Outcome);

        var state = harness.RunLedger.ReadAccount(account);
        Assert.Equal(1, state.ConsecutiveFailureCount);
        Assert.NotNull(state.NextRunDueAt);
    }

    /// <summary>
    /// A restart that cuts a folder short must reach the ledger as the restart it was. Classifying it as a failure
    /// would tell an operator to go looking for a defect, and leaving it unrecorded would leave the folder reading as
    /// whatever the run before the restart made it.
    /// </summary>
    [Fact]
    public async Task RunAsync_DeploymentStopsWhileAFolderIsInFlight_RecordsTheFolderAsInterruptedByShutdown()
    {
        // Arrange
        var folderEntered = new TaskCompletionSource();
        var releaseFolder = new TaskCompletionSource();
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        sessionFactory
            .OpenReadOnlyAsync(
                Arg.Any<MailAccountId>(),
                Arg.Any<MailFolderResolution>(),
                Arg.Any<MailTransportSecurityPolicy>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                folderEntered.TrySetResult();

                return HoldUntilWorkUnitCancelledAsync(releaseFolder, call.Arg<CancellationToken>());
            });
        using var harness = CreateHarness(
            SynchronizationTestHost.CreateSingleAccountOptions(enabled: true, "INBOX"),
            sessionFactory);
        var supervision = harness.StartSupervision();

        // Act
        await folderEntered.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        await harness.StopWorkUnitsAsync();
        releaseFolder.SetResult();
        await supervision.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Assert
        var account = MailAccountId.Create("primary");
        var folder = harness.RunLedger.ReadFolder(new MailFolderIdentity(account, MailFolderAlias.Create("INBOX")));
        Assert.Equal(MailFolderRunOutcome.InterruptedByShutdown, folder?.Outcome);

        // The restart is not the account's fault, so nothing about it grows the wait before the next run.
        Assert.Equal(0, harness.RunLedger.ReadAccount(account).ConsecutiveFailureCount);
    }

    /// <summary>
    /// A change the previous process left half-made is finished by the first run after a restart, with nobody asking
    /// for it. The account's own loop is what schedules that, which is why no separate worker exists for it.
    /// </summary>
    [Fact]
    public async Task RunAsync_EveryRun_ConvergesTheAccountsOutstandingMutationsBeforeItsFolders()
    {
        // Arrange
        var convergedBeforeAnyFolderWasOpened = new TaskCompletionSource<bool>();
        var recordStore = Substitute.For<IMailboxMutationRecordStore>();
        var folderWasOpened = 0;
        recordStore
            .ReadOutstandingAsync(Arg.Any<MailAccountIdentity>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                convergedBeforeAnyFolderWasOpened.TrySetResult(Volatile.Read(ref folderWasOpened) == 0);

                return Task.FromResult<IReadOnlyList<OutstandingMailboxMutation>>([]);
            });
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        sessionFactory
            .OpenReadOnlyAsync(
                Arg.Any<MailAccountId>(),
                Arg.Any<MailFolderResolution>(),
                Arg.Any<MailTransportSecurityPolicy>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Volatile.Write(ref folderWasOpened, 1);

                return Task.FromException<IMailboxSession>(new InvalidOperationException("connect failed"));
            });
        using var harness = CreateHarness(
            SynchronizationTestHost.CreateSingleAccountOptions(enabled: true, "INBOX"),
            sessionFactory,
            mutationRecordStore: recordStore);

        // Act
        await harness.SuperviseUntilAsync(convergedBeforeAnyFolderWasOpened.Task);

        // Assert
        Assert.True(await convergedBeforeAnyFolderWasOpened.Task);
    }

    /// <summary>
    /// A convergence pass that could not run defers the account exactly as a failed folder does, so a change waiting on
    /// an unreachable server is approached less often instead of once per interval forever.
    /// </summary>
    [Fact]
    public async Task RunAsync_ConvergencePassFails_DefersTheAccountsNextRun()
    {
        // Arrange
        var passFailed = new TaskCompletionSource();
        var recordStore = Substitute.For<IMailboxMutationRecordStore>();
        recordStore
            .ReadOutstandingAsync(Arg.Any<MailAccountIdentity>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<OutstandingMailboxMutation>>>(_ =>
            {
                passFailed.TrySetResult();

                throw new InvalidOperationException("The outstanding mutations could not be read.");
            });
        using var harness = CreateHarness(
            SynchronizationTestHost.CreateSingleAccountOptions(enabled: true, "INBOX"),
            Substitute.For<IMailboxSessionFactory>(),
            mutationRecordStore: recordStore);

        // Act
        await harness.SuperviseUntilAsync(passFailed.Task);

        // Assert
        Assert.Contains(
            harness.Logger.Messages,
            message => message.Contains(
                "Converging the outstanding mailbox mutations of account primary ended unexpectedly",
                StringComparison.Ordinal));
        Assert.Contains(
            harness.Logger.Messages,
            message => message.Contains("runs in a row", StringComparison.Ordinal));
    }

    /// <summary>An alias nobody advertises is fixed by an edit, so waiting longer for it would only slow the folders that work.</summary>
    [Fact]
    public async Task RunAsync_OnlyAnAliasFailedToResolve_DoesNotDeferTheAccount()
    {
        // Arrange
        var options = SynchronizationTestHost.CreateSingleAccountOptions(enabled: true, "Archive");
        var runFinished = new TaskCompletionSource();
        var catalog = Substitute.For<IRemoteFolderCatalog>();
        catalog
            .ListFoldersAsync(Arg.Any<MailAccountId>(), Arg.Any<MailTransportSecurityPolicy>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                runFinished.TrySetResult();

                return Task.FromResult<IReadOnlyList<RemoteFolder>>([]);
            });
        using var harness = CreateHarness(options, Substitute.For<IMailboxSessionFactory>(), remoteFolderCatalog: catalog);

        // Act
        await harness.SuperviseUntilAsync(runFinished.Task);

        // Assert
        Assert.DoesNotContain(harness.Logger.Messages, message => message.Contains("runs in a row", StringComparison.Ordinal));
    }

    /// <summary>Backoff must not outlive the condition that caused it, so a run that works returns the account to its interval.</summary>
    [Fact]
    public async Task RunAsync_RunSucceedsBetweenTwoFailures_CountsTheSecondFailureAsTheFirstAgain()
    {
        // Arrange
        var attemptCount = 0;
        var thirdRunFailed = new TaskCompletionSource();

        // Built before the factory is configured: a substitute created inside a Returns callback would write its own
        // setup into the call context NSubstitute is already using for the call being answered.
        await using var emptyMailbox = CreateEmptyMailbox();
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        sessionFactory
            .OpenReadOnlyAsync(
                Arg.Any<MailAccountId>(),
                Arg.Any<MailFolderResolution>(),
                Arg.Any<MailTransportSecurityPolicy>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var attemptNumber = Interlocked.Increment(ref attemptCount);

                if (attemptNumber is not (1 or 3))
                {
                    return Task.FromResult(emptyMailbox);
                }

                if (attemptNumber == 3)
                {
                    thirdRunFailed.TrySetResult();
                }

                return Task.FromException<IMailboxSession>(new InvalidOperationException("connect failed"));
            });
        using var harness = CreateHarness(
            SynchronizationTestHost.CreateSingleAccountOptions(enabled: true, "INBOX"),
            sessionFactory);

        // Act
        await harness.SuperviseWhileAdvancingUntilAsync(thirdRunFailed.Task);

        // Assert
        var deferrals = harness.Logger.Messages
            .Where(message => message.Contains("runs in a row", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, deferrals.Length);
        Assert.All(deferrals, deferral => Assert.Contains("failed 1 runs in a row", deferral, StringComparison.Ordinal));
    }

    /// <summary>The drain lets work already in flight finish; it must not become the window a queued folder starts in.</summary>
    [Fact]
    public async Task RunAsync_SchedulingStopsMidRun_StartsNoneOfTheFoldersStillQueued()
    {
        // Arrange
        var attemptedFolders = new List<string>();
        var firstFolderEntered = new TaskCompletionSource();
        var releaseFirstFolder = new TaskCompletionSource();
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        sessionFactory
            .OpenReadOnlyAsync(
                Arg.Any<MailAccountId>(),
                Arg.Any<MailFolderResolution>(),
                Arg.Any<MailTransportSecurityPolicy>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                lock (attemptedFolders)
                {
                    attemptedFolders.Add(call.Arg<MailFolderResolution>()!.Alias.Value);
                }

                firstFolderEntered.TrySetResult();

                return HoldUntilReleasedAsync(releaseFirstFolder);
            });
        using var harness = CreateHarness(
            SynchronizationTestHost.CreateSingleAccountOptions(enabled: true, "INBOX", "Archive", "Sent"),
            sessionFactory);
        var supervision = harness.StartSupervision();

        // Act
        await firstFolderEntered.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        await harness.StopSchedulingAsync();
        releaseFirstFolder.SetResult();
        await supervision.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["INBOX"], attemptedFolders);
    }

    /// <summary>A cycle the host stopped mid-way skipped folders that raised no failure count, so it must not read as a clean run.</summary>
    /// <remarks>
    /// The folders still queued behind the folder bound return without being started, so nothing about them reaches
    /// the failure count the cycle would otherwise be judged by. Reporting that as a run that succeeded would put a
    /// spurious healthy point on the duration histogram at every shutdown that lands inside a cycle.
    /// </remarks>
    [Fact]
    public async Task RunAsync_SchedulingStopsMidRun_PublishesTheCycleAsInterruptedRatherThanSucceeded()
    {
        // Arrange
        var firstFolderEntered = new TaskCompletionSource();
        var releaseFirstFolder = new TaskCompletionSource();

        await using var emptyMailbox = CreateEmptyMailbox();
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        sessionFactory
            .OpenReadOnlyAsync(
                Arg.Any<MailAccountId>(),
                Arg.Any<MailFolderResolution>(),
                Arg.Any<MailTransportSecurityPolicy>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                firstFolderEntered.TrySetResult();

                return HoldUntilReleasedAsync(releaseFirstFolder, emptyMailbox);
            });

        using var spans = new SynchronizationSpanCollector("interrupted-mid-cycle");
        using var harness = CreateHarness(
            SynchronizationTestHost.CreateOptions(
                enabled: true,
                SynchronizationTestHost.CreateAccount("interrupted-mid-cycle", "INBOX", "Archive", "Sent")),
            sessionFactory);
        var supervision = harness.StartSupervision();

        // Act
        await firstFolderEntered.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        await harness.StopSchedulingAsync();
        releaseFirstFolder.SetResult();
        await supervision.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Assert
        var cycle = Assert.Single(spans.Named("synchronize_account"));

        Assert.Equal("interrupted", cycle.GetTagItem("mailfathom.mail.sync.outcome"));
        Assert.DoesNotContain(
            harness.Logger.Messages,
            message => message.Contains("finished in", StringComparison.Ordinal));
    }

    /// <summary>An account a reload removes is withdrawn work, not a failure, so its supervisor ends instead of connecting again.</summary>
    [Fact]
    public async Task RunAsync_AccountLeavesConfiguration_EndsSupervisionOfIt()
    {
        // Arrange
        var attemptedFolders = new List<string>();
        var firstFolderAttempted = new TaskCompletionSource();
        var sessionFactory = CreateFailingSessionFactory(
            attemptedFolders,
            firstFolderAttempted,
            expectedFolderCount: 1,
            _ => new InvalidOperationException("connect failed"));
        using var harness = CreateHarness(
            SynchronizationTestHost.CreateSingleAccountOptions(enabled: true, "INBOX"),
            sessionFactory);
        var supervision = harness.StartSupervision();

        // Act
        await firstFolderAttempted.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        harness.Settings.Current = SynchronizationTestHost.CreateOptions(enabled: true);
        await SynchronizationTestHost.AdvanceUntilAsync(harness.Clock, supervision, AdvanceStep, DeadlockGuard);

        // Assert
        Assert.Contains(
            harness.Logger.Messages,
            message => message.Contains("Account primary is no longer configured", StringComparison.Ordinal));
    }

    /// <summary>
    /// The acceptance criterion of push, asserted where it is observable: a change the server reports produces another
    /// pass through the ordinary synchronizer, over an ordinary read-only session, without the account's interval
    /// having elapsed. Nothing about the pass differs from the one polling would have run.
    /// </summary>
    [Fact]
    public async Task RunAsync_WatchedFolderChanges_RunsAnotherPassBeforeTheIntervalElapses()
    {
        // Arrange
        var options = SynchronizationTestHost.CreateSingleAccountOptions(enabled: true, "INBOX");
        options.Accounts[0].Mode = MailSynchronizationMode.Push;
        var passes = 0;
        var secondPassStarted = new TaskCompletionSource();
        await using var emptyMailbox = CreateEmptyMailbox();
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        sessionFactory
            .OpenReadOnlyAsync(
                Arg.Any<MailAccountId>(),
                Arg.Any<MailFolderResolution>(),
                Arg.Any<MailTransportSecurityPolicy>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref passes) == 2)
                {
                    secondPassStarted.TrySetResult();
                }

                return Task.FromResult(emptyMailbox);
            });
        var clock = new FakeTimeProvider();
        var notificationSessions = new FakeMailboxNotificationSessionFactory(clock);
        using var harness = CreateHarness(options, sessionFactory, notificationSessions);
        var supervision = harness.StartSupervision();

        // Act
        await notificationSessions.SessionOpened.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        var watchedInbox = notificationSessions.SessionWatching("INBOX")!;
        await watchedInbox.WaitStarted.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        watchedInbox.ReportFolderChange();
        await secondPassStarted.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        await harness.StopSchedulingAsync();
        await supervision;

        // Assert
        Assert.Equal(TimeSpan.Zero, harness.Clock.GetUtcNow() - harness.Clock.Start);
        Assert.Contains(
            harness.PushLogger.Messages,
            message => message.Contains("Mail server reported a change in primary/INBOX", StringComparison.Ordinal));
    }

    /// <summary>
    /// Switching a folder back on is an ordinary mirror: the next run schedules it exactly as it schedules a folder
    /// that was mapped mirrored from the start, with no branch that tells the two apart. What the folder kept while it
    /// was off is what makes that run a resumption rather than a remirror.
    /// </summary>
    [Fact]
    public async Task RunAsync_AFolderSwitchedBackOn_IsScheduledByTheNextRunLikeAnyOther()
    {
        // Arrange
        var attemptedFolders = new List<string>();
        var archiveAttempted = new TaskCompletionSource();
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        sessionFactory
            .OpenReadOnlyAsync(
                Arg.Any<MailAccountId>(),
                Arg.Any<MailFolderResolution>(),
                Arg.Any<MailTransportSecurityPolicy>(),
                Arg.Any<CancellationToken>())
            .Throws(call =>
            {
                var folderAlias = call.Arg<MailFolderResolution>()!.Alias.Value;

                lock (attemptedFolders)
                {
                    attemptedFolders.Add(folderAlias);
                }

                if (string.Equals(folderAlias, "ARCHIVE", StringComparison.Ordinal))
                {
                    archiveAttempted.TrySetResult();
                }

                return new InvalidOperationException("connect failed");
            });
        var firstRunReached = new TaskCompletionSource();
        using var harness = CreateHarness(
            CreateOptionsWithArchiveUnmirrored(),
            sessionFactory,
            ruleEvaluationStore: CreateRuleStoreReporting(firstRunReached));
        var supervision = harness.StartSupervision();

        // Act
        await firstRunReached.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        harness.Settings.Current = SynchronizationTestHost.CreateSingleAccountOptions(
            enabled: true,
            "INBOX",
            "Archive");
        await SynchronizationTestHost.AdvanceUntilAsync(
            harness.Clock,
            archiveAttempted.Task,
            AdvanceStep,
            DeadlockGuard);
        await harness.StopSchedulingAsync();
        await supervision;

        // Assert
        Assert.Contains("ARCHIVE", attemptedFolders);
    }

    /// <summary>Configures one mirrored folder beside one the operator has switched synchronization off for.</summary>
    private static MailSynchronizationOptions CreateOptionsWithArchiveUnmirrored()
    {
        var options = SynchronizationTestHost.CreateSingleAccountOptions(enabled: true, "INBOX", "Archive");
        options.Accounts[0].Folders[1].Synchronize = false;

        return options;
    }

    /// <summary>A rule may only see mail its own run has already committed, which is what running last is for.</summary>
    [Fact]
    public async Task RunAsync_RulesEvaluated_ReachesTheAccountOnlyAfterItsFoldersHaveRun()
    {
        // Arrange
        var evaluatedAfterTheFolder = new TaskCompletionSource<bool>();
        var folderWasOpened = 0;
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        sessionFactory
            .OpenReadOnlyAsync(
                Arg.Any<MailAccountId>(),
                Arg.Any<MailFolderResolution>(),
                Arg.Any<MailTransportSecurityPolicy>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Volatile.Write(ref folderWasOpened, 1);

                return Task.FromException<IMailboxSession>(new InvalidOperationException("connect failed"));
            });
        var ruleStore = Substitute.For<IMailRuleEvaluationStore>();
        ruleStore
            .GetEmailsAwaitingFirstEvaluationAsync(
                Arg.Any<MailAccountIdentity>(),
                Arg.Any<StoredEmailId?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                evaluatedAfterTheFolder.TrySetResult(Volatile.Read(ref folderWasOpened) == 1);

                return Task.FromResult<IReadOnlyList<StoredEmailAwaitingRuleEvaluation>>([]);
            });
        using var harness = CreateHarness(
            SynchronizationTestHost.CreateSingleAccountOptions(enabled: true, "INBOX"),
            sessionFactory,
            ruleEvaluationStore: ruleStore);

        // Act
        await harness.SuperviseUntilAsync(evaluatedAfterTheFolder.Task);

        // Assert
        Assert.True(await evaluatedAfterTheFolder.Task);
    }

    /// <summary>
    /// The arrival pipeline's order, asserted where it is composed. Classification runs first so that every later stage
    /// reads a verdict instead of deciding without one, the rules run over what it did not settle, and the cut runs last
    /// because a rule may still move the message into a folder mapped differently from the one it arrived in.
    /// </summary>
    /// <remarks>
    /// Classification is a seam in this release rather than a running job — nothing scores a message because it arrived,
    /// and what reaches this call site today is an operator asking for a run over a mailbox. What is asserted here is
    /// that the call site sits where the order requires it and that the run reaches it, which is the half of the stage
    /// that ships now.
    /// </remarks>
    [Fact]
    public async Task RunAsync_EveryRun_ClassifiesThenEvaluatesRulesThenCutsPassages()
    {
        // Arrange
        var localSteps = new ConcurrentQueue<string>();
        var cutReached = new TaskCompletionSource();
        var classificationRunStore = Substitute.For<ISpamClassificationRunStore>();
        classificationRunStore
            .FindOutstandingAsync(Arg.Any<MailAccountIdentity>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                localSteps.Enqueue("classification");

                return Task.FromResult<SpamClassificationRun?>(null);
            });
        var ruleStore = Substitute.For<IMailRuleEvaluationStore>();
        ruleStore
            .GetEmailsAwaitingFirstEvaluationAsync(
                Arg.Any<MailAccountIdentity>(),
                Arg.Any<StoredEmailId?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                localSteps.Enqueue("rules");

                return Task.FromResult<IReadOnlyList<StoredEmailAwaitingRuleEvaluation>>([]);
            });
        var chunkingStore = Substitute.For<IStoredEmailChunkingStore>();
        chunkingStore
            .GetEmailsAwaitingChunkingAsync(Arg.Any<MailAccountIdentity>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                localSteps.Enqueue("cut");
                cutReached.TrySetResult();

                return Task.FromResult<IReadOnlyList<StoredEmailAwaitingChunking>>([]);
            });
        using var harness = CreateHarness(
            SynchronizationTestHost.CreateSingleAccountOptions(enabled: true),
            Substitute.For<IMailboxSessionFactory>(),
            ruleEvaluationStore: ruleStore,
            classificationRunStore: classificationRunStore,
            chunkingStore: chunkingStore);

        // Act
        await harness.SuperviseUntilAsync(cutReached.Task);

        // Assert
        Assert.Equal(["classification", "rules", "cut"], localSteps.Take(3));
    }

    /// <summary>The cut reaches no mail server either, so failing one must not make the account fetch its mail less often.</summary>
    [Fact]
    public async Task RunAsync_CuttingPassagesFails_DoesNotDeferTheAccountsNextRun()
    {
        // Arrange
        var passFailed = new TaskCompletionSource();
        var chunkingStore = Substitute.For<IStoredEmailChunkingStore>();
        chunkingStore
            .GetEmailsAwaitingChunkingAsync(Arg.Any<MailAccountIdentity>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<StoredEmailAwaitingChunking>>>(_ =>
            {
                passFailed.TrySetResult();

                throw new InvalidOperationException("the cut failed");
            });
        using var harness = CreateHarness(
            SynchronizationTestHost.CreateSingleAccountOptions(enabled: true),
            Substitute.For<IMailboxSessionFactory>(),
            chunkingStore: chunkingStore);

        // Act
        await harness.SuperviseUntilAsync(passFailed.Task);

        // Assert
        Assert.Contains(
            harness.Logger.Messages,
            message => message.Contains(
                "Cutting the passages of the evaluated mail of account primary ended unexpectedly",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            harness.Logger.Messages,
            message => message.Contains("runs in a row", StringComparison.Ordinal));
    }

    /// <summary>Evaluation reaches no mail server, so failing one must not make the account fetch its mail less often.</summary>
    [Fact]
    public async Task RunAsync_RuleEvaluationFails_DoesNotDeferTheAccountsNextRun()
    {
        // Arrange
        var passFailed = new TaskCompletionSource();
        var ruleStore = Substitute.For<IMailRuleEvaluationStore>();
        ruleStore
            .GetEmailsAwaitingFirstEvaluationAsync(
                Arg.Any<MailAccountIdentity>(),
                Arg.Any<StoredEmailId?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<StoredEmailAwaitingRuleEvaluation>>>(_ =>
            {
                passFailed.TrySetResult();

                throw new InvalidOperationException("The rule candidates could not be read.");
            });
        using var harness = CreateHarness(
            SynchronizationTestHost.CreateSingleAccountOptions(enabled: true),
            Substitute.For<IMailboxSessionFactory>(),
            ruleEvaluationStore: ruleStore);

        // Act
        await harness.SuperviseUntilAsync(passFailed.Task);

        // Assert
        Assert.Contains(
            harness.Logger.Messages,
            message => message.Contains(
                "Evaluating the rules of account primary ended unexpectedly",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            harness.Logger.Messages,
            message => message.Contains("runs in a row", StringComparison.Ordinal));
    }

    /// <summary>
    /// Reports that a run has reached its rule pass, which is the signal a test waits on when what it asserts is that
    /// something did not happen: every folder the run scheduled is behind it by then.
    /// </summary>
    /// <remarks>
    /// The cut runs after this signal rather than before it, so the absences a test may assert on it are the ones the
    /// folders produce — a connection opened, an eraser reached for. An absence the cut could still fill is not one of
    /// them, and a test wanting that waits on something the pass itself reports.
    /// </remarks>
    private static IMailRuleEvaluationStore CreateRuleStoreReporting(TaskCompletionSource ruleEvaluationReached)
    {
        var ruleStore = Substitute.For<IMailRuleEvaluationStore>();
        ruleStore
            .GetEmailsAwaitingFirstEvaluationAsync(
                Arg.Any<MailAccountIdentity>(),
                Arg.Any<StoredEmailId?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                ruleEvaluationReached.TrySetResult();

                return Task.FromResult<IReadOnlyList<StoredEmailAwaitingRuleEvaluation>>([]);
            });

        return ruleStore;
    }

    /// <summary>A finished run leaves whatever a client draws behind its freshness line out of date, so it says so.</summary>
    [Fact]
    public async Task RunAsync_WhenARunFinishes_SignalsThatTheAccountsStateMoved()
    {
        // Arrange
        // The second run reaching the mail server is what proves the first one finished, which is the moment the
        // signal is raised at: stopping supervision on the first folder instead would leave the cycle interrupted,
        // and an interrupted cycle deliberately says nothing.
        var attemptCount = 0;
        var secondRunStarted = new TaskCompletionSource();
        await using var emptyMailbox = CreateEmptyMailbox();
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        sessionFactory
            .OpenReadOnlyAsync(
                Arg.Any<MailAccountId>(),
                Arg.Any<MailFolderResolution>(),
                Arg.Any<MailTransportSecurityPolicy>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref attemptCount) == 2)
                {
                    secondRunStarted.TrySetResult();
                }

                return Task.FromResult(emptyMailbox);
            });
        using var harness = CreateHarness(
            SynchronizationTestHost.CreateSingleAccountOptions(enabled: true, "INBOX"),
            sessionFactory);

        // Act
        await harness.SuperviseWhileAdvancingUntilAsync(secondRunStarted.Task);

        harness.SignalClock.Advance(ClientSignals.FoldingWindow);
        await harness.Signals.DrainAsync();

        // Assert
        var signal = Assert.Single(
            harness.SignalChannel.Published,
            published => published.Kind == ClientSignalKind.AccountState);
        Assert.Equal(SyntheticMailOwner.Deployment, signal.Owner);
        Assert.Equal(MailAccountId.Create("primary"), signal.Account);
        Assert.Null(signal.Folder);
        Assert.Empty(signal.Emails);
    }

    private static IMailboxSessionFactory CreateFailingSessionFactory(
        List<string> attemptedFolders,
        TaskCompletionSource runReached,
        int expectedFolderCount,
        Func<string, Exception> failureFor)
    {
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        sessionFactory
            .OpenReadOnlyAsync(
                Arg.Any<MailAccountId>(),
                Arg.Any<MailFolderResolution>(),
                Arg.Any<MailTransportSecurityPolicy>(),
                Arg.Any<CancellationToken>())
            .Throws(call =>
            {
                var folderAlias = call.Arg<MailFolderResolution>()!.Alias.Value;
                attemptedFolders.Add(folderAlias);

                // The supervisor loops until it is stopped, and its wait never elapses under a fake clock, so the test
                // observes exactly one run by counting the folders it reached rather than by waiting on time.
                if (attemptedFolders.Count == expectedFolderCount)
                {
                    runReached.TrySetResult();
                }

                return failureFor(folderAlias);
            });

        return sessionFactory;
    }

    /// <summary>Collects the synchronization spans one account published while a test ran.</summary>
    /// <remarks>
    /// Narrowed to one account, because the activity source is the application's own and every other class of this
    /// suite supervises an account of its own at the same moment. The spans are kept in the order they stopped, which
    /// is the order a run with one folder connection works its folders in.
    /// </remarks>
    private sealed class SynchronizationSpanCollector : IDisposable
    {
        private readonly ConcurrentQueue<Activity> stopped = new();
        private readonly ActivityListener listener;

        internal SynchronizationSpanCollector(string accountId)
        {
            this.listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == Telemetry.Name,
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                    ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    if (Equals(activity.GetTagItem("mailfathom.mail.account"), accountId))
                    {
                        this.stopped.Enqueue(activity);
                    }
                },
            };

            ActivitySource.AddActivityListener(this.listener);
        }

        public void Dispose() => this.listener.Dispose();

        internal IReadOnlyList<Activity> Named(string operationName) =>
            [.. this.stopped.Where(activity => activity.OperationName == operationName)];
    }

    /// <summary>Models a folder work unit that is under way and stays there until the test lets it end.</summary>
    private static async Task<IMailboxSession> HoldUntilReleasedAsync(TaskCompletionSource release)
    {
        await release.Task;

        throw new InvalidOperationException("connect failed");
    }

    /// <summary>Models a folder work unit that the drain deadline cuts short, which is what a deployment stopping does to one.</summary>
    private static async Task<IMailboxSession> HoldUntilWorkUnitCancelledAsync(
        TaskCompletionSource release,
        CancellationToken cancellationToken)
    {
        await release.Task;
        cancellationToken.ThrowIfCancellationRequested();

        throw new InvalidOperationException("The work unit was released without its token having been cancelled.");
    }

    /// <summary>Models the same work unit ending in a folder the server serves, for a test about what a held run finishes as.</summary>
    private static async Task<IMailboxSession> HoldUntilReleasedAsync(
        TaskCompletionSource release,
        IMailboxSession mailbox)
    {
        await release.Task;

        return mailbox;
    }

    /// <summary>Models a folder the server serves and that holds no email, which is the cheapest successful run there is.</summary>
    private static IMailboxSession CreateEmptyMailbox()
    {
        var mailbox = Substitute.For<IMailboxSession>();
        mailbox.GetUidValidityAsync(Arg.Any<CancellationToken>()).Returns(ImapUidValidity.Create(1));
        mailbox
            .GetEmailBatchAfterAsync(
                Arg.Any<ImapUid?>(),
                Arg.Any<int>(),
                Arg.Any<MailSynchronizationWindow>(),
                Arg.Any<CancellationToken>())
            .Returns(new RemoteEmailMetadataBatch([], InspectedThroughUid: null, HasMore: false));

        return mailbox;
    }

    private static SupervisorHarness CreateHarness(
        MailSynchronizationOptions options,
        IMailboxSessionFactory sessionFactory,
        FakeMailboxNotificationSessionFactory? notificationSessionFactory = null,
        IRemoteFolderCatalog? remoteFolderCatalog = null,
        IMailboxMutationRecordStore? mutationRecordStore = null,
        IStoredMailFolderMirrorStore? folderMirrorStore = null,
        IMailRuleEvaluationStore? ruleEvaluationStore = null,
        ISpamClassificationRunStore? classificationRunStore = null,
        IStoredEmailChunkingStore? chunkingStore = null,
        params string[] unadvertisedAliases)
    {
        var clock = new FakeTimeProvider();
        var settings = new StubSettingsSnapshot<MailSynchronizationOptions>(options);
        var services = SynchronizationTestHost.BuildServiceProvider(
            options,
            settings,
            sessionFactory,
            clock,
            notificationSessionFactory,
            remoteFolderCatalog,
            mutationRecordStore,
            folderMirrorStore,
            ruleEvaluationStore,
            classificationRunStore,
            chunkingStore,
            unadvertisedAliases);

        return new SupervisorHarness(
            services,
            settings,
            clock,
            MailAccountIdentity.Create(
                SyntheticMailOwner.Deployment,
                MailAccountId.Create(options.Accounts[0].AccountId)));
    }

    /// <summary>Holds the one supervisor a test drives, together with what it was composed from.</summary>
    private sealed class SupervisorHarness : IDisposable
    {
        private readonly ServiceProvider services;
        private readonly SemaphoreSlim accountRunSlots = new(1);
        private readonly CancellationTokenSource scheduling = new();
        private readonly CancellationTokenSource workUnits = new();
        private readonly AccountSynchronizationSupervisor supervisor;

        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership of the watch passes to the supervisor, which disposes it when its own supervision ends.")]
        internal SupervisorHarness(
            ServiceProvider services,
            StubSettingsSnapshot<MailSynchronizationOptions> settings,
            FakeTimeProvider clock,
            MailAccountIdentity account)
        {
            this.services = services;
            this.Settings = settings;
            this.Clock = clock;
            this.Logger = new RecordingLogger<AccountSynchronizationSupervisor>();
            this.PushLogger = new RecordingLogger<AccountPushNotificationWatch>();
            this.RunLedger = new MailSynchronizationRunLedger(clock);
            this.Signals = new ClientSignals([this.SignalChannel], this.SignalClock);
            this.supervisor = new AccountSynchronizationSupervisor(
                account,
                services.GetRequiredService<IServiceScopeFactory>(),
                settings,
                this.accountRunSlots,
                new AccountPushNotificationWatch(
                    account.Id,
                    services.GetRequiredService<IServiceScopeFactory>(),
                    this.PushLogger,
                    clock),
                services.GetRequiredService<MailSynchronizationTelemetry>(),
                this.RunLedger,
                this.Signals,
                this.Logger);
        }

        internal MailSynchronizationRunLedger RunLedger { get; }

        /// <summary>Gets what this run told a client, which most tests here have no claim about.</summary>
        internal RecordingClientSignalChannel SignalChannel { get; } = new();

        /// <summary>Gets the clock the signal window is measured against, which is a second one deliberately.</summary>
        /// <remarks>Not the supervisor's own: these tests advance that one by whole minutes to run a backoff out, and a folding window measured against it would tick hundreds of times per advance for a delivery no test is waiting on.</remarks>
        internal FakeTimeProvider SignalClock { get; } = new();

        /// <summary>Gets the publisher the supervisor raises through.</summary>
        internal ClientSignals Signals { get; }

        internal StubSettingsSnapshot<MailSynchronizationOptions> Settings { get; }

        internal FakeTimeProvider Clock { get; }

        internal RecordingLogger<AccountSynchronizationSupervisor> Logger { get; }

        internal RecordingLogger<AccountPushNotificationWatch> PushLogger { get; }

        internal Task StartSupervision() => this.supervisor.RunAsync(this.scheduling.Token, this.workUnits.Token);

        /// <summary>Cancels scheduling the way host shutdown does, leaving the work-unit token live for the drain.</summary>
        internal Task StopSchedulingAsync() => this.scheduling.CancelAsync();

        /// <summary>Ends the drain the way a host that waited long enough does, interrupting the work still in flight.</summary>
        internal Task StopWorkUnitsAsync() => this.workUnits.CancelAsync();

        /// <summary>Supervises the account until the awaited signal arrives, then stops scheduling and lets it finish.</summary>
        internal async Task SuperviseUntilAsync(Task signal)
        {
            var supervision = this.StartSupervision();

            await signal.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
            await this.scheduling.CancelAsync();
            await supervision;
        }

        /// <summary>Supervises the account across several runs, advancing the clock over the waits between them.</summary>
        internal async Task SuperviseWhileAdvancingUntilAsync(Task signal)
        {
            var supervision = this.StartSupervision();

            await SynchronizationTestHost.AdvanceUntilAsync(this.Clock, signal, AdvanceStep, DeadlockGuard);
            await this.scheduling.CancelAsync();
            await supervision;
        }

        public void Dispose()
        {
            this.scheduling.Dispose();
            this.workUnits.Dispose();
            this.accountRunSlots.Dispose();
            this.services.Dispose();
        }
    }
}
