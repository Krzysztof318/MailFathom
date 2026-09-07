// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.AttachmentText.Limits;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the ledger every attachment-ceiling test measures a period from.</summary>
/// <remarks>
/// A fault here reports somebody else's arrangement. A ledger that summed the two steps together would let a
/// description ceiling pass a test that only ever read octets; one that answered the deployment's total for an owner
/// would make a per-owner ceiling test pass while the ceiling bounded everybody; and one that carried a charge across
/// period boundaries would report a roll-over that never happened.
/// </remarks>
public sealed class InMemoryAttachmentDerivationSpendLedgerTests
{
    private static readonly DateTimeOffset Period = new(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReadConsumedAsync_APeriodNobodySpentIn_AnswersWithZeroOnBothScopes()
    {
        // Arrange
        var ledger = new InMemoryAttachmentDerivationSpendLedger();

        // Act
        var totals = await ledger.ReadConsumedAsync(
            Period,
            AttachmentDerivationStep.Extraction,
            SyntheticMailOwner.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, totals.OwnerConsumedUnitCount);
        Assert.Equal(0, totals.DeploymentConsumedUnitCount);
    }

    [Fact]
    public async Task ReadConsumedAsync_TwoOwnersSpendingInOnePeriod_AnswersEachWithTheirOwnBesideTheTotal()
    {
        // Arrange
        var ledger = new InMemoryAttachmentDerivationSpendLedger();
        ledger.Seed(Period, AttachmentDerivationStep.Extraction, SyntheticMailOwner.Deployment, 4_096);
        ledger.Seed(Period, AttachmentDerivationStep.Extraction, SyntheticMailOwner.Another, 512);

        // Act
        var totals = await ledger.ReadConsumedAsync(
            Period,
            AttachmentDerivationStep.Extraction,
            SyntheticMailOwner.Another,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(512, totals.OwnerConsumedUnitCount);
        Assert.Equal(4_608, totals.DeploymentConsumedUnitCount);
    }

    [Fact]
    public async Task ReadConsumedAsync_BothStepsSpentIn_KeepsTheTwoUnitsApart()
    {
        // Arrange
        var ledger = new InMemoryAttachmentDerivationSpendLedger();
        ledger.Seed(Period, AttachmentDerivationStep.Extraction, SyntheticMailOwner.Deployment, 4_096);
        ledger.Seed(Period, AttachmentDerivationStep.Description, SyntheticMailOwner.Deployment, 3);

        // Act
        var described = await ledger.ReadDeploymentConsumedAsync(
            Period,
            AttachmentDerivationStep.Description,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, described);
    }

    [Fact]
    public async Task ReadDeploymentConsumedAsync_APeriodOtherThanTheOneCharged_CountsNoneOfIt()
    {
        // Arrange
        var ledger = new InMemoryAttachmentDerivationSpendLedger();
        ledger.Seed(Period, AttachmentDerivationStep.Extraction, SyntheticMailOwner.Deployment, 4_096);

        // Act
        var next = await ledger.ReadDeploymentConsumedAsync(
            Period.AddDays(1),
            AttachmentDerivationStep.Extraction,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, next);
    }

    [Fact]
    public async Task RecordSpendAsync_ASecondChargeInTheSamePeriod_AddsToTheFirstRatherThanReplacingIt()
    {
        // Arrange
        var ledger = new InMemoryAttachmentDerivationSpendLedger();

        // Act
        await ledger.RecordSpendAsync(
            Session(),
            Period,
            AttachmentDerivationStep.Extraction,
            SyntheticMailOwner.Deployment,
            1_000,
            TestContext.Current.CancellationToken);
        await ledger.RecordSpendAsync(
            Session(),
            Period,
            AttachmentDerivationStep.Extraction,
            SyntheticMailOwner.Deployment,
            24,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            1_024,
            ledger.Consumed[(Period, AttachmentDerivationStep.Extraction, SyntheticMailOwner.Deployment)]);
    }

    /// <summary>A step that consumed nothing writes no row, which is what keeps a period free of charges of zero.</summary>
    [Fact]
    public async Task RecordSpendAsync_NothingConsumed_WritesNoChargeAtAll()
    {
        // Arrange
        var ledger = new InMemoryAttachmentDerivationSpendLedger();

        // Act
        await ledger.RecordSpendAsync(
            Session(),
            Period,
            AttachmentDerivationStep.Description,
            SyntheticMailOwner.Deployment,
            0,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(ledger.Consumed);
    }

    [Fact]
    public async Task RecordSpendAsync_ANegativeCount_IsRefused()
    {
        // Arrange
        var ledger = new InMemoryAttachmentDerivationSpendLedger();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ledger.RecordSpendAsync(
            Session(),
            Period,
            AttachmentDerivationStep.Extraction,
            SyntheticMailOwner.Deployment,
            -1,
            TestContext.Current.CancellationToken));
    }

    private static IgnoredPersistenceSession Session() => new();
}
