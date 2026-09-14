// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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

    private static readonly DateTimeOffset PeriodStart = new(2026, 8, 8, 0, 0, 0, TimeSpan.Zero);

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
            SyntheticMailUser.Deployment,
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
            SyntheticMailUser.Deployment,
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
            SyntheticMailUser.Deployment,
            inputCharacterCount: 5_000,
            TestContext.Current.CancellationToken);
        var period = await gate.ReadCurrentPeriodAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(5_000, period.ConsumedInputCharacterCount);
        Assert.Null(period.CeilingInputCharacterCount);
        Assert.Null(period.RemainingInputCharacterCount);
        Assert.True(period.AdmitsRequest);
    }

    /// <summary>A user's own ceiling stops that user alone, which is what a per-user bound is for.</summary>
    [Fact]
    public async Task ReadCurrentPeriodForAsync_OneUserHasSpentTheirShare_RefusesThemAndAdmitsEverybodyElse()
    {
        // Arrange
        var ledger = new InMemoryEmbeddingSpendLedger();
        var gate = CreateGate(ledger, BoundedPerUser(10_000, 1_000), new FakeTimeProvider(Midday));
        await gate.RecordSpendAsync(
            Substitute.For<IPersistenceSession>(),
            SyntheticMailUser.Deployment,
            inputCharacterCount: 1_000,
            TestContext.Current.CancellationToken);

        // Act
        var spent = await gate.ReadCurrentPeriodForAsync(
            SyntheticMailUser.Deployment,
            TestContext.Current.CancellationToken);
        var other = await gate.ReadCurrentPeriodForAsync(
            SyntheticMailUser.Another,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(spent.AdmitsRequest);
        Assert.Equal(EmbeddingSpendBound.User, spent.ReachedBound);
        Assert.True(other.AdmitsRequest);
        Assert.Equal(EmbeddingSpendBound.None, other.ReachedBound);

        // What one user spent is still part of what the deployment spent, which is the figure the wider ceiling reads.
        Assert.Equal(1_000, other.Deployment.ConsumedInputCharacterCount);
        Assert.Equal(0, other.User.ConsumedInputCharacterCount);
    }

    /// <summary>The deployment's ceiling is the wider fact, so it is what a refusal names when both are reached.</summary>
    /// <remarks>
    /// Raising one user's share answers nothing while the deployment itself has stopped spending, so a worker reading
    /// the user's bound there would pause the wrong thing and an operator would act on the wrong figure.
    /// </remarks>
    [Fact]
    public async Task ReadCurrentPeriodForAsync_BothCeilingsAreReached_NamesTheDeployment()
    {
        // Arrange
        var ledger = new InMemoryEmbeddingSpendLedger();
        var gate = CreateGate(ledger, BoundedPerUser(1_000, 1_000), new FakeTimeProvider(Midday));
        await gate.RecordSpendAsync(
            Substitute.For<IPersistenceSession>(),
            SyntheticMailUser.Deployment,
            inputCharacterCount: 1_000,
            TestContext.Current.CancellationToken);

        // Act
        var admission = await gate.ReadCurrentPeriodForAsync(
            SyntheticMailUser.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmbeddingSpendBound.Deployment, admission.ReachedBound);
    }

    /// <summary>Another user's spend fills the deployment's window, and everybody under it is refused with it.</summary>
    [Fact]
    public async Task ReadCurrentPeriodForAsync_SomebodyElseFilledTheDeploymentsWindow_RefusesThisUserToo()
    {
        // Arrange
        var ledger = new InMemoryEmbeddingSpendLedger();
        var gate = CreateGate(ledger, BoundedPerUser(1_000, 900), new FakeTimeProvider(Midday));
        await gate.RecordSpendAsync(
            Substitute.For<IPersistenceSession>(),
            SyntheticMailUser.Another,
            inputCharacterCount: 1_000,
            TestContext.Current.CancellationToken);

        // Act
        var admission = await gate.ReadCurrentPeriodForAsync(
            SyntheticMailUser.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmbeddingSpendBound.Deployment, admission.ReachedBound);
        Assert.Equal(0, admission.User.ConsumedInputCharacterCount);
    }

    /// <summary>Charging names the user, so what the ledger holds is attributable rather than a deployment total.</summary>
    [Fact]
    public async Task RecordSpendAsync_TwoUsersSpendingInOnePeriod_ChargesEachToTheirOwnRow()
    {
        // Arrange
        var ledger = new InMemoryEmbeddingSpendLedger();
        var gate = CreateGate(ledger, BoundedPerUser(10_000, 10_000), new FakeTimeProvider(Midday));
        var periodStart = new DateTimeOffset(2026, 8, 8, 0, 0, 0, TimeSpan.Zero);

        // Act
        await gate.RecordSpendAsync(
            Substitute.For<IPersistenceSession>(),
            SyntheticMailUser.Deployment,
            inputCharacterCount: 300,
            TestContext.Current.CancellationToken);
        await gate.RecordSpendAsync(
            Substitute.For<IPersistenceSession>(),
            SyntheticMailUser.Another,
            inputCharacterCount: 700,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(300, ledger.ConsumedByPeriodAndUser[(periodStart, SyntheticMailUser.Deployment)]);
        Assert.Equal(700, ledger.ConsumedByPeriodAndUser[(periodStart, SyntheticMailUser.Another)]);
        Assert.Equal(1_000, ledger.ConsumedByPeriod[periodStart]);
    }

    /// <summary>Work is performed for somebody, so a charge that names nobody is a defect rather than a deployment charge.</summary>
    [Fact]
    public async Task ReadCurrentPeriodForAsync_AUserNamingNobody_IsRefused()
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

    /// <summary>
    /// ADR 0014 counts a shared mailbox in full against every user assigned it, so work on it proceeds only while
    /// every one of them is under their ceiling and a refusal is the first that is not.
    /// </summary>
    [Fact]
    public async Task ReadCurrentPeriodForAccountAsync_OneAssignedUserOfTwoHasSpentTheirShare_RefusesTheMailbox()
    {
        // Arrange
        var ledger = new InMemoryEmbeddingSpendLedger();
        ledger.Seed(PeriodStart, SyntheticMailUser.Another, inputCharacterCount: 500);
        var gate = CreateGate(
            ledger,
            BoundedPerUser(10_000, 500),
            new FakeTimeProvider(Midday),
            new StubMailAccountAssignments()
                .Assigning(SyntheticMailUser.Deployment, SyntheticMailAccount.Deployment)
                .Assigning(SyntheticMailUser.Another, SyntheticMailAccount.Deployment));

        // Act
        var admission = await gate.ReadCurrentPeriodForAccountAsync(
            SyntheticMailAccount.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(admission.AdmitsRequest);
        Assert.Equal(EmbeddingSpendBound.User, admission.ReachedBound);
    }

    /// <summary>
    /// A mailbox nobody is assigned has no per-user allowance to be admitted under, and every caller-facing scope
    /// narrows to the accounts somebody is assigned — so embedding it would spend on mail nobody could read, under no
    /// per-user ceiling at all, for as long as it stayed unassigned.
    /// </summary>
    [Fact]
    public async Task ReadCurrentPeriodForAccountAsync_AMailboxNobodyIsAssigned_AdmitsNothingWhateverIsDeclared()
    {
        // Arrange
        var gate = CreateGate(
            new InMemoryEmbeddingSpendLedger(),
            EmbeddingSpendBudget.Unbounded,
            new FakeTimeProvider(Midday),
            new StubMailAccountAssignments());

        // Act
        var admission = await gate.ReadCurrentPeriodForAccountAsync(
            SyntheticMailAccount.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(admission.AdmitsRequest);
        Assert.Equal(EmbeddingSpendBound.User, admission.ReachedBound);
    }

    /// <summary>
    /// What one call sent is one figure however many people are assigned the mailbox, so the deployment's own row
    /// moves by it once while each user is charged in full. A deployment total read off the users' sum would refuse a
    /// mailbox three people share after a third of what the operator declared.
    /// </summary>
    [Fact]
    public async Task RecordAccountSpendAsync_AMailboxTwoUsersShare_ChargesEachInFullAndTheDeploymentOnce()
    {
        // Arrange
        var ledger = new InMemoryEmbeddingSpendLedger();
        var gate = CreateGate(
            ledger,
            BoundedPerUser(10_000, 10_000),
            new FakeTimeProvider(Midday),
            new StubMailAccountAssignments()
                .Assigning(SyntheticMailUser.Deployment, SyntheticMailAccount.Deployment)
                .Assigning(SyntheticMailUser.Another, SyntheticMailAccount.Deployment));

        // Act
        await gate.RecordAccountSpendAsync(
            Substitute.For<IPersistenceSession>(),
            SyntheticMailAccount.Deployment,
            inputCharacterCount: 900,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(900, ledger.ConsumedByPeriodAndUser[(PeriodStart, SyntheticMailUser.Deployment)]);
        Assert.Equal(900, ledger.ConsumedByPeriodAndUser[(PeriodStart, SyntheticMailUser.Another)]);
        Assert.Equal(900, ledger.ConsumedByPeriod[PeriodStart]);
    }

    /// <summary>A mailbox nobody is assigned still moves the deployment's figure, which is what an operator watches.</summary>
    [Fact]
    public async Task RecordAccountSpendAsync_AMailboxNobodyIsAssigned_StillChargesTheDeployment()
    {
        // Arrange
        var ledger = new InMemoryEmbeddingSpendLedger();
        var gate = CreateGate(
            ledger,
            BoundedPerUser(10_000, 10_000),
            new FakeTimeProvider(Midday),
            new StubMailAccountAssignments());

        // Act
        await gate.RecordAccountSpendAsync(
            Substitute.For<IPersistenceSession>(),
            SyntheticMailAccount.Deployment,
            inputCharacterCount: 120,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(120, ledger.ConsumedByPeriod[PeriodStart]);
    }

    [Fact]
    public void Constructor_AMissingCollaborator_IsRefused()
    {
        // Arrange
        var ledger = new InMemoryEmbeddingSpendLedger();
        var budget = EmbeddingSpendBudget.Unbounded;
        var timeProvider = new FakeTimeProvider();

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new EmbeddingSpendGate(null!, new StubMailAccountAssignments(), budget, timeProvider));
        Assert.Throws<ArgumentNullException>(() => new EmbeddingSpendGate(ledger, new StubMailAccountAssignments(), null!, timeProvider));
        Assert.Throws<ArgumentNullException>(() => new EmbeddingSpendGate(ledger, new StubMailAccountAssignments(), budget, null!));
    }

    private static EmbeddingSpendBudget Bounded(long ceiling) =>
        EmbeddingSpendBudget.Create(ceiling, 0, TimeSpan.FromDays(1));

    private static EmbeddingSpendBudget BoundedPerUser(long ceiling, long userCeiling) =>
        EmbeddingSpendBudget.Create(ceiling, userCeiling, TimeSpan.FromDays(1));

    private static EmbeddingSpendGate CreateGate(
        IEmbeddingSpendLedger ledger,
        EmbeddingSpendBudget budget,
        TimeProvider timeProvider,
        StubMailAccountAssignments? assignments = null) =>
        new(ledger, assignments ?? new StubMailAccountAssignments(), budget, timeProvider);
}
