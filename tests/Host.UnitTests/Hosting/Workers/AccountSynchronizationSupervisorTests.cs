// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Folders;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Synchronization;
using MailFathom.Domain.Transport;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Hosting.Workers;
using MailFathom.Host.UnitTests.TestDoubles;
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
        var mirrorStore = new RecordingMailFolderMirrorStore();
        using var harness = CreateHarness(
            CreateOptionsWithArchiveUnmirrored(),
            sessionFactory,
            folderMirrorStore: mirrorStore);

        // Act, waiting on the pass that follows every folder the run scheduled.
        await harness.SuperviseUntilAsync(mirrorStore.FirstErasureReached);

        // Assert
        Assert.Equal(["INBOX"], attemptedFolders);
    }

    /// <summary>Stored mail no run will ever refresh again is taken away rather than left answering with the flags it had.</summary>
    [Fact]
    public async Task RunAsync_AFolderTheAccountNoLongerMirrors_ErasesWhatIsStoredForIt()
    {
        // Arrange
        var mirrorStore = new RecordingMailFolderMirrorStore();
        using var harness = CreateHarness(
            CreateOptionsWithArchiveUnmirrored(),
            Substitute.For<IMailboxSessionFactory>(),
            folderMirrorStore: mirrorStore);

        // Act
        await harness.SuperviseUntilAsync(mirrorStore.FirstErasureReached);

        // Assert
        Assert.Equal(
            [new MailFolderIdentity(MailAccountId.Create("primary"), MailFolderAlias.Create("ARCHIVE"))],
            mirrorStore.ErasedFolders);
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
            .ReadOutstandingAsync(Arg.Any<MailAccountId>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
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
            .ReadOutstandingAsync(Arg.Any<MailAccountId>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
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

    /// <summary>Configures one mirrored folder beside one the operator has switched synchronization off for.</summary>
    private static MailSynchronizationOptions CreateOptionsWithArchiveUnmirrored()
    {
        var options = SynchronizationTestHost.CreateSingleAccountOptions(enabled: true, "INBOX", "Archive");
        options.Accounts[0].Folders[1].Synchronize = false;

        return options;
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

    /// <summary>Models a folder work unit that is under way and stays there until the test lets it end.</summary>
    private static async Task<IMailboxSession> HoldUntilReleasedAsync(TaskCompletionSource release)
    {
        await release.Task;

        throw new InvalidOperationException("connect failed");
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
            unadvertisedAliases);

        return new SupervisorHarness(
            services,
            settings,
            clock,
            MailAccountId.Create(options.Accounts[0].AccountId));
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
            MailAccountId accountId)
        {
            this.services = services;
            this.Settings = settings;
            this.Clock = clock;
            this.Logger = new RecordingLogger<AccountSynchronizationSupervisor>();
            this.PushLogger = new RecordingLogger<AccountPushNotificationWatch>();
            this.supervisor = new AccountSynchronizationSupervisor(
                accountId,
                services.GetRequiredService<IServiceScopeFactory>(),
                settings,
                this.accountRunSlots,
                new AccountPushNotificationWatch(
                    accountId,
                    services.GetRequiredService<IServiceScopeFactory>(),
                    this.PushLogger,
                    clock),
                this.Logger,
                clock);
        }

        internal StubSettingsSnapshot<MailSynchronizationOptions> Settings { get; }

        internal FakeTimeProvider Clock { get; }

        internal RecordingLogger<AccountSynchronizationSupervisor> Logger { get; }

        internal RecordingLogger<AccountPushNotificationWatch> PushLogger { get; }

        internal Task StartSupervision() => this.supervisor.RunAsync(this.scheduling.Token, this.workUnits.Token);

        /// <summary>Cancels scheduling the way host shutdown does, leaving the work-unit token live for the drain.</summary>
        internal Task StopSchedulingAsync() => this.scheduling.CancelAsync();

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
