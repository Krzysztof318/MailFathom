// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Domain.Accounts;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Outbox;

public sealed class MailOutboxSignalTests
{
    private static readonly MailAccountIdentity Work =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("work"));
    private static readonly MailAccountIdentity Personal =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("personal"));

    /// <summary>A capacity below one would be a queue nothing can enter, so it is refused where it is stated.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_CapacityIsNotPositive_IsRefused(int capacity)
    {
        // Act
        var thrown = Assert.Throws<ArgumentOutOfRangeException>(() => new MailOutboxSignal(capacity));

        // Assert
        Assert.Equal("capacity", thrown.ParamName);
    }

    /// <summary>What the queue holds is accounts, so a hundred messages for one account are one pass rather than a hundred.</summary>
    [Fact]
    public void Signal_SameAccountRepeatedly_QueuesItOnce()
    {
        // Arrange
        var signal = new MailOutboxSignal(capacity: 8);

        // Act
        var signalled = Enumerable.Range(0, 100).Select(_ => signal.Signal(Work)).ToArray();

        // Assert
        Assert.All(signalled, Assert.True);
        Assert.Equal(1, signal.Depth);
    }

    /// <summary>Two accounts are two passes, because a claim takes one account's sends and no others.</summary>
    [Fact]
    public void Signal_TwoAccounts_QueuesBoth()
    {
        // Arrange
        var signal = new MailOutboxSignal(capacity: 8);

        // Act
        signal.Signal(Work);
        signal.Signal(Personal);

        // Assert
        Assert.Equal(2, signal.Depth);
    }

    /// <summary>The backpressure is explicit: a full queue refuses the signal and says so rather than growing.</summary>
    [Fact]
    public void Signal_QueueIsFull_IsRefusedAndReported()
    {
        // Arrange
        var signal = new MailOutboxSignal(capacity: 1);
        Assert.True(signal.Signal(Work));

        // Act
        var accepted = signal.Signal(Personal);

        // Assert
        Assert.False(accepted);
        Assert.Equal(1, signal.Depth);
    }

    /// <summary>A refused account is not left marked as pending, which would suppress its every later signal.</summary>
    [Fact]
    public async Task Signal_AfterARefusal_IsAcceptedOnceTheQueueDrains()
    {
        // Arrange
        var signal = new MailOutboxSignal(capacity: 1);
        signal.Signal(Work);
        Assert.False(signal.Signal(Personal));

        // Act
        await ReadAsync(signal, count: 1);
        var accepted = signal.Signal(Personal);

        // Assert
        Assert.True(accepted);
        Assert.Equal(1, signal.Depth);
    }

    /// <summary>An account is released as it is handed out, so a send written during its pass wakes another one.</summary>
    [Fact]
    public async Task ReadAllAsync_AccountSignalledAgainAfterItWasHandedOut_QueuesASecondPass()
    {
        // Arrange
        var signal = new MailOutboxSignal(capacity: 4);
        signal.Signal(Work);

        // Act
        var handedOut = await ReadAsync(signal, count: 1);
        var signalledAgain = signal.Signal(Work);

        // Assert
        Assert.Equal(Work.Id, Assert.Single(handedOut));
        Assert.True(signalledAgain);
        Assert.Equal(1, signal.Depth);
    }

    /// <summary>The enumeration ends when the host stops, rather than holding the delivery loop open.</summary>
    [Fact]
    public async Task ReadAllAsync_HostStops_EndsTheEnumeration()
    {
        // Arrange
        var signal = new MailOutboxSignal(capacity: 4);
        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();

        // Act
        var reading = async () =>
        {
            await foreach (var accountId in signal.ReadAllAsync(stopping.Token))
            {
                Assert.Fail($"Nothing was signalled, yet {accountId} was handed out.");
            }
        };

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(reading);
    }

    /// <summary>
    /// A queue refilled as fast as it drains still ends when the host stops. That is what a backlog large enough to
    /// fill every batch looks like, and a loop that only noticed the stop while waiting would run until it was killed.
    /// </summary>
    [Fact]
    public async Task ReadAllAsync_QueueNeverEmpties_StillEndsWhenTheHostStops()
    {
        // Arrange
        var signal = new MailOutboxSignal(capacity: 4);
        using var stopping = new CancellationTokenSource();
        signal.Signal(Work);

        // Act
        var reading = async () =>
        {
            await foreach (var accountId in signal.ReadAllAsync(stopping.Token))
            {
                // Exactly what a pass that filled its batch does, so the queue is never found empty.
                signal.Signal(accountId);

                await stopping.CancelAsync();
            }
        };

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(reading)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    private static async Task<IReadOnlyList<MailAccountId>> ReadAsync(MailOutboxSignal signal, int count)
    {
        List<MailAccountId> read = [];

        using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await foreach (var accountId in signal.ReadAllAsync(stopping.Token))
        {
            read.Add(accountId.Id);

            if (read.Count == count)
            {
                break;
            }
        }

        return read;
    }
}
