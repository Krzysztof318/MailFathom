// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the per-user stored-content figure every ceiling test measures a user from.</summary>
/// <remarks>
/// A fault here reports somebody else's arrangement: a ledger that answered one user's figure for every user would
/// make a per-user ceiling test pass while the ceiling bounded the deployment, and one that never counted its reads
/// would let a claim about how often a run measures pass without anything having been measured.
/// </remarks>
public sealed class InMemoryUserStoredContentLedgerTests
{
    [Fact]
    public async Task ReadStoredContentBytesAsync_AnUserHoldingNothing_AnswersWithZero()
    {
        // Arrange
        var ledger = new InMemoryUserStoredContentLedger();

        // Act
        var held = await ledger.ReadStoredContentBytesAsync(
            SyntheticMailUser.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, held);
        Assert.Equal(1, ledger.ReadCount);
    }

    [Fact]
    public async Task ReadStoredContentBytesAsync_TwoUsersHoldingDifferentAmounts_AnswersEachWithTheirOwn()
    {
        // Arrange
        var ledger = new InMemoryUserStoredContentLedger()
            .Holding(SyntheticMailUser.Deployment, 4_096)
            .Holding(SyntheticMailUser.Another, 512);

        // Act
        var deployment = await ledger.ReadStoredContentBytesAsync(
            SyntheticMailUser.Deployment,
            TestContext.Current.CancellationToken);
        var another = await ledger.ReadStoredContentBytesAsync(
            SyntheticMailUser.Another,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(4_096, deployment);
        Assert.Equal(512, another);
        Assert.Equal(2, ledger.ReadCount);
    }

    /// <summary>Re-deriving is counted apart from reading, which is what tells a maintained figure from a recomputed one.</summary>
    [Fact]
    public async Task RederiveStoredContentBytesAsync_AnUserHoldingPayloads_AnswersTheSameFigureAndCountsSeparately()
    {
        // Arrange
        var ledger = new InMemoryUserStoredContentLedger().Holding(SyntheticMailUser.Deployment, 4_096);

        // Act
        var rederived = await ledger.RederiveStoredContentBytesAsync(
            SyntheticMailUser.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(4_096, rederived);
        Assert.Equal(1, ledger.RederiveCount);
        Assert.Equal(0, ledger.ReadCount);
    }

    /// <summary>The double refuses an unnamed user exactly as the persisted ledger does.</summary>
    /// <remarks>
    /// A fake that answered such a user from an entry keyed by an empty identifier would let a caller pass a test it
    /// would be refused by in a deployment, which is the one thing a double must not do.
    /// </remarks>
    [Fact]
    public async Task EveryMember_AnUserNamingNobody_IsRefused()
    {
        // Arrange
        var ledger = new InMemoryUserStoredContentLedger();

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => ledger.ReadStoredContentBytesAsync(default, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(
            () => ledger.RederiveStoredContentBytesAsync(default, TestContext.Current.CancellationToken));
        Assert.Equal(0, ledger.ReadCount);
        Assert.Equal(0, ledger.RederiveCount);
    }

    [Fact]
    public async Task ReadStoredContentBytesAsync_ACancelledToken_IsObserved()
    {
        // Arrange
        var ledger = new InMemoryUserStoredContentLedger();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act, Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => ledger.ReadStoredContentBytesAsync(SyntheticMailUser.Deployment, cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => ledger.RederiveStoredContentBytesAsync(SyntheticMailUser.Deployment, cancellation.Token));
    }
}
