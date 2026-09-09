// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Synchronization;
using MailFathom.Domain.Accounts;
using Xunit;

namespace MailFathom.Application.UnitTests.Synchronization;

/// <summary>Covers what ends an account's wait between synchronization runs, and what a wait it arrived beside does with it.</summary>
/// <remarks>
/// The whole of this type is about timing, so the cases are the orderings: brought forward while a wait is registered,
/// brought forward while none is, and brought forward for another account. Each is a real sequence — the record is
/// written by a request whose timing against the account's own loop nothing coordinates.
/// </remarks>
public sealed class MailAccountRunSignalTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("personal");
    private static readonly MailAccountId Another = MailAccountId.Create("work");

    [Fact]
    public void BringForward_AWaitAlreadyRegistered_EndsThatWait()
    {
        // Arrange
        var signal = new MailAccountRunSignal();
        using var waiting = signal.Register(Account, CancellationToken.None);

        // Act
        signal.BringForward(Account);

        // Assert
        Assert.True(waiting.Token.IsCancellationRequested);
    }

    /// <summary>The ordinary timing, because the record is written while the account is mid-run rather than while it waits.</summary>
    [Fact]
    public void Register_BroughtForwardWhileNothingWaited_ComesBackAlreadyEnded()
    {
        // Arrange
        var signal = new MailAccountRunSignal();

        // Act
        signal.BringForward(Account);

        using var waiting = signal.Register(Account, CancellationToken.None);

        // Assert
        Assert.True(waiting.Token.IsCancellationRequested);
    }

    /// <summary>A run brought forward for work already done would be a second early run on every change.</summary>
    [Fact]
    public void Register_AfterAWaitTookTheOneBroughtForward_WaitsAgain()
    {
        // Arrange
        var signal = new MailAccountRunSignal();
        signal.BringForward(Account);

        using (signal.Register(Account, CancellationToken.None))
        {
        }

        // Act
        using var waiting = signal.Register(Account, CancellationToken.None);

        // Assert
        Assert.False(waiting.Token.IsCancellationRequested);
    }

    [Fact]
    public void BringForward_AnotherAccount_LeavesThisAccountWaiting()
    {
        // Arrange
        var signal = new MailAccountRunSignal();
        using var waiting = signal.Register(Account, CancellationToken.None);

        // Act
        signal.BringForward(Another);

        // Assert
        Assert.False(waiting.Token.IsCancellationRequested);
    }

    /// <summary>The other reason a wait ends, which the caller tells apart from this one by reading its own token.</summary>
    [Fact]
    public void Register_TheHostStoppingScheduling_EndsTheWait()
    {
        // Arrange
        var signal = new MailAccountRunSignal();
        using var stopping = new CancellationTokenSource();
        using var waiting = signal.Register(Account, stopping.Token);

        // Act
        stopping.Cancel();

        // Assert
        Assert.True(waiting.Token.IsCancellationRequested);
    }

    /// <summary>A wait already over must not take the next one's, which would be a change waiting out the interval it was raised to avoid.</summary>
    [Fact]
    public void BringForward_AfterTheWaitItWouldHaveEndedWasDisposed_IsKeptForTheNextWait()
    {
        // Arrange
        var signal = new MailAccountRunSignal();

        using (signal.Register(Account, CancellationToken.None))
        {
        }

        // Act
        signal.BringForward(Account);

        // Assert
        using var waiting = signal.Register(Account, CancellationToken.None);

        Assert.True(waiting.Token.IsCancellationRequested);
    }
}
