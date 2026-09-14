// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Records;
using MailFathom.Host.Configuration.RootSettings;
using MailFathom.Host.Configuration.SensitiveContent;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.Hosting.Workers;
using MailFathom.Host.Signals;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Settings;
using MailFathom.Infrastructure.Persistence.Users;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using StackExchange.Redis;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Workers;

/// <summary>
/// Covers how a replica that did not commit a configuration change comes to serve it: at once where another replica's
/// announcement reaches it, and on its own interval where none does.
/// </summary>
public sealed class ConfigurationConvergenceWorkerTests
{
    /// <summary>Guards against a reading that never happens. No assertion depends on how long a run actually takes.</summary>
    private static readonly TimeSpan DeadlockGuard = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A commit another replica announces is republished here without the interval elapsing, which the clock that
    /// never moves in this test is what proves.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_AnotherReplicaAnnouncesACommit_RepublishesItBeforeTheInterval()
    {
        // Arrange
        var backplane = new InMemoryBackplane();
        var row = new InMemoryRootSettingsRow("""{ "Layered": { "Setting": "before" } }""", version: 3);
        var layer = LoadedFrom(row);
        var republished = RepublicationOf(layer);
        using var worker = Worker(() => Task.FromResult(backplane.Connect()), ReloaderOver(layer, row), NoUsers(), new FakeTimeProvider());

        await worker.StartAsync(CancellationToken.None);
        await backplane.FirstSubscription.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Act
        row.CommitFromElsewhere("""{ "Layered": { "Setting": "after" } }""");
        await AnnouncerOver(backplane).AnnounceAsync();
        await republished.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Assert
        await worker.StopAsync(CancellationToken.None);
        Assert.Equal(4, layer.Version);
        layer.TryGet("Layered:Setting", out var effective);
        Assert.Equal("after", effective);
    }

    /// <summary>
    /// A replica that hears no announcement — here because the deployment declares no backplane, and the same for one
    /// whose announcement was lost — still republishes the committed version once the interval elapses.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_NoAnnouncementReachesTheReplica_RepublishesTheCommitOnTheInterval()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var row = new InMemoryRootSettingsRow("""{ "Layered": { "Setting": "before" } }""", version: 3);
        var layer = LoadedFrom(row);
        var republished = RepublicationOf(layer);
        using var worker = Worker(connect: null, ReloaderOver(layer, row), NoUsers(), clock);

        row.CommitFromElsewhere("""{ "Layered": { "Setting": "after" } }""");

        // Act
        await worker.StartAsync(CancellationToken.None);
        await SynchronizationTestHost.AdvanceUntilAsync(clock, republished, ConfigurationConvergenceWorker.Interval, DeadlockGuard);

        // Assert
        await worker.StopAsync(CancellationToken.None);
        Assert.Equal(4, layer.Version);
    }

    /// <summary>A reading that fails is reported and does not end the worker, which reads again on the next announcement.</summary>
    [Fact]
    public async Task ExecuteAsync_AReadingFails_ReportsItAndReadsAgainOnTheNextAnnouncement()
    {
        // Arrange
        var backplane = new InMemoryBackplane();
        var firstReading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondReading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readings = 0;
        var documents = Substitute.For<IUserSettingsDocumentReader>();
        var logger = new RecordingLogger<ConfigurationConvergenceWorker>();

        documents.ReadVersionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref readings) == 1)
                {
                    firstReading.TrySetResult();

                    throw new InvalidOperationException("The database did not answer.");
                }

                secondReading.TrySetResult();

                return Task.FromResult<IReadOnlyList<UserSettingsDocumentVersion>>([]);
            });

        using var worker = Worker(() => Task.FromResult(backplane.Connect()), rootSettings: null, documents, new FakeTimeProvider(), logger);
        var announcer = AnnouncerOver(backplane);

        await worker.StartAsync(CancellationToken.None);
        await backplane.FirstSubscription.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Act
        await announcer.AnnounceAsync();
        await firstReading.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        await announcer.AnnounceAsync();
        await secondReading.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Assert
        await worker.StopAsync(CancellationToken.None);
        Assert.Contains(logger.Messages, message => message.Contains("could not compare", StringComparison.Ordinal));
    }

    private static RootSettingsConfigurationProvider LoadedFrom(InMemoryRootSettingsRow row)
    {
        var provider = new RootSettingsConfigurationProvider(new RootSettingsDocument(row.Json, row.Version));

        provider.Load();

        return provider;
    }

    private static Task RepublicationOf(RootSettingsConfigurationProvider layer)
    {
        var republished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        layer.GetReloadToken().RegisterChangeCallback(_ => republished.TrySetResult(), state: null);

        return republished.Task;
    }

    private static RootSettingsReloader ReloaderOver(RootSettingsConfigurationProvider layer, InMemoryRootSettingsRow row) =>
        new(layer, row, new RecordingLogger<RootSettingsReloader>());

    private static IUserSettingsDocumentReader NoUsers()
    {
        var documents = Substitute.For<IUserSettingsDocumentReader>();

        documents.ReadVersionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<UserSettingsDocumentVersion>>([]));

        return documents;
    }

    private static ConfigurationChangeAnnouncements AnnouncerOver(InMemoryBackplane backplane) =>
        new(() => Task.FromResult(backplane.Connect()), new RecordingLogger<ConfigurationChangeAnnouncements>());

    private static ConfigurationConvergenceWorker Worker(
        Func<Task<ISubscriber>>? connect,
        RootSettingsReloader? rootSettings,
        IUserSettingsDocumentReader documents,
        TimeProvider clock,
        RecordingLogger<ConfigurationConvergenceWorker>? logger = null)
    {
        var roster = new ServedMailUsers();

        roster.Resolved([]);

        var users = new ServedMailUsersConvergence(
            UserRecordScopes.Resolving(
                documents,
                new UserAccountDocumentBinder(
                    new PersistedSecretMaterial(DeclaredSecretScheme.Registered),
                    clock,
                    Options.Create(new SensitiveContentOptions()))),
            roster,
            new HeldBackRecords(),
            new RecordingLogger<ServedMailUsersConvergence>());

        return new ConfigurationConvergenceWorker(
            new ConfigurationChangeAnnouncements(connect, new RecordingLogger<ConfigurationChangeAnnouncements>()),
            users,
            rootSettings,
            clock,
            logger ?? new RecordingLogger<ConfigurationConvergenceWorker>());
    }
}
