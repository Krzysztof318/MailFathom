// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using MailKit;
using MailKit.Net.Imap;
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

    /// <summary>The clock measures idleness, so a connection still being used is never taken away mid-run.</summary>
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

        // Act
        await using var session = await harness.OpenSessionAsync();
        await session.SetSeenAsync(CreateOccurrenceId(42U), isSeen: true, CancellationToken.None);
        clock.Advance(IdlePeriod * 4);
        await session.SetSeenAsync(CreateOccurrenceId(43U), isSeen: true, CancellationToken.None);

        // Assert
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
