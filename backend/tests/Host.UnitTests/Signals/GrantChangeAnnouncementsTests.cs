// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Grants;
using MailFathom.Domain.Access;
using MailFathom.Host.Signals;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Host.UnitTests.Signals;

/// <summary>Covers how a committed change to what users are granted reaches this replica's remembered grants and every other replica's.</summary>
public sealed class GrantChangeAnnouncementsTests
{
    /// <summary>The replica that committed a change forgets at once, without waiting on a backplane that may never deliver its own message back.</summary>
    [Fact]
    public async Task AnnounceAsync_NoBackplaneDeclared_ForgetsTheGrantsThisReplicaComputed()
    {
        // Arrange
        var grants = new UserGrantCache();
        grants.Remember(SyntheticUser.Deployment, ScopedGrant.AtDeployment([MailFathomPermission.MailRead]), grants.Generation);
        var announcements = new GrantChangeAnnouncements(
            grants,
            new ConfigurationChangeAnnouncements(connect: null, new RecordingLogger<ConfigurationChangeAnnouncements>()));

        // Act
        await announcements.AnnounceAsync();

        // Assert
        Assert.False(grants.TryRecall(SyntheticUser.Deployment, out _));
    }

    /// <summary>The other replicas hear the change through the announcement a configuration change travels as, which is what wakes them to forget theirs.</summary>
    [Fact]
    public async Task AnnounceAsync_OnOneReplica_ReachesAnotherReplicaListening()
    {
        // Arrange
        var backplane = new InMemoryBackplane();
        var heard = 0;
        await AnnouncementsOver(backplane).ListenAsync(() => Interlocked.Increment(ref heard));
        var announcements = new GrantChangeAnnouncements(new UserGrantCache(), AnnouncementsOver(backplane));

        // Act
        await announcements.AnnounceAsync();

        // Assert
        Assert.Equal(1, heard);
    }

    private static ConfigurationChangeAnnouncements AnnouncementsOver(InMemoryBackplane backplane) =>
        new(() => Task.FromResult(backplane.Connect()), new RecordingLogger<ConfigurationChangeAnnouncements>());
}
