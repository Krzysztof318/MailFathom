// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Embeddings.Limits;

/// <summary>Covers what a period admits, what charging it does, and what a rolled-over period admits again.</summary>
public sealed class EmbeddingSpendGateTests
{
    private static readonly DateTimeOffset Midday = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReadCurrentPeriodAsync_NothingSpentYet_ReportsTheWholeCeilingAsRemaining()
    {
        // Arrange
        var gate = CreateGate(new InMemoryEmbeddingSpendLedger(), Bounded(1_000), new FakeTimeProvider(Midday));

        // Act
        var period = await gate.ReadCurrentPeriodAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new DateTimeOffset(2026, 8, 8, 0, 0, 0, TimeSpan.Zero), period.StartsAt);
        Assert.Equal(new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero), period.EndsAt);
        Assert.Equal(0, period.ConsumedInputCharacterCount);
        Assert.Equal(1_000, period.RemainingInputCharacterCount);
        Assert.True(period.AdmitsRequest);
    }

    /// <summary>The ceiling being reached is what a run reads before it spends, and it reads it from what was charged.</summary>
    [Fact]
    public async Task ReadCurrentPeriodAsync_TheCeilingHasBeenCharged_ReportsThePeriodAsAdmittingNothing()
    {
        // Arrange
        var ledger = new InMemoryEmbeddingSpendLedger();
        var gate = CreateGate(ledger, Bounded(1_000), new FakeTimeProvider(Midday));
        await gate.RecordSpendAsync(
            Substitute.For<IPersistenceSession>(),
            SyntheticMailOwner.Deployment,
            inputCharacterCount: 1_000,
            TestContext.Current.CancellationToken);

        // Act
        var period = await gate.ReadCurrentPeriodAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1_000, period.ConsumedInputCharacterCount);
        Assert.Equal(0, period.RemainingInputCharacterCount);
        Assert.False(period.AdmitsRequest);
    }

    /// <summary>
    /// The ceiling releases itself. Nothing acts on the ledger between the two reads below and nothing resets it — the
    /// clock reaching the next period is the whole mechanism, which is why a paused worker only has to wait.
    /// </summary>
    [Fact]
    public async Task ReadCurrentPeriodAsync_ThePeriodRollsOver_AdmitsRequestsAgainWithoutAnybodyClearingAnything()
    {
        // Arrange
        var ledger = new InMemoryEmbeddingSpendLedger();
        var timeProvider = new FakeTimeProvider(Midday);
        var gate = CreateGate(ledger, Bounded(1_000), timeProvider);
        await gate.RecordSpendAsync(
            Substitute.For<IPersistenceSession>(),
            SyntheticMailOwner.Deployment,
            inputCharacterCount: 1_200,
            TestContext.Current.CancellationToken);
        var exhausted = await gate.ReadCurrentPeriodAsync(TestContext.Current.CancellationToken);

        // Act
        timeProvider.Advance(exhausted.EndsAt - Midday);
        var rolledOver = await gate.ReadCurrentPeriodAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(exhausted.AdmitsRequest);
        Assert.True(rolledOver.AdmitsRequest);
        Assert.Equal(exhausted.EndsAt, rolledOver.StartsAt);
        Assert.Equal(0, rolledOver.ConsumedInputCharacterCount);

        // The period that was spent keeps its record: a roll-over is a new window rather than a cleared counter.
        Assert.Equal(1_200, ledger.ConsumedByPeriod[exhausted.StartsAt]);
    }

    /// <summary>A deployment with no ceiling is counted all the same, because the figure is what an operator sets one from.</summary>
    [Fact]
    public async Task RecordSpendAsync_NoCeilingIsDeclared_StillChargesThePeriodAndAdmitsTheNextRequest()
    {
        // Arrange
        var ledger = new InMemoryEmbeddingSpendLedger();
        var gate = CreateGate(ledger, EmbeddingSpendBudget.Unbounded, new FakeTimeProvider(Midday));

        // Act
        await gate.RecordSpendAsync(
            Substitute.For<IPersistenceSession>(),
            SyntheticMailOwner.Deployment,
            inputCharacterCount: 5_000,
            TestContext.Current.CancellationToken);
        var period = await gate.ReadCurrentPeriodAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(5_000, period.ConsumedInputCharacterCount);
        Assert.Null(period.CeilingInputCharacterCount);
        Assert.Null(period.RemainingInputCharacterCount);
        Assert.True(period.AdmitsRequest);
    }

    /// <summary>An owner's own ceiling stops that owner alone, which is what a per-owner bound is for.</summary>
    [Fact]
    public async Task ReadCurrentPeriodForAsync_OneOwnerHasSpentTheirShare_RefusesThemAndAdmitsEverybodyElse()
    {
        // Arrange
        var ledger = new InMemoryEmbeddingSpendLedger();
        var gate = CreateGate(ledger, BoundedPerOwner(10_000, 1_000), new FakeTimeProvider(Midday));
        await gate.RecordSpendAsync(
            Substitute.For<IPersistenceSession>(),
            SyntheticMailOwner.Deployment,
            inputCharacterCount: 1_000,
            TestContext.Current.CancellationToken);

        // Act
        var spent = await gate.ReadCurrentPeriodForAsync(
            SyntheticMailOwner.Deployment,
            TestContext.Current.CancellationToken);
        var other = await gate.ReadCurrentPeriodForAsync(
            SyntheticMailOwner.Another,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(spent.AdmitsRequest);
        Assert.Equal(EmbeddingSpendBound.Owner, spent.ReachedBound);
        Assert.True(other.AdmitsRequest);
        Assert.Equal(EmbeddingSpendBound.None, other.ReachedBound);

        // What one owner spent is still part of what the deployment spent, which is the figure the wider ceiling reads.
        Assert.Equal(1_000, other.Deployment.ConsumedInputCharacterCount);
        Assert.Equal(0, other.Owner.ConsumedInputCharacterCount);
    }

    /// <summary>The deployment's ceiling is the wider fact, so it is what a refusal names when both are reached.</summary>
    /// <remarks>
    /// Raising one owner's share answers nothing while the deployment itself has stopped spending, so a worker reading
    /// the owner's bound there would pause the wrong thing and an operator would act on the wrong figure.
    /// </remarks>
    [Fact]
    public async Task ReadCurrentPeriodForAsync_BothCeilingsAreReached_NamesTheDeployment()
    {
        // Arrange
        var ledger = new InMemoryEmbeddingSpendLedger();
        var gate = CreateGate(ledger, BoundedPerOwner(1_000, 1_000), new FakeTimeProvider(Midday));
        await gate.RecordSpendAsync(
            Substitute.For<IPersistenceSession>(),
            SyntheticMailOwner.Deployment,
            inputCharacterCount: 1_000,
            TestContext.Current.CancellationToken);

        // Act
        var admission = await gate.ReadCurrentPeriodForAsync(
            SyntheticMailOwner.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmbeddingSpendBound.Deployment, admission.ReachedBound);
    }

    /// <summary>Another owner's spend fills the deployment's window, and everybody under it is refused with it.</summary>
    [Fact]
    public async Task ReadCurrentPeriodForAsync_SomebodyElseFilledTheDeploymentsWindow_RefusesThisOwnerToo()
    {
        // Arrange
        var ledger = new InMemoryEmbeddingSpendLedger();
        var gate = CreateGate(ledger, BoundedPerOwner(1_000, 900), new FakeTimeProvider(Midday));
        await gate.RecordSpendAsync(
            Substitute.For<IPersistenceSession>(),
            SyntheticMailOwner.Another,
            inputCharacterCount: 1_000,
            TestContext.Current.CancellationToken);

        // Act
        var admission = await gate.ReadCurrentPeriodForAsync(
            SyntheticMailOwner.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmbeddingSpendBound.Deployment, admission.ReachedBound);
        Assert.Equal(0, admission.Owner.ConsumedInputCharacterCount);
    }

    /// <summary>Charging names the owner, so what the ledger holds is attributable rather than a deployment total.</summary>
    [Fact]
    public async Task RecordSpendAsync_TwoOwnersSpendingInOnePeriod_ChargesEachToTheirOwnRow()
    {
        // Arrange
        var ledger = new InMemoryEmbeddingSpendLedger();
        var gate = CreateGate(ledger, BoundedPerOwner(10_000, 10_000), new FakeTimeProvider(Midday));
        var periodStart = new DateTimeOffset(2026, 8, 8, 0, 0, 0, TimeSpan.Zero);

        // Act
        await gate.RecordSpendAsync(
            Substitute.For<IPersistenceSession>(),
            SyntheticMailOwner.Deployment,
            inputCharacterCount: 300,
            TestContext.Current.CancellationToken);
        await gate.RecordSpendAsync(
            Substitute.For<IPersistenceSession>(),
            SyntheticMailOwner.Another,
            inputCharacterCount: 700,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(300, ledger.ConsumedByPeriodAndOwner[(periodStart, SyntheticMailOwner.Deployment)]);
        Assert.Equal(700, ledger.ConsumedByPeriodAndOwner[(periodStart, SyntheticMailOwner.Another)]);
        Assert.Equal(1_000, ledger.ConsumedByPeriod[periodStart]);
    }

    /// <summary>Work is performed for somebody, so a charge that names nobody is a defect rather than a deployment charge.</summary>
    [Fact]
    public async Task ReadCurrentPeriodForAsync_AnOwnerNamingNobody_IsRefused()
    {
        // Arrange
        var gate = CreateGate(new InMemoryEmbeddingSpendLedger(), Bounded(1_000), new FakeTimeProvider(Midday));

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => gate.ReadCurrentPeriodForAsync(default, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(
            () => gate.RecordSpendAsync(
                Substitute.For<IPersistenceSession>(),
                default,
                inputCharacterCount: 10,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Constructor_AMissingCollaborator_IsRefused()
    {
        // Arrange
        var ledger = new InMemoryEmbeddingSpendLedger();
        var budget = EmbeddingSpendBudget.Unbounded;
        var timeProvider = new FakeTimeProvider();

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new EmbeddingSpendGate(null!, budget, timeProvider));
        Assert.Throws<ArgumentNullException>(() => new EmbeddingSpendGate(ledger, null!, timeProvider));
        Assert.Throws<ArgumentNullException>(() => new EmbeddingSpendGate(ledger, budget, null!));
    }

    private static EmbeddingSpendBudget Bounded(long ceiling) =>
        EmbeddingSpendBudget.Create(ceiling, 0, TimeSpan.FromDays(1));

    private static EmbeddingSpendBudget BoundedPerOwner(long ceiling, long ownerCeiling) =>
        EmbeddingSpendBudget.Create(ceiling, ownerCeiling, TimeSpan.FromDays(1));

    private static EmbeddingSpendGate CreateGate(
        IEmbeddingSpendLedger ledger,
        EmbeddingSpendBudget budget,
        TimeProvider timeProvider) =>
        new(ledger, budget, timeProvider);
}
