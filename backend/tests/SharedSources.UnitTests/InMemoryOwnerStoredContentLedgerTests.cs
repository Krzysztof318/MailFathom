// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the per-owner stored-content figure every ceiling test measures an owner from.</summary>
/// <remarks>
/// A fault here reports somebody else's arrangement: a ledger that answered one owner's figure for every owner would
/// make a per-owner ceiling test pass while the ceiling bounded the deployment, and one that never counted its reads
/// would let a claim about how often a run measures pass without anything having been measured.
/// </remarks>
public sealed class InMemoryOwnerStoredContentLedgerTests
{
    [Fact]
    public async Task ReadStoredContentBytesAsync_AnOwnerHoldingNothing_AnswersWithZero()
    {
        // Arrange
        var ledger = new InMemoryOwnerStoredContentLedger();

        // Act
        var held = await ledger.ReadStoredContentBytesAsync(
            SyntheticMailOwner.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, held);
        Assert.Equal(1, ledger.ReadCount);
    }

    [Fact]
    public async Task ReadStoredContentBytesAsync_TwoOwnersHoldingDifferentAmounts_AnswersEachWithTheirOwn()
    {
        // Arrange
        var ledger = new InMemoryOwnerStoredContentLedger()
            .Holding(SyntheticMailOwner.Deployment, 4_096)
            .Holding(SyntheticMailOwner.Another, 512);

        // Act
        var deployment = await ledger.ReadStoredContentBytesAsync(
            SyntheticMailOwner.Deployment,
            TestContext.Current.CancellationToken);
        var another = await ledger.ReadStoredContentBytesAsync(
            SyntheticMailOwner.Another,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(4_096, deployment);
        Assert.Equal(512, another);
        Assert.Equal(2, ledger.ReadCount);
    }

    /// <summary>Re-deriving is counted apart from reading, which is what tells a maintained figure from a recomputed one.</summary>
    [Fact]
    public async Task RederiveStoredContentBytesAsync_AnOwnerHoldingPayloads_AnswersTheSameFigureAndCountsSeparately()
    {
        // Arrange
        var ledger = new InMemoryOwnerStoredContentLedger().Holding(SyntheticMailOwner.Deployment, 4_096);

        // Act
        var rederived = await ledger.RederiveStoredContentBytesAsync(
            SyntheticMailOwner.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(4_096, rederived);
        Assert.Equal(1, ledger.RederiveCount);
        Assert.Equal(0, ledger.ReadCount);
    }

    [Fact]
    public async Task ReadStoredContentBytesAsync_ACancelledToken_IsObserved()
    {
        // Arrange
        var ledger = new InMemoryOwnerStoredContentLedger();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act, Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => ledger.ReadStoredContentBytesAsync(SyntheticMailOwner.Deployment, cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => ledger.RederiveStoredContentBytesAsync(SyntheticMailOwner.Deployment, cancellation.Token));
    }
}
