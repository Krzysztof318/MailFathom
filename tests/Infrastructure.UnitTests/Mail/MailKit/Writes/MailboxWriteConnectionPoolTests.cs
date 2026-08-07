// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using MailKit;
using MailKit.Net.Imap;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;
using static MailFathom.Infrastructure.UnitTests.TestDoubles.MailKitImapSessionTestContext;
using static MailFathom.Infrastructure.UnitTests.TestDoubles.MailKitImapWriteSessionTestContext;

namespace MailFathom.Infrastructure.UnitTests.Mail.MailKit.Writes;

/// <summary>
/// The bound this class exists for is one write connection per account. It is asserted through the connection sequence
/// rather than through a counter, because the sequence fails loudly on an establishment the test did not script: a
/// second connection is not a number that grew, it is a login the mail server counts against the account's limit.
/// </summary>
public sealed class MailboxWriteConnectionPoolTests
{
    private static readonly TimeSpan IdlePeriod = TimeSpan.FromMinutes(2);

    /// <summary>A run of changes must cost one handshake, not one per change.</summary>
    [Fact]
    public async Task LeaseAsync_TwiceInSequence_ReusesTheOneConnection()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var openFolder = CreateWritableFolder();
        var client = PrepareServer(new FakeImapClient { Capabilities = ImapCapabilities.UidPlus }, openFolder);
        await using var harness = CreateHarness(
            resilience,
            ConnectionSequence(client),
            new FakeTimeProvider(ObservedAt),
            new MailboxWriteSessionOptions { ConnectionIdlePeriod = IdlePeriod });

        // Act
        await using (var first = await harness.OpenSessionAsync())
        {
            await first.SetSeenAsync(CreateOccurrenceId(42U), isSeen: true, CancellationToken.None);
        }

        await using (var second = await harness.OpenSessionAsync())
        {
            await second.SetSeenAsync(CreateOccurrenceId(43U), isSeen: true, CancellationToken.None);
        }

        // Assert
        Assert.Equal(1, client.ConnectCount);
        await openFolder.Received(2).StoreAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Any<IStoreFlagsRequest>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>An account nobody is changing gives its connection slot back rather than holding one for ever.</summary>
    [Fact]
    public async Task LeaseAsync_AfterTheIdlePeriodElapsed_ClosesTheConnectionAndOpensAFreshOne()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var openFolder = CreateWritableFolder();
        var firstClient = PrepareServer(new FakeImapClient { Capabilities = ImapCapabilities.UidPlus }, openFolder);
        var secondClient = PrepareServer(new FakeImapClient { Capabilities = ImapCapabilities.UidPlus }, openFolder);
        var clock = new FakeTimeProvider(ObservedAt);
        await using var harness = CreateHarness(
            resilience,
            ConnectionSequence(firstClient, secondClient),
            clock,
            new MailboxWriteSessionOptions { ConnectionIdlePeriod = IdlePeriod });

        await using (var first = await harness.OpenSessionAsync())
        {
            await first.SetSeenAsync(CreateOccurrenceId(42U), isSeen: true, CancellationToken.None);
        }

        // Act
        clock.Advance(IdlePeriod);

        await using (var second = await harness.OpenSessionAsync())
        {
            await second.SetSeenAsync(CreateOccurrenceId(43U), isSeen: true, CancellationToken.None);
        }

