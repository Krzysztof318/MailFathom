// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Signals;
using MailFathom.Host.UnitTests.TestDoubles;
using StackExchange.Redis;
using Xunit;

namespace MailFathom.Host.UnitTests.Signals;

/// <summary>
/// Covers how a committed configuration change is heard by the replicas that did not commit it, and that neither half
/// of it can fail the write that raised it or the replica that listens.
/// </summary>
public sealed class ConfigurationChangeAnnouncementsTests
{
    /// <summary>A change one replica announces reaches another replica listening on the same endpoint.</summary>
    [Fact]
    public async Task AnnounceAsync_OnOneReplica_ReachesAnotherReplicaListening()
    {
        // Arrange
        var backplane = new InMemoryBackplane();
        var heard = 0;
        await Over(backplane).ListenAsync(() => Interlocked.Increment(ref heard));

        // Act
        await Over(backplane).AnnounceAsync();

        // Assert
        Assert.Equal(1, heard);
    }

    /// <summary>
    /// With no backplane declared there is nothing to listen on and nothing to announce over, and neither is a failure
    /// worth attempting again: every replica converges on its own interval.
    /// </summary>
    [Fact]
    public async Task ListenAsync_NoBackplaneDeclared_ReportsNothingLeftToAttempt()
    {
        // Arrange
        var announcements = new ConfigurationChangeAnnouncements(
            connect: null,
            new RecordingLogger<ConfigurationChangeAnnouncements>());

        // Act
        var listening = await announcements.ListenAsync(() => { });
        await announcements.AnnounceAsync();

        // Assert
        Assert.True(listening);
    }

    /// <summary>
    /// A connection attempt that failed is made again rather than remembered, so a replica that started before its
    /// backplane answered still ends up listening.
    /// </summary>
    [Fact]
    public async Task ListenAsync_AfterAConnectionAttemptThatFailed_ConnectsAgain()
    {
        // Arrange
        var backplane = new InMemoryBackplane();
        var attempts = 0;
        var announcements = new ConfigurationChangeAnnouncements(
            () => ++attempts == 1 ? Unreachable() : Task.FromResult(backplane.Connect()),
            new RecordingLogger<ConfigurationChangeAnnouncements>());

        // Act
        var first = await announcements.ListenAsync(() => { });
        var second = await announcements.ListenAsync(() => { });

        // Assert
        Assert.False(first);
        Assert.True(second);
    }

    /// <summary>An announcement that cannot be published is dropped rather than failing the committed write that raised it.</summary>
    [Fact]
    public async Task AnnounceAsync_EndpointUnreachable_CompletesWithoutFailing()
    {
        // Arrange
        var announcements = new ConfigurationChangeAnnouncements(
            Unreachable,
            new RecordingLogger<ConfigurationChangeAnnouncements>());

        // Act
        var failure = await Record.ExceptionAsync(announcements.AnnounceAsync);

        // Assert
        Assert.Null(failure);
    }

    private static ConfigurationChangeAnnouncements Over(InMemoryBackplane backplane) =>
        new(() => Task.FromResult(backplane.Connect()), new RecordingLogger<ConfigurationChangeAnnouncements>());

    private static Task<ISubscriber> Unreachable() =>
        Task.FromException<ISubscriber>(
            new RedisConnectionException(ConnectionFailureType.UnableToConnect, "The endpoint did not answer."));
}