        // Assert
        Assert.Equal(1, firstClient.DisconnectCount);
        Assert.Equal(1, secondClient.ConnectCount);
    }

    /// <summary>
    /// The clock measures idleness, so a connection still being used is never taken away mid-run. The first session is
    /// opened and disposed on purpose: the idle timer is created when a lease is released, so a test that only ever
    /// held a session would advance a clock with no callback armed and pass whether or not the guard that cancels a
    /// pending expiry on re-lease exists at all.
    /// </summary>
    [Fact]
    public async Task LeaseAsync_WhileASessionIsStillOpen_DoesNotExpireTheConnectionUnderIt()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var openFolder = CreateWritableFolder();
        var client = PrepareServer(new FakeImapClient { Capabilities = ImapCapabilities.UidPlus }, openFolder);
        var clock = new FakeTimeProvider(ObservedAt);
        await using var harness = CreateHarness(
            resilience,
            ConnectionSequence(client),
            clock,
            new MailboxWriteSessionOptions { ConnectionIdlePeriod = IdlePeriod });

        await using (var armingSession = await harness.OpenSessionAsync())
        {
            await armingSession.SetSeenAsync(CreateOccurrenceId(42U), isSeen: true, CancellationToken.None);
        }

        // Act
        await using var heldSession = await harness.OpenSessionAsync();
        clock.Advance(IdlePeriod * 4);

        // The eviction runs from a timer callback nothing awaits, so without waiting for it here the assertions below
        // would run before a wrongly-scheduled close had touched anything and pass whatever it went on to do.
        await harness.Pool.WaitForPendingEvictionsAsync();
        await heldSession.SetSeenAsync(CreateOccurrenceId(43U), isSeen: true, CancellationToken.None);

        // Assert
        Assert.Equal(0, client.DisconnectCount);
        Assert.Equal(1, client.ConnectCount);
    }

    /// <summary>
    /// A session disposed twice must release the account's gate once. A second release would raise the semaphore's
    /// count to two and let a caller take the connection while somebody else is still using it, which is the
    /// one-connection-per-account bound failing in the least visible way: nothing throws, and two mutations simply
    /// interleave on one IMAP connection.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_OnASessionDisposedTwice_ReleasesTheAccountOnlyOnce()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var openFolder = CreateWritableFolder();
        var client = PrepareServer(new FakeImapClient { Capabilities = ImapCapabilities.UidPlus }, openFolder);
        await using var harness = CreateHarness(
            resilience,
            ConnectionSequence(client),
            new FakeTimeProvider(ObservedAt),
            new MailboxWriteSessionOptions { ConnectionIdlePeriod = IdlePeriod });

        var doublyDisposedSession = await harness.OpenSessionAsync();
        await doublyDisposedSession.DisposeAsync();

        // Act
        await doublyDisposedSession.DisposeAsync();

        // Assert
        // The gate is proven to hold exactly one permit by contending for it: a second release would have left two, and
        // the contending lease would then complete immediately instead of waiting for the held one.
        var heldSession = await harness.OpenSessionAsync();
        var contendingSession = harness.OpenSessionAsync();

        Assert.False(contendingSession.IsCompleted);

        await heldSession.DisposeAsync();

        await using var resumedSession = await contendingSession;
        Assert.Equal(1, client.ConnectCount);
    }

    /// <summary>
    /// The bound is one connection per account at any moment, not one per sequence of callers. Every other test here
    /// disposes a session before opening the next, so none of them would notice a gate that stopped blocking: two
    /// overlapping mutations would each open their own connection and double the account's login count against a server
    /// that counts them. The scripted sequence carries one connection, so a second establishment fails the test rather
    /// than inflating a counter.
    /// </summary>
    [Fact]
    public async Task LeaseAsync_WhileAnotherSessionHoldsTheAccount_WaitsInsteadOfOpeningASecondConnection()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var openFolder = CreateWritableFolder();
        var client = PrepareServer(new FakeImapClient { Capabilities = ImapCapabilities.UidPlus }, openFolder);
        await using var harness = CreateHarness(
            resilience,
            ConnectionSequence(client),
            new FakeTimeProvider(ObservedAt),
            new MailboxWriteSessionOptions { ConnectionIdlePeriod = IdlePeriod });

        var heldSession = await harness.OpenSessionAsync();

        // Act
        var contendingSession = harness.OpenSessionAsync();

        // Assert
        // The gate is held, and a lease waiting on it cannot have run past its first await, so this is a state rather
        // than a race: no delay is waited on and no clock is consulted.
        Assert.False(contendingSession.IsCompleted);
        Assert.Equal(1, client.ConnectCount);

        await heldSession.DisposeAsync();

        await using var resumedSession = await contendingSession;
        await resumedSession.SetSeenAsync(CreateOccurrenceId(42U), isSeen: true, CancellationToken.None);

        Assert.Equal(1, client.ConnectCount);
        Assert.Equal(0, client.DisconnectCount);
    }

    /// <summary>
    /// A connection is pinned to the folder it selected. Serving a second folder over the held connection would make
    /// the bound one connection per folder instead of one per account, so the connection is replaced.
    /// </summary>
    [Fact]
    public async Task LeaseAsync_ForASecondFolderOfTheAccount_ReplacesTheConnectionRatherThanAddingOne()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var openFolder = CreateWritableFolder();
        var firstClient = PrepareServer(new FakeImapClient { Capabilities = ImapCapabilities.UidPlus }, openFolder);
        var secondClient = PrepareServer(new FakeImapClient { Capabilities = ImapCapabilities.UidPlus }, openFolder);
        await using var harness = CreateHarness(
            resilience,
            ConnectionSequence(firstClient, secondClient),
            new FakeTimeProvider(ObservedAt),
            new MailboxWriteSessionOptions { ConnectionIdlePeriod = IdlePeriod });

        // Act
        await using (var inbox = await harness.OpenSessionAsync())
        {
            await inbox.SetSeenAsync(CreateOccurrenceId(42U), isSeen: true, CancellationToken.None);
        }

        await using (var drafts = await harness.OpenSessionAsync(DraftsFolder))
        {
            await drafts.SetSeenAsync(CreateOccurrenceIn(DraftsFolder, 43U), isSeen: true, CancellationToken.None);
        }

        // Assert
        Assert.Equal(1, firstClient.DisconnectCount);
        Assert.Equal(1, secondClient.ConnectCount);
    }

    /// <summary>
    /// The connection's own disposal logs out politely first, so a socket the server reset while the connection sat
    /// idle makes it throw. The scope resolved for that connection holds the account's settings provider and access
    /// token source, and it is the pool's to release whether or not the connection went quietly — otherwise the leak
    /// happens on exactly the path that meets a broken connection most often, where the failure is caught and logged.
    /// </summary>
    [Fact]
    public async Task IdleExpiry_WhenTheConnectionFailsToCloseCleanly_StillReleasesItsScope()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var openFolder = CreateWritableFolder();
        var client = PrepareServer(new FakeImapClient { Capabilities = ImapCapabilities.UidPlus }, openFolder);
        var clock = new FakeTimeProvider(ObservedAt);
        await using var harness = CreateHarness(
            resilience,
            ConnectionSequence(client),
            clock,
            new MailboxWriteSessionOptions { ConnectionIdlePeriod = IdlePeriod });

        await using (var session = await harness.OpenSessionAsync())
        {
            await session.SetSeenAsync(CreateOccurrenceId(42U), isSeen: true, CancellationToken.None);
        }

        client.DisconnectException = new IOException("the server reset the idle connection");

        // Act
        clock.Advance(IdlePeriod);

        // Assert
        Assert.Equal(1, harness.ScopeDisposals.Count);
        Assert.Contains(
            harness.RecordedLogs.Records,
            record => record.Level == LogLevel.Warning && record.Failure is IOException);
    }

    /// <summary>A host stopping must not leave an authenticated connection open against the mail server.</summary>
    [Fact]
    public async Task DisposeAsync_WithAConnectionHeld_ClosesIt()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var openFolder = CreateWritableFolder();
        var client = PrepareServer(new FakeImapClient { Capabilities = ImapCapabilities.UidPlus }, openFolder);
        var harness = CreateHarness(
            resilience,
            ConnectionSequence(client),
            new FakeTimeProvider(ObservedAt),
            new MailboxWriteSessionOptions { ConnectionIdlePeriod = IdlePeriod });

        await using (var session = await harness.OpenSessionAsync())
        {
            await session.SetSeenAsync(CreateOccurrenceId(42U), isSeen: true, CancellationToken.None);
        }

        // Act
        await harness.DisposeAsync();

        // Assert
        Assert.Equal(1, client.DisconnectCount);
    }
}
